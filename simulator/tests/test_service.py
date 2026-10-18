import json
import threading
from collections import Counter
from datetime import datetime

import pytest
from conftest import wait_until

from stormbench_sim import uns_contract as uns
from stormbench_sim.control import Mode
from stormbench_sim.network import Controls, read_layout
from stormbench_sim.runner import PythonController, RunResult, RunSpec, run
from stormbench_sim.service import (
    DEFAULT_PACE,
    DEFAULT_STORM,
    ScenarioService,
    catalog,
    control_steps,
    initial_metrics,
    network_description,
    unix_ms,
)
from stormbench_sim.sparkplug import DataType, Metric, Payload

YEAR = (datetime(2000, 1, 1, 0, 0), datetime(2000, 12, 31, 23, 55))


class FakeNode:
    """Stands in for the edge node: keeps every update and the current value of each metric."""

    def __init__(self, metrics):
        self.values = {m.name: m.value for m in metrics}
        self.updates = []
        self._lock = threading.Lock()

    def publish(self, values):
        with self._lock:
            unknown = set(values) - set(self.values)
            assert not unknown, f"not declared in the NBIRTH: {unknown}"
            self.values.update(values)
            self.updates.append(dict(values))

    def series(self, name):
        with self._lock:
            return [u[name] for u in self.updates if name in u]


def start(storm, algorithm, pace=0.0):
    return Payload(
        1,
        None,
        [
            Metric(uns.START, DataType.BOOLEAN, True),
            Metric(uns.STORM, DataType.STRING, storm),
            Metric(uns.ALGORITHM, DataType.STRING, algorithm),
            Metric(uns.PACE, DataType.DOUBLE, pace),
        ],
    )


STOP = Payload(1, None, [Metric(uns.STOP, DataType.BOOLEAN, True)])


def test_control_steps_as_the_runner_counts_them():
    assert control_steps(datetime(2000, 5, 29), datetime(2000, 6, 1), 300) == 863
    assert control_steps(*YEAR, 300) == 105_406


def test_unix_ms_takes_model_time_as_utc():
    assert unix_ms(datetime(2000, 5, 29)) == 959_558_400_000


def test_catalog(network):
    cat = json.loads(json.dumps(catalog(network, YEAR)))
    assert set(cat) == {"storms", "algorithms", "paces", "defaultStorm", "defaultAlgorithm", "defaultPace"}
    storms = {s["id"]: s for s in cat["storms"]}
    assert list(storms) == ["2000-05-30", "2000-08-19", "2000-10-04", "2000-05-29-3d", "2000"]
    assert storms["2000-05-29-3d"] == {
        "id": "2000-05-29-3d",
        "label": "29 May to 1 June 2000",
        "description": "Three days, the validation window of the golden tests",
        "start": "2000-05-29T00:00:00",
        "end": "2000-06-01T00:00:00",
        "steps": 863,
    }
    assert (storms["2000"]["start"], storms["2000"]["end"]) == ("2000-01-01T00:00:00", "2000-12-31T23:55:00")
    assert storms["2000"]["steps"] == 105_406
    assert [storms[s]["steps"] for s in ("2000-05-30", "2000-08-19", "2000-10-04")] == [431, 287, 287]
    algorithms = {a["id"]: a for a in cat["algorithms"]}
    assert list(algorithms) == ["base", "efd-plc", "efd-plc-intended", "efd-center", "mpc-center"]
    assert [a["mode"] for a in cat["algorithms"]] == [0, 1, 2, 3, 3]
    assert [a["center"] for a in cat["algorithms"]] == [None, None, None, "scada", "simulator"]
    assert all(set(a) == {"id", "label", "description", "mode", "center"} for a in cat["algorithms"])
    assert cat["paces"] == [0.5, 0.2, 0.1, 0.05, 0.0]
    assert cat["defaultStorm"] == "2000-05-30" in storms
    assert cat["defaultAlgorithm"] == "efd-center" in algorithms
    assert cat["defaultPace"] == 0.1 in cat["paces"]


