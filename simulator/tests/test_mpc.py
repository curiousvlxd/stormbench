from __future__ import annotations

from datetime import datetime
from pathlib import Path

import numpy as np
import pytest

from stormbench_sim.control import ControlInputs, Mode
from stormbench_sim.mpc import (
    ConceptualModel,
    InflowForecast,
    LinearMpc,
    MpcCenter,
    MpcConfig,
    Orifice,
    Tank,
    build_model,
)
from stormbench_sim.network import Controls, Network, Station
from stormbench_sim.runner import CenterController, PythonController, RunSpec, run

STEP = 300
GATE = Orifice("VA", height_m=0.05, width_m=0.3, discharge_coeff=1.0, offset_m=0.0)
OUTLET = Orifice("VB", height_m=0.08, width_m=0.3, discharge_coeff=1.0, offset_m=0.0)
MODEL = ConceptualModel(
    step_s=STEP,
    tanks=(Tank("A", 100.0, 2.0, GATE, True), Tank("B", 50.0, 2.0, OUTLET, False)),
    downstream={"A": ("B", 1)},
    to_wwtp=("B",),
    probes=(),
    stations=("A",),
)
NETWORK = Network(
    name="tiny",
    model_dir=Path("."),
    inp="tiny.inp",
    efd_rules="",
    control_step_s=STEP,
    wwtp_outfall="WWTP",
    tanks=("A", "B"),
    tank_max_depth_m=2.0,
    overflow_points=("A", "B"),
    efd_threshold_m=1.0,
    stations=(Station("VA", "A", 0.5, 0.2),),
)
FLAT = MpcConfig(horizon_steps=6, overflow_weights={"A": 2.0, "B": 1.0}, time_weighting="flat")


def solve(inflow, volumes=(0.0, 0.0), history=0.0, config=FLAT):
    lp = LinearMpc(MODEL, config)
    depths = np.asarray(volumes) / np.array([t.area_m2 for t in MODEL.tanks])
    q_max = np.array([t.outlet.flow(d, 1.0) for t, d in zip(MODEL.tanks, depths, strict=True)])
    x = lp.solve(
        np.asarray(volumes, float), q_max, np.zeros(2), np.asarray(inflow, float), np.full((1, 2), history)
    )
    assert x is not None
    return lp, x


def test_orifice_inversion_round_trips():
    for depth in (0.02, 0.05, 0.3, 1.0, 1.9):
        for setting in (0.0, 0.1, 0.37, 0.8, 1.0):
            q = GATE.flow(depth, setting)
            s = GATE.setting_for(q, depth)
            assert 0.0 <= s <= 1.0
            assert GATE.flow(depth, s) == pytest.approx(q, rel=1e-9, abs=1e-12)
    assert GATE.setting_for(10.0, 1.0) == 1.0
    assert GATE.setting_for(0.0, 1.0) == 0.0


def test_mass_balance_holds_in_the_plan():
    inflow = np.array([[0.02, 0.01]] * 6)
    lp, _ = solve(inflow, volumes=(40.0, 10.0), history=0.005)
    res_x = lp.last_x
    for k in range(lp.H):
        for i in range(2):
            v_next = res_x[lp.iv(k, i)]
            v_now = (40.0, 10.0)[i] if k == 0 else res_x[lp.iv(k - 1, i)]
            upstream = 0.0
            if i == 1:
                upstream = res_x[lp.iq(k - 1, 0)] if k >= 1 else 0.005
            expected = v_now + lp.dt * (inflow[k, i] + upstream - res_x[lp.iq(k, i)] - res_x[lp.iw(k, i)])
            assert v_next == pytest.approx(expected, abs=1e-6)
            assert -1e-7 <= v_next <= MODEL.tanks[i].max_volume_m3 + 1e-6


def test_no_overflow_when_storage_suffices():
    inflow = np.array([[0.05, 0.0]] * 6)
    lp, _ = solve(inflow)
    overflow = [lp.last_x[lp.iw(k, i)] for k in range(lp.H) for i in range(2)]
    assert max(overflow) == pytest.approx(0.0, abs=1e-9)


