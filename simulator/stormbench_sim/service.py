from __future__ import annotations

import argparse
import json
import logging
import queue
import signal
import threading
from dataclasses import dataclass
from datetime import datetime, timedelta
from pathlib import Path

from . import uns_contract as uns
from .control import Mode
from .kpi import SPILL_EPS_M3
from .network import Controls, Network, load_network, read_kinds, read_layout, read_map, read_period
from .pipeline import execute
from .runner import FieldFaults, RunResult, RunSpec, StepRecord, run
from .sparkplug import DataType, EdgeNode, Metric, Payload, Value
from .uns_contract import ScenarioState

log = logging.getLogger(__name__)


@dataclass(frozen=True)
class Storm:
    id: str
    label: str
    description: str
    period: tuple[datetime, datetime] | None


@dataclass(frozen=True)
class Algorithm:
    """`center`: who proposes the setpoints in mode 3, "scada" (the Optimization module over the
    UNS) or "simulator" (this process plays the center with MPC, ADR 0017); None for the PLC alone."""

    id: str
    label: str
    description: str
    mode: Mode
    center: str | None = None


STORMS = (
    Storm(
        "2000-05-30",
        "30 May 2000",
        "Medium storm, where EFD spills less",
        (datetime(2000, 5, 30, 0, 0), datetime(2000, 5, 31, 12, 0)),
    ),
    Storm(
        "2000-08-19",
        "19 August 2000",
        "Most rain of the year, short and heavy",
        (datetime(2000, 8, 19, 6, 0), datetime(2000, 8, 20, 6, 0)),
    ),
    Storm(
        "2000-10-04",
        "4 October 2000",
        "Largest overflow of the year",
        (datetime(2000, 10, 4, 12, 0), datetime(2000, 10, 5, 12, 0)),
    ),
    Storm(
        "2000-05-29-3d",
        "29 May to 1 June 2000",
        "Three days, the validation window of the golden tests",
        (datetime(2000, 5, 29, 0, 0), datetime(2000, 6, 1, 0, 0)),
    ),
    Storm("2000", "Year 2000", "The whole benchmark year", None),
)
ALGORITHMS = (
    Algorithm("base", "Base Case", "Local stations hold the fixed throttle settings", Mode.LOCAL),
    Algorithm(
        "efd-plc",
        "EFD in the PLC",
        "Equal Filling Degree in IEC 61131-3, rules as SWMM executes them",
        Mode.EFD_PUBLISHED,
    ),
    Algorithm(
        "efd-plc-intended",
        "EFD in the PLC, as intended",
        "Rule priorities fixed: 1 m threshold, emptiest tank throttled",
        Mode.EFD_INTENDED,
    ),
    Algorithm(
        "efd-center",
        "EFD from the center",
        "Scada proposes over the UNS, the PLC stations validate",
        Mode.EFD_CENTER,
        "scada",
    ),
    Algorithm(
        "mpc-center",
        "MPC, simulator as center",
        "Model predictive control with a perfect inflow forecast, computed by the simulator in the "
        "role of the center (not by Scada); the PLC stations validate",
        Mode.EFD_CENTER,
        "simulator",
    ),
)
CENTER_OPTION = {"scada": "uns", "simulator": "mpc"}
PACES = (0.5, 0.2, 0.1, 0.05, 0.0)
DEFAULT_STORM = "2000-05-30"
DEFAULT_ALGORITHM = "efd-center"
DEFAULT_PACE = 0.1
MAX_PACE_S = 5.0
BUSY = (ScenarioState.PREPARING, ScenarioState.RUNNING)

_EPOCH = datetime(1970, 1, 1)


def unix_ms(moment: datetime) -> int:
    """Model time taken as UTC."""
    return (moment - _EPOCH) // timedelta(milliseconds=1)