@pytest.mark.model
def test_network_description(network):
    net = json.loads(json.dumps(network_description(network)))
    assert set(net) == {
        "name",
        "controlStepS",
        "tankMaxDepthM",
        "wwtp",
        "tanks",
        "overflowPoints",
        "stations",
        "nodes",
        "links",
        "labelOffsets",
        "map",
    }
    assert set(net["map"]) == {"catchments", "gauges", "waters", "areas", "places", "tints"}
    assert (net["name"], net["controlStepS"], net["tankMaxDepthM"], net["wwtp"]) == (
        "astlingen",
        300,
        5.0,
        "Out_to_WWTP",
    )
    assert net["tanks"] == ["T1", "T2", "T3", "T4", "T5", "T6"]
    assert net["overflowPoints"] == list(network.overflow_points)
    assert net["stations"][0] == {"valve": "V2", "tank": "T2", "baseSetting": 0.2366, "lowSetting": 0.1075}
    assert Counter(n["kind"] for n in net["nodes"]) == {"junction": 23, "storage": 6, "outfall": 1}
    assert Counter(link["kind"] for link in net["links"]) == {"conduit": 23, "orifice": 6}
    nodes = {n["id"]: n for n in net["nodes"]}
    assert (nodes["T1"]["x"], nodes["T1"]["y"]) == read_layout(network).coords["T1"]
    assert all(link["from"] in nodes and link["to"] in nodes for link in net["links"])
    assert {"id": "V2", "kind": "orifice", "from": "T2", "to": "J17"} in net["links"]
    assert net["labelOffsets"] == {"CSO10": [-330.0, 120.0]}


@pytest.mark.model
def test_initial_metrics_cover_the_contract(network):
    metrics = initial_metrics(network)
    by_name = {m.name: m for m in metrics}
    assert len(by_name) == len(metrics)
    console = [
        uns.STATE,
        uns.RUN,
        uns.STORM,
        uns.ALGORITHM,
        uns.PACE,
        uns.STEP,
        uns.STEPS,
        uns.CLOCK,
        uns.MESSAGE,
    ]
    field = [
        uns.RAIN_INTENSITY,
        uns.OVERFLOW_TOTAL,
        uns.WWTP_INFLOW,
        uns.BASE_CASE_OVERFLOW_TOTAL,
        uns.MISMATCHES,
    ]
    faults = [uns.CENTER_LINK_CUT, uns.CENTER_LINK_UP, uns.STUCK_GATE, uns.FROZEN_SENSOR, uns.PLC_STEP]
    faults += [uns.valve_opening(s.valve) for s in network.stations] + [
        uns.true_level(t) for t in network.tanks
    ]
    points = [f(p) for p in network.overflow_points for f in (uns.overflow_active, uns.overflow_volume)]
    assert set(by_name) == {uns.NETWORK, uns.CATALOG, *console, *field, *faults, *points}
    assert by_name[uns.STATE] == Metric(uns.STATE, DataType.STRING, "idle")
    assert by_name[uns.RUN] == Metric(uns.RUN, DataType.INT64, 0)
    assert by_name[uns.PACE] == Metric(uns.PACE, DataType.DOUBLE, DEFAULT_PACE)
    assert by_name[uns.CLOCK].datatype == by_name[uns.MISMATCHES].datatype == DataType.INT64
    assert by_name[uns.overflow_active("T1")] == Metric(uns.overflow_active("T1"), DataType.BOOLEAN, False)
    assert by_name[uns.overflow_volume("CSO10")].datatype == DataType.DOUBLE
    assert json.loads(by_name[uns.CATALOG].value)["defaultStorm"] == DEFAULT_STORM
    assert json.loads(by_name[uns.NETWORK].value)["name"] == "astlingen"


@pytest.fixture
def console(network, tmp_path):
    node = FakeNode(initial_metrics(network))
    service = ScenarioService(network, node, tmp_path / "results", tmp_path / "work", control="python")
    yield service, node
    service.stop()
    service.join(60)