def test_overflow_only_what_cannot_be_stored_or_released():
    inflow = np.array([[0.5, 0.0]] * 6)
    lp, q = solve(inflow, volumes=(150.0, 0.0))
    x = lp.last_x
    spilled = sum(x[lp.iw(k, 0)] for k in range(lp.H)) * lp.dt
    released = sum(q[:, 0]) * lp.dt
    stored = MODEL.tanks[0].max_volume_m3 - 150.0
    excess = inflow[:, 0].sum() * lp.dt - stored - released
    assert spilled == pytest.approx(excess, rel=1e-6)
    assert q[0, 0] == pytest.approx(GATE.flow(1.5, 1.0), rel=1e-6)
    assert sum(x[lp.iw(k, 1)] for k in range(lp.H)) == pytest.approx(0.0, abs=1e-9)


def test_upstream_weight_moves_the_spill_downstream():
    inflow = np.array([[0.2, 0.05]] * 6)
    _, q = solve(inflow, volumes=(195.0, 95.0))
    assert q[0, 0] == pytest.approx(GATE.flow(1.95, 1.0), rel=1e-6)


def test_center_proposes_settings_within_0_1():
    rng = np.random.default_rng(1)
    forecast = InflowForecast(datetime(2000, 1, 1), STEP, ("A", "B"), rng.uniform(0, 0.3, (200, 2)))
    center = MpcCenter(NETWORK, MODEL, forecast, MpcConfig(horizon_steps=8))
    controller = CenterController(center, NETWORK)
    for k in range(1, 150):
        depths = tuple(rng.uniform(0.0, 2.0, 2))
        out = controller.decide(ControlInputs(k * STEP, Mode.EFD_CENTER, True, depths))
        assert all(0.0 <= s <= 1.0 for s in out.setpoints)
        assert out.status == 0
    assert center.stats.failures == 0
    assert center.summary()["max_ms"] < 100


def test_center_link_down_runs_locally_without_asking_the_center():
    forecast = InflowForecast(datetime(2000, 1, 1), STEP, ("A", "B"), np.zeros((10, 2)))
    center = MpcCenter(NETWORK, MODEL, forecast, MpcConfig(horizon_steps=4))
    out = CenterController(center, NETWORK).decide(ControlInputs(STEP, Mode.EFD_CENTER, False, (1.0, 1.0)))
    assert out.setpoints == (0.5,)
    assert center.stats.solves == 0


@pytest.mark.model
def test_conceptual_model_read_from_the_inp(network):
    model = build_model(network)
    assert [t.name for t in model.tanks] == ["T2", "T3", "T4", "T6", "T1"]
    assert [t.controlled for t in model.tanks] == [True, True, True, True, False]
    assert sum(t.max_volume_m3 for t in model.tanks) == pytest.approx(5400.0)
    assert {up: down for up, (down, _) in model.downstream.items()} == {
        "T2": "T1",
        "T3": "T1",
        "T4": "T1",
        "T6": "T3",
    }
    assert model.to_wwtp == ("T1",)
    links = {p.element for p in model.probes if p.kind == "link"}
    assert links == {"C18", "C17", "C7", "C10", "C20", "C22", "C1"}


@pytest.mark.model
def test_mpc_spills_less_than_efd_on_a_storm(network, tmp_path):
    window = (datetime(2000, 5, 30, 0, 0), datetime(2000, 5, 31, 12, 0))
    efd = run(
        RunSpec(
            "efd", network, Controls.EXTERNAL, Mode.EFD_PUBLISHED, PythonController(network), period=window
        ),
        tmp_path,
        progress=None,
    )
    center = MpcCenter.for_run(network, tmp_path, window)
    mpc = run(
        RunSpec(
            "mpc",
            network,
            Controls.EXTERNAL,
            Mode.EFD_CENTER,
            CenterController(center, network),
            period=window,
        ),
        tmp_path,
        progress=None,
    )
    assert mpc.totals["flood_total_m3"] < 0.95 * efd.totals["flood_total_m3"]
    assert ((mpc.setpoints >= 0.0) & (mpc.setpoints <= 1.0)).all()
    assert (mpc.status == 0).all()
    summary = center.summary()
    assert summary["failures"] == 0
    assert summary["p99_ms"] < 100
