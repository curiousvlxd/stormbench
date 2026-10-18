from __future__ import annotations

import json
import logging
import threading
from collections.abc import Callable, Iterable, Mapping

import paho.mqtt.client as mqtt
from google.protobuf.message import DecodeError

from .codec import DataType, Metric, Payload, Value, decode, encode, now_ms

log = logging.getLogger(__name__)

NAMESPACE = "spBv1.0"
BD_SEQ = "bdSeq"
REBIRTH = "Node Control/Rebirth"
KEEPALIVE_S = 10


def paho_client(client_id: str) -> mqtt.Client:
    """Clean session and no automatic reconnect: every session is a new client with its own will."""
    return mqtt.Client(
        mqtt.CallbackAPIVersion.VERSION2,
        client_id=client_id,
        clean_session=True,
        protocol=mqtt.MQTTv311,
        reconnect_on_failure=False,
    )


def _coerce(datatype: int, value: Value) -> Value:
    if value is None:
        return None
    if datatype in (DataType.INT64, DataType.UINT64):
        return int(value)
    if datatype == DataType.DOUBLE:
        return float(value)
    if datatype == DataType.BOOLEAN:
        return bool(value)
    if datatype == DataType.STRING:
        return str(value)
    return value


class EdgeNode:
    """Edge node without devices. `metrics` declares every node metric with its initial value;
    `commands` are Boolean NCMD metrics declared as false in every NBIRTH. NCMD payloads other than
    a rebirth request go to `on_command`, called on the MQTT network thread."""

    def __init__(
        self,
        host: str,
        port: int,
        group_id: str,
        node_id: str,
        primary_host_id: str,
        metrics: Iterable[Metric],
        commands: Iterable[str] = (),
        client_factory: Callable[[str], mqtt.Client] = paho_client,
        reconnect_delay_s: float = 2.0,
    ):
        self.host, self.port = host, port
        self.group_id, self.node_id, self.primary_host_id = group_id, node_id, primary_host_id
        self.client_id = f"{group_id}-{node_id}"
        self.commands = tuple(commands)
        self.on_command: Callable[[Payload], None] | None = None
        self._metrics = {m.name: Metric(m.name, m.datatype, _coerce(m.datatype, m.value)) for m in metrics}
        self._client_factory = client_factory
        self._reconnect_delay_s = reconnect_delay_s
        self._lock = threading.RLock()
        self._stopping = threading.Event()
        self._thread: threading.Thread | None = None
        self._client: mqtt.Client | None = None
        self._ended = threading.Event()
        self._bd_seq = 0
        self._seq = 0
        self._born = False
        self._host_online = False
        self._host_timestamp: int | None = None

    def topic(self, message_type: str) -> str:
        return f"{NAMESPACE}/{self.group_id}/{message_type}/{self.node_id}"

    @property
    def state_topic(self) -> str:
        return f"{NAMESPACE}/STATE/{self.primary_host_id}"

    @property
    def is_born(self) -> bool:
        with self._lock:
            return self._born

    @property
    def bd_seq(self) -> int:
        """bdSeq of the current (or last) MQTT session."""
        with self._lock:
            return self._bd_seq

    def start(self) -> None:
        self._thread = threading.Thread(target=self._run, name=f"sparkplug-{self.node_id}", daemon=True)
        self._thread.start()

    def stop(self, timeout_s: float = 5.0) -> None:
        """Orderly shutdown: NDEATH, disconnect, no new session."""
        self._stopping.set()
        if self._thread is not None:
            self._thread.join(timeout_s)

    def publish(self, values: Mapping[str, Value]) -> None:
        """Store the values; when born, publish the changed ones as NDATA (report by exception)."""
        with self._lock:
            changed = []
            for name, value in values.items():
                current = self._metrics.get(name)
                if current is None:
                    raise KeyError(f"metric {name!r} is not declared in the NBIRTH")
                value = _coerce(current.datatype, value)
                if value != current.value:
                    self._metrics[name] = Metric(name, current.datatype, value)
                    changed.append(self._metrics[name])
            if changed and self._born and self._client is not None:
                self._seq = (self._seq + 1) % 256
                self._send(self._client, "NDATA", Payload(now_ms(), self._seq, changed))

    def _run(self) -> None:
        first = True
        while not self._stopping.is_set():
            if not first:
                with self._lock:
                    self._bd_seq = (self._bd_seq + 1) % 256
            first = False
            try:
                self._run_session()
            except Exception as exc:
                log.warning("MQTT session failed: %s", exc)
            if self._stopping.wait(self._reconnect_delay_s):
                break

    def _run_session(self) -> None:
        client = self._client_factory(self.client_id)
        ended = threading.Event()
        with self._lock:
            bd_seq = self._bd_seq
            self._client, self._ended = client, ended
            self._born = self._host_online = False
        client.on_connect = self._on_connect
        client.on_message = self._on_message
        client.on_disconnect = self._on_disconnect
        client.will_set(self.topic("NDEATH"), self._death(bd_seq), qos=1, retain=False)
        try:
            client.connect(self.host, self.port, keepalive=KEEPALIVE_S)
            client.loop_start()
            while not ended.wait(0.2):
                if self._stopping.is_set():
                    self._end_session(client, wait=True)
                    break
        finally:
            with self._lock:
                self._born = False
                self._client = None
            client.loop_stop()
        log.info("Session with bdSeq %d ended", bd_seq)

    def _on_connect(self, client: mqtt.Client, _userdata, _flags, reason_code, _properties) -> None:
        if reason_code.is_failure:
            log.warning("Broker refused the connection: %s", reason_code)
            client.disconnect()
            return
        log.info("Connected to %s:%s, bdSeq %d", self.host, self.port, self.bd_seq)
        client.subscribe([(self.topic("NCMD"), 1), (self.state_topic, 1)])

    def _on_disconnect(self, client: mqtt.Client, _userdata, _flags, _reason_code, _properties) -> None:
        with self._lock:
            if client is not self._client:
                return
            self._born = False
            self._ended.set()

    def _on_message(self, client: mqtt.Client, _userdata, message: mqtt.MQTTMessage) -> None:
        if client is not self._client:
            return
        if message.topic == self.state_topic:
            self._on_host_state(client, message.payload)
            return
        if message.topic != self.topic("NCMD"):
            return
        try:
            payload = decode(message.payload)
        except DecodeError:
            log.warning("Undecodable Sparkplug payload on %s", message.topic)
            return
        rebirth = payload.find(REBIRTH)
        if rebirth is not None and rebirth.value is True:
            log.info("Rebirth requested by NCMD")
            with self._lock:
                if self._host_online:
                    self._publish_birth(client)
        elif self.on_command is not None:
            self.on_command(payload)

    def _on_host_state(self, client: mqtt.Client, raw: bytes) -> None:
        try:
            state = json.loads(raw)
            online, timestamp = state["online"], state["timestamp"]
        except (ValueError, KeyError, TypeError):
            return
        if not isinstance(online, bool) or isinstance(timestamp, bool) or not isinstance(timestamp, int):
            return
        with self._lock:
            if self._host_timestamp is not None and timestamp < self._host_timestamp:
                return
            self._host_timestamp = timestamp
            was_online, self._host_online = self._host_online, online
            if online and not was_online and not self._born:
                self._publish_birth(client)
        if was_online and not online:
            log.warning("Primary host went offline: NDEATH and new session")
            self._end_session(client, wait=False)

    def _publish_birth(self, client: mqtt.Client) -> None:
        """NBIRTH with seq 0: bdSeq, rebirth, the declared commands and every metric (lock held)."""
        self._seq = 0
        metrics = [
            Metric(BD_SEQ, DataType.INT64, self._bd_seq),
            Metric(REBIRTH, DataType.BOOLEAN, False),
            *(Metric(name, DataType.BOOLEAN, False) for name in self.commands),
            *self._metrics.values(),
        ]
        self._send(client, "NBIRTH", Payload(now_ms(), 0, metrics))
        self._born = True
        log.info("NBIRTH published, bdSeq %d", self._bd_seq)

    def _end_session(self, client: mqtt.Client, wait: bool) -> None:
        """NDEATH (QoS 1) and disconnect. `wait` for the broker's ack: not on the network thread."""
        with self._lock:
            self._born = False
            info = None
            if client.is_connected():
                info = client.publish(self.topic("NDEATH"), self._death(self._bd_seq), qos=1, retain=False)
        if wait and info is not None:
            try:
                info.wait_for_publish(2.0)
            except (RuntimeError, ValueError) as exc:
                log.warning("NDEATH not acknowledged: %s", exc)
        client.disconnect()

    def _send(self, client: mqtt.Client, message_type: str, payload: Payload) -> None:
        client.publish(self.topic(message_type), encode(payload), qos=0, retain=False)

    def _death(self, bd_seq: int) -> bytes:
        return encode(Payload(now_ms(), None, [Metric(BD_SEQ, DataType.INT64, bd_seq)]))