@pytest.mark.model
def test_refused_commands_leave_the_state_unchanged(console):
    service, node = console
    for command, reason in [
        (start("1999-01-01", "efd-center"), "Unknown storm '1999-01-01'."),
        (start("2000-05-30", "magic"), "Unknown algorithm 'magic'."),
        (start("2000-05-30", "base", pace=7.0), "Pace must be between 0 and 5 s per step."),
        (start("2000-05-30", "base", pace=float("nan")), "Pace must be between 0 and 5 s per step."),
        (STOP, "No storm is running."),
    ]:
        service.handle(command)
        assert node.values[uns.MESSAGE] == reason
    assert node.series(uns.STATE) == []
    assert (node.values[uns.STATE], node.values[uns.RUN]) == ("idle", 0)


@pytest.mark.model
def test_a_console_run_equals_the_offline_run(console, network, tmp_path):
    service, node = console
    service.handle(start("2000-10-04", "efd-center"))
    service.join(120)

    assert node.series(uns.STATE) == ["preparing", "running", "finished"]
    window = (datetime(2000, 10, 4, 12, 0), datetime(2000, 10, 5, 12, 0))
    steps = node.values[uns.STEPS]
    assert steps == control_steps(*window, network.control_step_s)
    assert node.series(uns.STEP) == [0, *range(1, steps + 1)]
    assert node.series(uns.CLOCK)[:2] == [unix_ms(window[0]), unix_ms(window[0]) + 300_000]
    assert node.values[uns.CLOCK] == unix_ms(window[1])
    for series in (
        node.series(uns.OVERFLOW_TOTAL),
        node.series(uns.WWTP_INFLOW),
        node.series(uns.BASE_CASE_OVERFLOW_TOTAL),
        *(node.series(uns.overflow_volume(p)) for p in network.overflow_points),
    ):
        assert series == sorted(series)
    assert any(True in node.series(uns.overflow_active(p)) for p in network.overflow_points)
    assert max(node.series(uns.RAIN_INTENSITY)) > 0

    offline = run(
        RunSpec(
            "offline", network, Controls.EXTERNAL, Mode.EFD_CENTER, PythonController(network), period=window
        ),
        tmp_path / "offline",
        progress=None,
    )
    for point in network.overflow_points:
        assert node.values[uns.overflow_volume(point)] == offline.totals["flood_m3"][point]
    assert node.values[uns.OVERFLOW_TOTAL] == offline.totals["flood_total_m3"]
    assert node.values[uns.WWTP_INFLOW] == offline.totals["wwtp_inflow_m3"]
    assert node.values[uns.BASE_CASE_OVERFLOW_TOTAL] > 0
    assert (node.values[uns.MISMATCHES], node.values[uns.MESSAGE]) == (0, "")
    saved = RunResult.load(tmp_path / "results" / "runs" / "console-2000-10-04-efd-center")
    assert saved.totals["flood_m3"] == offline.totals["flood_m3"]


@pytest.mark.model
def test_a_console_mpc_run_equals_the_offline_mpc_run(console, network, tmp_path):
    from stormbench_sim.mpc import MpcCenter
    from stormbench_sim.runner import CenterController

    service, node = console
    service.handle(start("2000-08-19", "mpc-center"))
    service.join(180)

    assert node.series(uns.STATE) == ["preparing", "running", "finished"]
    window = (datetime(2000, 8, 19, 6, 0), datetime(2000, 8, 20, 6, 0))
    center = MpcCenter.for_run(network, tmp_path / "offline", window)
    offline = run(
        RunSpec(
            "offline",
            network,
            Controls.EXTERNAL,
            Mode.EFD_CENTER,
            CenterController(center, network),
            period=window,
        ),
        tmp_path / "offline",
        progress=None,
    )
    assert node.values[uns.OVERFLOW_TOTAL] == offline.totals["flood_total_m3"]
    assert node.values[uns.OVERFLOW_TOTAL] < node.values[uns.BASE_CASE_OVERFLOW_TOTAL]
    saved = RunResult.load(tmp_path / "results" / "runs" / "console-2000-08-19-mpc-center")
    assert saved.controller == "python-mpc"
    assert saved.meta["mpc"]["failures"] == 0


