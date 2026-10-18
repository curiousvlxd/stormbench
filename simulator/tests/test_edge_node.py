import json
from types import SimpleNamespace

import pytest
from conftest import wait_until
from paho.mqtt.packettypes import PacketTypes
from paho.mqtt.reasoncodes import ReasonCode

from stormbench_sim.sparkplug import DataType, EdgeNode, Metric, Payload, decode, encode

NCMD = "spBv1.0/Astlingen/NCMD/Sim01"
STATE = "spBv1.0/STATE/Scada01"
COMMANDS = ("Scenario/Start", "Scenario/Stop")


class FakeClient:
    """The part of paho's Client the edge node uses. Callbacks run on the caller's thread."""

    def __init__(self, client_id):
        self.client_id = client_id
        self.connected = False
        self.will = None
        self.keepalive = None
        self.subscriptions = []
        self.published = []
        self.on_connect = self.on_message = self.on_disconnect = None

    def will_set(self, topic, payload, qos, retain):
        self.will = (topic, decode(payload), qos, retain)

    def connect(self, host, port, keepalive):
        self.keepalive = keepalive
        self.connected = True
        self.on_connect(self, None, None, ReasonCode(PacketTypes.CONNACK, "Success"), None)

    def loop_start(self):
        pass

    def loop_stop(self):
        pass

    def subscribe(self, topics):
        self.subscriptions += topics

    def publish(self, topic, payload, qos, retain):
        self.published.append((topic, decode(payload), qos, retain))
        return SimpleNamespace(wait_for_publish=lambda timeout=None: None)

    def disconnect(self):
        if self.connected:
            self.drop()

    def is_connected(self):
        return self.connected

    def drop(self):
        self.connected = False
        self.on_disconnect(self, None, None, ReasonCode(PacketTypes.DISCONNECT, "Normal disconnection"), None)

    def deliver(self, topic, payload: bytes):
        self.on_message(self, None, SimpleNamespace(topic=topic, payload=payload))

    def host_state(self, online, timestamp):
        self.deliver(STATE, json.dumps({"online": online, "timestamp": timestamp}).encode())

    def messages(self, message_type):
        return [p for t, p, _, _ in self.published if t == f"spBv1.0/Astlingen/{message_type}/Sim01"]


class FakeBroker:
    """Client factory: one new client per MQTT session."""

    def __init__(self):
        self.clients = []

    def __call__(self, client_id):
        self.clients.append(FakeClient(client_id))
        return self.clients[-1]

    @property
    def client(self):
        return self.clients[-1]


@pytest.fixture
def broker():
    return FakeBroker()


@pytest.fixture
def node(broker):
    edge = EdgeNode(
        "broker",
        1883,
        "Astlingen",
        "Sim01",
        "Scada01",
        [Metric("Rain/Intensity", DataType.DOUBLE, 0.0), Metric("Scenario/State", DataType.STRING, "idle")],
        commands=COMMANDS,
        client_factory=broker,
        reconnect_delay_s=0.01,
    )
    edge.start()
    wait_until(lambda: broker.clients and broker.client.connected)
    yield edge
    edge.stop()


def command(*metrics):
    return encode(Payload(1, None, list(metrics)))


def test_will_is_the_ndeath_with_the_session_bdseq(node, broker):
    topic, death, qos, retain = broker.client.will
    assert topic == "spBv1.0/Astlingen/NDEATH/Sim01"
    assert (qos, retain) == (1, False)
    assert death.seq is None
    assert death.metrics == [Metric("bdSeq", DataType.INT64, 0)]
    assert broker.client.client_id == "Astlingen-Sim01"
    assert broker.client.keepalive == 10


def test_no_birth_before_the_primary_host_is_online(node, broker):
    client = broker.client
    assert (NCMD, 1) in client.subscriptions
    assert (STATE, 1) in client.subscriptions
    node.publish({"Rain/Intensity": 3.5})
    client.host_state(False, 100)
    assert client.published == []
    assert not node.is_born


def test_older_state_is_ignored(node, broker):
    client = broker.client
    client.host_state(False, 200)
    client.host_state(True, 100)
    assert client.messages("NBIRTH") == []
    client.host_state(True, 300)
    assert len(client.messages("NBIRTH")) == 1


