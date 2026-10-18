from __future__ import annotations

from datetime import datetime
from itertools import pairwise
from pathlib import Path

import pytest

from stormbench_sim.control import Mode
from stormbench_sim.network import Controls
from stormbench_sim.runner import FieldFaults, PythonController, RunSpec, run

pytestmark = pytest.mark.model

PERIOD = (datetime(2000, 5, 30, 18, 0), datetime(2000, 5, 31, 2, 0))


def efd_run(network, tmp_path: Path, name: str, faults: FieldFaults | None):
    records = []
    spec = RunSpec(
        name,
        network,
        Controls.EXTERNAL,
        Mode.EFD_PUBLISHED,
        PythonController(network),
        PERIOD,
        observer=records.append,
        faults=faults,
    )
    return run(spec, tmp_path, progress=None), records


def test_without_faults_measured_and_applied_equal_the_model(network, tmp_path):
    _, records = efd_run(network, tmp_path, "healthy", FieldFaults())

    assert all(r.measured == r.depths for r in records)
    assert all(a.openings == b.outputs.setpoints for a, b in pairwise(records))


def test_stuck_gate_keeps_its_opening_while_the_controller_moves(network, tmp_path):
    station = next(i for i, s in enumerate(network.stations) if s.valve == "V2")
    _, healthy = efd_run(network, tmp_path, "healthy", None)
    _, stuck = efd_run(network, tmp_path, "stuck", FieldFaults(stuck_gate="V2"))

    assert {r.openings[station] for r in stuck} == {network.stations[station].base_setting}
    assert len({r.openings[station] for r in healthy}) > 1, "EFD moves V2 in this storm"


def test_frozen_sensor_reports_one_value_while_the_tank_fills(network, tmp_path):
    tank = network.tanks.index("T4")
    _, records = efd_run(network, tmp_path, "frozen", FieldFaults(frozen_sensor="T4"))

    assert {r.measured[tank] for r in records} == {records[0].depths[tank]}
    assert max(r.depths[tank] for r in records) > records[0].depths[tank] + 1.0