@pytest.mark.model
def test_stop_ends_the_run_after_the_current_step(console, tmp_path):
    service, node = console
    worker = threading.Thread(target=service.serve)
    worker.start()
    try:
        service.submit(start("2000-08-19", "efd-plc", pace=0.05))
        wait_until(lambda: node.values[uns.STEP] >= 3, 60)
        service.submit(start("2000-08-19", "base"))
        service.submit(STOP)
        wait_until(lambda: node.values[uns.STATE] == "stopped", 30)
    finally:
        service.shutdown()
        worker.join(30)
    assert node.series(uns.STATE) == ["preparing", "running", "stopped"]
    assert node.values[uns.MESSAGE] == "A storm is already running."
    assert node.values[uns.RUN] == 1
    assert 3 <= node.values[uns.STEP] < node.values[uns.STEPS]
    saved = RunResult.load(tmp_path / "results" / "runs" / "console-2000-08-19-efd-plc")
    assert saved.meta["stopped"] is True
    assert saved.meta["steps"] == node.values[uns.STEP]


@pytest.mark.model
def test_shutdown_stops_the_run_it_interrupts(console):
    service, node = console
    service.submit(start("2000-05-30", "efd-center", pace=0.5))
    service.shutdown()
    service.serve()
    assert node.series(uns.STATE) == ["preparing", "stopped"]


@pytest.mark.model
def test_a_failed_run_reports_the_reason_and_the_console_keeps_serving(network, tmp_path):
    work = tmp_path / "work"
    work.write_text("a file where the work directory should be", encoding="utf-8")
    node = FakeNode(initial_metrics(network))
    service = ScenarioService(network, node, tmp_path / "results", work, control="python")
    for run_no in (1, 2):
        service.handle(start("2000-05-30", "base"))
        service.join(30)
        assert node.values[uns.RUN] == run_no
        assert node.values[uns.STATE] == "failed"
        assert node.values[uns.MESSAGE]
    assert node.series(uns.STATE) == ["preparing", "failed", "preparing", "failed"]


@pytest.mark.model
def test_the_link_to_the_center_can_be_cut_and_restored_from_the_console(console):
    service, node = console
    cut = Payload(1, None, [Metric(uns.CENTER_LINK_CUT, DataType.BOOLEAN, True)])
    restore = Payload(1, None, [Metric(uns.CENTER_LINK_CUT, DataType.BOOLEAN, False)])

    service.handle(cut)
    assert node.values[uns.CENTER_LINK_CUT] is True
    assert service._link_cut.is_set()

    service.handle(restore)
    assert node.values[uns.CENTER_LINK_CUT] is False
    assert not service._link_cut.is_set()


@pytest.mark.model
def test_field_faults_are_set_cleared_and_checked(console):
    service, node = console

    service.handle(Payload(1, None, [Metric(uns.STUCK_GATE, DataType.STRING, "V3")]))
    service.handle(Payload(1, None, [Metric(uns.FROZEN_SENSOR, DataType.STRING, "T4")]))
    assert (service._faults.stuck_gate, service._faults.frozen_sensor) == ("V3", "T4")
    assert (node.values[uns.STUCK_GATE], node.values[uns.FROZEN_SENSOR]) == ("V3", "T4")

    service.handle(Payload(1, None, [Metric(uns.STUCK_GATE, DataType.STRING, "V9")]))
    assert node.values[uns.MESSAGE] == "Unknown target 'V9' for Fault/StuckGate."
    assert service._faults.stuck_gate == "V3"

    service.handle(Payload(1, None, [Metric(uns.STUCK_GATE, DataType.STRING, "")]))
    assert service._faults.stuck_gate is None
