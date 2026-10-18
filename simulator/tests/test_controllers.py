from __future__ import annotations

import argparse
import threading
from datetime import datetime
from pathlib import Path

import pytest

from stormbench_sim.control import STATUS_BAD_PROPOSAL, STATUS_FALLBACK, ControlInputs, Mode, decide
from stormbench_sim.pipeline import center_option, execute, load_center
from stormbench_sim.plc import PlcController
from stormbench_sim.runner import CenterController

EXAMPLES = Path(__file__).resolve().parents[1] / "examples"
LEVELS = (1.0, 2.5, 1.5, 0.3, 0.0, 4.9)


@pytest.fixture
def plugin(monkeypatch, network, tmp_path):
    monkeypatch.syspath_prepend(str(EXAMPLES))
    return load_center("fill_proportional:make_center", network, tmp_path, None, "t", argparse.Namespace())


def test_center_option_accepts_builtins_and_plugins():
    assert center_option("mpc") == "mpc"
    assert center_option("my.module:factory") == "my.module:factory"
    with pytest.raises(argparse.ArgumentTypeError):
        center_option("magic")


def test_builtin_centers_played_elsewhere_load_as_none(network, tmp_path):
    for spec in ("uns", "emulated"):
        assert load_center(spec, network, tmp_path, None, "t", argparse.Namespace()) is None


def test_plugin_center_behind_the_station_checks(plugin, network):
    controller = CenterController(plugin, network)
    assert controller.name == "python-fill-proportional"
    out = controller.decide(ControlInputs(300, Mode.EFD_CENTER, True, LEVELS))
    assert out.setpoints == pytest.approx((0.5, 0.6508, 0.3523, 0.98))
    assert out.status == 0


class Broken:
    name = "broken"

    def propose(self, inputs):
        return [float("nan"), 0.5, 0.5, 0.5]

    def observe(self, inputs, outputs):
        self.applied = outputs.setpoints


def test_the_stations_reject_an_implausible_proposal(network):
    center = Broken()
    out = CenterController(center, network).decide(ControlInputs(300, Mode.EFD_CENTER, True, LEVELS))
    assert out.status == STATUS_FALLBACK + STATUS_BAD_PROPOSAL
    assert center.applied == tuple(s.base_setting for s in network.stations)


@pytest.mark.model
def test_plugin_center_runs_a_storm_offline(monkeypatch, network, tmp_path):
    monkeypatch.syspath_prepend(str(EXAMPLES))
    args = argparse.Namespace(
        center="fill_proportional:make_center",
        start=datetime(2000, 10, 4, 12).isoformat(),
        end=datetime(2000, 10, 5, 12).isoformat(),
        pace=0.0,
        work_dir=str(tmp_path / "work"),
        results_dir=str(tmp_path / "results"),
    )
    result = execute("fill", "python", Mode.EFD_CENTER, args, network)
    assert result.controller == "python-fill-proportional"
    assert (result.status == 0).all()
    assert result.totals["flood_total_m3"] > 0


class FakeLink:
    """Plays the PLC: answers as the reference would with the proposal it was sent."""

    def __init__(self, network):
        self.network, self.calls = network, []

    def exchange(self, inputs, proposal=None, center_deadline_s=None):
        self.calls.append((inputs, proposal, center_deadline_s))
        return decide(inputs, self.network, proposal), False


def test_the_plc_gets_the_proposal_of_a_center_played_here(plugin, network):
    link = FakeLink(network)
    controller = PlcController(link, network, center=plugin)

    out = controller.decide(ControlInputs(300, Mode.EFD_CENTER, True, LEVELS))

    inputs, proposal, deadline = link.calls[0]
    assert proposal == list(plugin.propose(inputs))
    assert deadline is None
    assert out.setpoints == pytest.approx(proposal)
    assert controller.shadow.mismatches == 0


def test_a_cut_link_keeps_a_center_played_here_out(network):
    link, center, cut = FakeLink(network), Broken(), threading.Event()
    cut.set()
    controller = PlcController(link, network, center=center, link_cut=cut)

    out = controller.decide(ControlInputs(300, Mode.EFD_CENTER, True, LEVELS))

    assert link.calls[0][0].center_ok is False
    assert link.calls[0][1] is None
    assert out.status == STATUS_FALLBACK
    assert center.applied == tuple(s.base_setting for s in network.stations)