def test_nbirth_declares_bdseq_rebirth_commands_and_every_metric(node, broker):
    client = broker.client
    node.publish({"Rain/Intensity": 3.5})
    client.host_state(True, 1)
    (birth,) = client.messages("NBIRTH")
    assert birth.seq == 0
    assert birth.metrics == [
        Metric("bdSeq", DataType.INT64, 0),
        Metric("Node Control/Rebirth", DataType.BOOLEAN, False),
        Metric("Scenario/Start", DataType.BOOLEAN, False),
        Metric("Scenario/Stop", DataType.BOOLEAN, False),
        Metric("Rain/Intensity", DataType.DOUBLE, 3.5),
        Metric("Scenario/State", DataType.STRING, "idle"),
    ]
    assert all(qos == 0 and not retain for _, _, qos, retain in client.published)


def test_ndata_seq_increments_and_wraps(node, broker):
    client = broker.client
    client.host_state(True, 1)
    for i in range(300):
        node.publish({"Rain/Intensity": float(i + 1)})
    seqs = [p.seq for p in client.messages("NDATA")]
    assert seqs[:3] == [1, 2, 3]
    assert seqs[254:257] == [255, 0, 1]
    assert len(seqs) == 300


def test_report_by_exception(node, broker):
    client = broker.client
    client.host_state(True, 1)
    node.publish({"Rain/Intensity": 0.0, "Scenario/State": "running"})
    node.publish({"Rain/Intensity": 0, "Scenario/State": "running"})
    node.publish({"Rain/Intensity": 2})
    data = client.messages("NDATA")
    assert [p.metrics for p in data] == [
        [Metric("Scenario/State", DataType.STRING, "running")],
        [Metric("Rain/Intensity", DataType.DOUBLE, 2.0)],
    ]
    with pytest.raises(KeyError):
        node.publish({"Not/Declared": 1.0})


def test_rebirth_repeats_bdseq_and_restarts_seq(node, broker):
    client = broker.client
    client.host_state(True, 1)
    node.publish({"Rain/Intensity": 1.0})
    node.publish({"Rain/Intensity": 2.0})
    client.deliver(NCMD, command(Metric("Node Control/Rebirth", DataType.BOOLEAN, True)))
    births = client.messages("NBIRTH")
    assert len(births) == 2
    assert births[1].seq == 0
    assert births[1].find("bdSeq").value == 0
    assert births[1].find("Rain/Intensity").value == 2.0
    node.publish({"Rain/Intensity": 3.0})
    assert client.messages("NDATA")[-1].seq == 1


def test_host_offline_ends_the_session_and_the_next_one_uses_the_next_bdseq(node, broker):
    first = broker.client
    first.host_state(True, 1)
    first.host_state(False, 1)
    (death,) = first.messages("NDEATH")
    assert death.find("bdSeq").value == 0
    assert [q for t, _, q, _ in first.published if t.endswith("NDEATH/Sim01")] == [1]
    assert not first.connected
    wait_until(lambda: len(broker.clients) == 2 and broker.client.connected)
    second = broker.client
    assert second.will[1].find("bdSeq").value == 1
    second.host_state(False, 1)
    assert second.messages("NBIRTH") == []
    second.host_state(True, 2)
    assert second.messages("NBIRTH")[0].find("bdSeq").value == 1
    assert node.bd_seq == 1


def test_lost_connection_starts_a_new_session(node, broker):
    broker.client.host_state(True, 1)
    broker.client.drop()
    wait_until(lambda: len(broker.clients) == 2 and broker.client.connected)
    assert broker.client.will[1].find("bdSeq").value == 1
    node.publish({"Rain/Intensity": 5.0})
    assert broker.client.published == []


def test_ncmd_reaches_the_command_callback(node, broker):
    received = []
    node.on_command = received.append
    client = broker.client
    client.host_state(True, 1)
    client.deliver(
        NCMD,
        command(
            Metric("Scenario/Start", DataType.BOOLEAN, True),
            Metric("Scenario/Storm", DataType.STRING, "2000-05-29-3d"),
            Metric("Scenario/Pace", DataType.DOUBLE, 0.0),
        ),
    )
    client.deliver(NCMD, b"\xff not protobuf")
    (cmd,) = received
    assert cmd.find("Scenario/Start").value is True
    assert cmd.find("Scenario/Storm").value == "2000-05-29-3d"


def test_stop_publishes_ndeath_and_disconnects(broker):
    edge = EdgeNode("broker", 1883, "Astlingen", "Sim01", "Scada01", [], client_factory=broker)
    edge.start()
    wait_until(lambda: broker.clients and broker.client.connected)
    broker.client.host_state(True, 1)
    edge.stop()
    client = broker.client
    assert client.published[-1][0] == "spBv1.0/Astlingen/NDEATH/Sim01"
    assert client.published[-1][2] == 1
    assert not client.connected
    assert len(broker.clients) == 1