def control_steps(start: datetime, end: datetime, step_s: int) -> int:
    """Rows the runner records for a window. pyswmm ends the iteration on the stride that reaches
    the end, so that stride is no row: three days of 5 min steps give 863 rows, the year 105,406."""
    return -(-round((end - start).total_seconds()) // step_s) - 1


def storm_period(storm: Storm, model_period: tuple[datetime, datetime]) -> tuple[datetime, datetime]:
    return storm.period or model_period


def catalog(net: Network, model_period: tuple[datetime, datetime]) -> dict:
    """Metric Scenario/Catalog: what the test console can run."""
    storms = []
    for storm in STORMS:
        start, end = storm_period(storm, model_period)
        storms.append(
            {
                "id": storm.id,
                "label": storm.label,
                "description": storm.description,
                "start": start.isoformat(),
                "end": end.isoformat(),
                "steps": control_steps(start, end, net.control_step_s),
            }
        )
    return {
        "storms": storms,
        "algorithms": [
            {
                "id": a.id,
                "label": a.label,
                "description": a.description,
                "mode": int(a.mode),
                "center": a.center,
            }
            for a in ALGORITHMS
        ],
        "paces": list(PACES),
        "defaultStorm": DEFAULT_STORM,
        "defaultAlgorithm": DEFAULT_ALGORITHM,
        "defaultPace": DEFAULT_PACE,
    }


def network_description(net: Network) -> dict:
    """Metric Model/Network: the network as the HMI draws it (read from the model at run time,
    published on the local broker only; the model itself is not redistributed)."""
    layout = read_layout(net)
    node_kinds, link_kinds = read_kinds(net)
    return {
        "name": net.name,
        "controlStepS": net.control_step_s,
        "tankMaxDepthM": net.tank_max_depth_m,
        "wwtp": net.wwtp_outfall,
        "tanks": list(net.tanks),
        "overflowPoints": list(net.overflow_points),
        "stations": [
            {"valve": s.valve, "tank": s.tank, "baseSetting": s.base_setting, "lowSetting": s.low_setting}
            for s in net.stations
        ],
        "nodes": [
            {"id": node, "kind": node_kinds[node], "x": x, "y": y}
            for node, (x, y) in layout.coords.items()
            if node in node_kinds
        ],
        "links": [{"id": link, "kind": link_kinds[link], "from": a, "to": b} for link, a, b in layout.links],
        "labelOffsets": {point: list(offset) for point, offset in net.label_offsets.items()},
        "map": read_map(net),
    }


def telemetry_reset(net: Network) -> dict[str, Value]:
    values: dict[str, Value] = {
        uns.RAIN_INTENSITY: 0.0,
        uns.OVERFLOW_TOTAL: 0.0,
        uns.WWTP_INFLOW: 0.0,
        uns.BASE_CASE_OVERFLOW_TOTAL: 0.0,
        uns.MISMATCHES: 0,
    }
    for point in net.overflow_points:
        values[uns.overflow_active(point)] = False
        values[uns.overflow_volume(point)] = 0.0
    return values


def _datatype(value: Value) -> DataType:
    if isinstance(value, bool):
        return DataType.BOOLEAN
    if isinstance(value, int):
        return DataType.INT64
    if isinstance(value, float):
        return DataType.DOUBLE
    return DataType.STRING


def initial_metrics(net: Network) -> list[Metric]:
    """Every Sim01 metric with its value before the first run; the datatype follows the value."""
    compact = {"separators": (",", ":")}
    values: dict[str, Value] = {
        uns.NETWORK: json.dumps(network_description(net), **compact),
        uns.CATALOG: json.dumps(catalog(net, read_period(net)), **compact),
        uns.STATE: ScenarioState.IDLE.value,
        uns.RUN: 0,
        uns.STORM: "",
        uns.ALGORITHM: "",
        uns.PACE: DEFAULT_PACE,
        uns.STEP: 0,
        uns.STEPS: 0,
        uns.CLOCK: 0,
        uns.MESSAGE: "",
        uns.CENTER_LINK_CUT: False,
        uns.CENTER_LINK_UP: True,
        uns.STUCK_GATE: "",
        uns.PLC_STEP: 0,
        uns.FROZEN_SENSOR: "",
        **{uns.valve_opening(s.valve): s.base_setting for s in net.stations},
        **{uns.true_level(tank): 0.0 for tank in net.tanks},
        **telemetry_reset(net),
    }
    return [Metric(name, _datatype(value), value) for name, value in values.items()]


class _Telemetry:
    """Field values of one run: the runner's steps next to the Base Case reference of the window."""

    def __init__(self, net: Network, reference: RunResult):
        self.points = net.overflow_points
        self.valves = tuple(s.valve for s in net.stations)
        self.tanks = net.tanks
        self.reference_rows = [sum(row) for row in reference.flood_cum.tolist()]
        self.reference_total = reference.totals["flood_total_m3"]
        self.previous = (0.0,) * len(self.points)

    def step(self, rec: StepRecord) -> dict[str, Value]:
        values: dict[str, Value] = {
            uns.STEP: rec.k + 1,
            uns.CLOCK: unix_ms(rec.time),
            uns.RAIN_INTENSITY: sum(rec.rain_mmph) / len(rec.rain_mmph) if rec.rain_mmph else 0.0,
            uns.WWTP_INFLOW: rec.wwtp_cum,
            uns.BASE_CASE_OVERFLOW_TOTAL: self.reference_rows[min(rec.k, len(self.reference_rows) - 1)],
            uns.MISMATCHES: rec.mismatches,
        }
        if rec.center_link_up is not None:
            values[uns.CENTER_LINK_UP] = rec.center_link_up
        if rec.plc_step is not None:
            values[uns.PLC_STEP] = rec.plc_step
        if rec.openings is not None:
            values.update({uns.valve_opening(v): o for v, o in zip(self.valves, rec.openings, strict=True)})
        values.update({uns.true_level(t): d for t, d in zip(self.tanks, rec.depths, strict=True)})
        values.update(self._overflow(rec.flood_cum))
        return values

    def final(self, result: RunResult, end: datetime) -> dict[str, Value]:
        """State at the end of the window: the last stride reaches it without being a row, and the
        totals saved with the result include it."""
        values: dict[str, Value] = {
            uns.CLOCK: unix_ms(end),
            uns.WWTP_INFLOW: result.totals["wwtp_inflow_m3"],
            uns.BASE_CASE_OVERFLOW_TOTAL: self.reference_total,
        }
        values.update(self._overflow(tuple(result.totals["flood_m3"][p] for p in self.points)))
        return values

    def _overflow(self, volumes: tuple[float, ...]) -> dict[str, Value]:
        values: dict[str, Value] = {uns.OVERFLOW_TOTAL: sum(volumes)}
        for point, volume, before in zip(self.points, volumes, self.previous, strict=True):
            values[uns.overflow_volume(point)] = volume
            values[uns.overflow_active(point)] = volume - before > SPILL_EPS_M3
        self.previous = volumes
        return values


def _flag(command: Payload, name: str) -> bool:
    metric = command.find(name)
    return metric is not None and metric.value is True


def _text(command: Payload, name: str) -> str:
    metric = command.find(name)
    return metric.value if metric is not None and isinstance(metric.value, str) else ""


def _pace(command: Payload) -> float:
    metric = command.find(uns.PACE)
    if metric is None:
        return DEFAULT_PACE
    if isinstance(metric.value, bool) or not isinstance(metric.value, int | float):
        return float("nan")
    return float(metric.value)


class ScenarioService:
    """Test console of Sim01. `node` publishes the values (report by exception); `control` is
    "plc" for the testbed, "python" runs the Python reference controller instead of the PLC."""

    def __init__(
        self,
        net: Network,
        node: EdgeNode,
        results_dir: Path,
        work_dir: Path,
        plc_host: str = "localhost",
        plc_port: int = 502,
        plc_timeout_s: float = 60.0,
        control: str = "plc",
    ):
        self.net, self.node, self.control = net, node, control
        self.results_dir, self.work_dir = Path(results_dir), Path(work_dir)
        self.plc_host, self.plc_port, self.plc_timeout_s = plc_host, plc_port, plc_timeout_s
        self.model_period = read_period(net)
        self._storms = {s.id: s for s in STORMS}
        self._algorithms = {a.id: a for a in ALGORITHMS}
        self._commands: queue.SimpleQueue[Payload | None] = queue.SimpleQueue()
        self._lock = threading.Lock()
        self._state = ScenarioState.IDLE
        self._run = 0
        self._stop = threading.Event()
        self._link_cut = threading.Event()
        self._faults = FieldFaults()
        self._worker: threading.Thread | None = None

    def submit(self, command: Payload) -> None:
        """NCMD from the MQTT network thread: only enqueue."""
        self._commands.put(command)

    def shutdown(self) -> None:
        """Safe in a signal handler: SimpleQueue.put is reentrant."""
        self._commands.put(None)

    def serve(self) -> None:
        """Handle commands in arrival order until shutdown(), then stop the current run."""
        while True:
            try:
                command = self._commands.get(timeout=0.5)
            except queue.Empty:
                continue
            if command is None:
                break
            self.handle(command)
        with self._lock:
            self._stop.set()
        self.join(self.plc_timeout_s + 5)

    def handle(self, command: Payload) -> None:
        for name, choices, apply in (
            (uns.STUCK_GATE, [s.valve for s in self.net.stations], self._set_stuck_gate),
            (uns.FROZEN_SENSOR, list(self.net.tanks), self._set_frozen_sensor),
        ):
            metric = command.find(name)
            if metric is not None and isinstance(metric.value, str):
                if metric.value in ("", *choices):
                    apply(metric.value or None)
                    self.node.publish({name: metric.value})
                else:
                    self.node.publish({uns.MESSAGE: f"Unknown target '{metric.value}' for {name}."})
        cut = command.find(uns.CENTER_LINK_CUT)
        if cut is not None and isinstance(cut.value, bool):
            self.cut_center_link(cut.value)
        if _flag(command, uns.STOP):
            self.stop()
        if _flag(command, uns.START):
            self.start(_text(command, uns.STORM), _text(command, uns.ALGORITHM), _pace(command))

    def start(self, storm_id: str, algorithm_id: str, pace: float) -> bool:
        storm, algorithm = self._storms.get(storm_id), self._algorithms.get(algorithm_id)
        with self._lock:
            if self._state in BUSY:
                reason = "A storm is already running."
            elif storm is None:
                reason = f"Unknown storm '{storm_id}'."
            elif algorithm is None:
                reason = f"Unknown algorithm '{algorithm_id}'."
            elif not 0.0 <= pace <= MAX_PACE_S:
                reason = f"Pace must be between 0 and {MAX_PACE_S:g} s per step."
            else:
                reason = None
            if reason is not None:
                log.warning("Start refused: %s", reason)
                self.node.publish({uns.MESSAGE: reason})
                return False
            self._run += 1
            self._state = ScenarioState.PREPARING
            self._stop = stop = threading.Event()
            period = storm_period(storm, self.model_period)
            self.node.publish(
                {
                    uns.RUN: self._run,
                    uns.STORM: storm.id,
                    uns.ALGORITHM: algorithm.id,
                    uns.PACE: pace,
                    uns.STEP: 0,
                    uns.STEPS: control_steps(*period, self.net.control_step_s),
                    uns.CLOCK: unix_ms(period[0]),
                    uns.MESSAGE: "",
                    **telemetry_reset(self.net),
                    uns.STATE: ScenarioState.PREPARING,
                }
            )
            run_no = self._run
        log.info("Run %d: storm %s, algorithm %s, pace %g s", run_no, storm.id, algorithm.id, pace)
        if self._worker is not None:
            self._worker.join()
        self._worker = threading.Thread(
            target=self._execute,
            args=(storm, algorithm, pace, period, stop),
            name=f"run-{run_no}",
            daemon=True,
        )
        self._worker.start()
        return True

    def _set_stuck_gate(self, valve: str | None) -> None:
        self._faults.stuck_gate = valve
        log.info("Stuck gate: %s", valve or "none")

    def _set_frozen_sensor(self, tank: str | None) -> None:
        self._faults.frozen_sensor = tank
        log.info("Frozen level sensor: %s", tank or "none")

    def cut_center_link(self, cut: bool) -> None:
        """Fault injection: while cut, mode-3 steps reach the PLC with CenterOk = 0. Takes effect
        at the next step of a running storm and stays for the next storms until cleared."""
        if cut:
            self._link_cut.set()
        else:
            self._link_cut.clear()
        log.info("Link to the center %s", "cut" if cut else "restored")
        self.node.publish({uns.CENTER_LINK_CUT: cut})

    def stop(self) -> None:
        with self._lock:
            if self._state in BUSY:
                self._stop.set()
                return
            self.node.publish({uns.MESSAGE: "No storm is running."})

    def join(self, timeout_s: float | None = None) -> None:
        """Wait for the current run to end (tests, shutdown)."""
        if self._worker is not None:
            self._worker.join(timeout_s)

    def _execute(
        self,
        storm: Storm,
        algorithm: Algorithm,
        pace: float,
        period: tuple[datetime, datetime],
        stop: threading.Event,
    ) -> None:
        name = f"console-{storm.id}-{algorithm.id}"
        try:
            reference = run(
                RunSpec(f"console-{storm.id}-reference", self.net, Controls.BASE, period=period, stop=stop),
                self.work_dir,
                progress=None,
            )
            if stop.is_set():
                self._set_state(ScenarioState.STOPPED)
                return
            telemetry = _Telemetry(self.net, reference)
            self._set_state(ScenarioState.RUNNING)
            args = argparse.Namespace(
                plc_host=self.plc_host,
                plc_port=self.plc_port,
                plc_timeout=self.plc_timeout_s,
                plc_attempts=3,
                no_shadow=False,
                center=CENTER_OPTION.get(algorithm.center or "scada", "uns"),
                center_variant="efd-published",
                start=period[0].isoformat(),
                end=period[1].isoformat(),
                outage=None,
                link_cut=self._link_cut,
                faults=self._faults,
                pace=pace,
                work_dir=str(self.work_dir),
                results_dir=str(self.results_dir),
            )
            result = execute(
                name,
                self.control,
                algorithm.mode,
                args,
                self.net,
                observer=lambda rec: self.node.publish(telemetry.step(rec)),
                stop=stop,
            )
            if result.meta.get("stopped"):
                self._set_state(ScenarioState.STOPPED)
            else:
                self._set_state(ScenarioState.FINISHED, telemetry.final(result, period[1]))
        except Exception as exc:
            log.exception("Run %s failed", name)
            self._set_state(ScenarioState.FAILED, {uns.MESSAGE: str(exc) or type(exc).__name__})

    def _set_state(self, state: ScenarioState, values: dict[str, Value] | None = None) -> None:
        """Change the state together with its values, so no command sees one without the other."""
        with self._lock:
            self._state = state
            self.node.publish({**(values or {}), uns.STATE: state})
            log.info("Run %d %s", self._run, state)


def run_service(args: argparse.Namespace) -> int:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    net = load_network(args.network, Path(args.model_dir))
    node = EdgeNode(
        args.mqtt_host,
        args.mqtt_port,
        uns.GROUP_ID,
        uns.SIM_NODE_ID,
        uns.PRIMARY_HOST_ID,
        initial_metrics(net),
        commands=(uns.START, uns.STOP),
    )
    service = ScenarioService(
        net, node, Path(args.results_dir), Path(args.work_dir), args.plc_host, args.plc_port, args.plc_timeout
    )
    node.on_command = service.submit
    for name in ("SIGINT", "SIGTERM", "SIGBREAK"):
        if hasattr(signal, name):
            signal.signal(getattr(signal, name), lambda *_: service.shutdown())
    log.info(
        "Edge node %s: broker %s:%s, PLC %s:%s",
        uns.SIM_NODE_ID,
        args.mqtt_host,
        args.mqtt_port,
        args.plc_host,
        args.plc_port,
    )
    node.start()
    try:
        service.serve()
    finally:
        node.stop()
    return 0
