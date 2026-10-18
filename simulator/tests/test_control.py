import pytest

from stormbench_sim.control import (
    STATUS_BAD_PROPOSAL,
    STATUS_CLAMPED,
    STATUS_FALLBACK,
    STATUS_INPUT_FAULT,
    ControlInputs,
    Mode,
    basin_station,
    decide,
    efd_coordinator,
)

BASE = [0.2366, 0.6508, 0.3523, 0.4303]
LOW = [0.1075, 0.3159, 0.1894, 0.1687]


def depths(t2, t3, t4, t6, t1=0.0, t5=0.0):
    return (t1, t2, t3, t4, t5, t6)


def test_published_opens_only_the_strictly_fullest_tank():
    assert efd_coordinator(Mode.EFD_PUBLISHED, [2.0, 1.5, 0.3, 4.9], BASE, LOW, 1.0) == [
        BASE[0],
        BASE[1],
        BASE[2],
        1.0,
    ]


def test_published_ignores_the_low_setting_and_the_threshold():
    assert efd_coordinator(Mode.EFD_PUBLISHED, [0.5, 0.9, 0.2, 0.1], BASE, LOW, 1.0) == [
        BASE[0],
        1.0,
        BASE[2],
        BASE[3],
    ]


def test_intended_throttles_the_emptiest_tank():
    assert efd_coordinator(Mode.EFD_INTENDED, [2.0, 1.5, 0.3, 4.9], BASE, LOW, 1.0) == [
        BASE[0],
        BASE[1],
        LOW[2],
        1.0,
    ]


def test_intended_keeps_base_case_below_threshold():
    assert efd_coordinator(Mode.EFD_INTENDED, [0.5, 0.9, 0.2, 0.1], BASE, LOW, 1.0) == BASE


def test_ties_keep_base_setting():
    assert efd_coordinator(Mode.EFD_PUBLISHED, [2.0, 2.0, 1.0, 1.0], BASE, LOW, 1.0) == BASE
    assert efd_coordinator(Mode.EFD_INTENDED, [2.0, 2.0, 1.0, 1.0], BASE, LOW, 1.0) == BASE


def test_station_clamps_remote_setpoint():
    assert basin_station(1.3, True, 0.5) == (1.0, True)
    assert basin_station(-0.1, True, 0.5) == (0.0, True)
    assert basin_station(0.7, True, 0.5) == (0.7, False)
    assert basin_station(0.7, False, 0.5) == (0.5, False)


def test_decide_efd(network):
    out = decide(ControlInputs(300, Mode.EFD_PUBLISHED, True, depths(2.0, 1.5, 0.3, 4.9)), network)
    assert out.setpoints == (BASE[0], BASE[1], BASE[2], 1.0)
    assert out.mode_active == Mode.EFD_PUBLISHED
    assert out.status == 0


def test_local_mode_is_base_case(network):
    out = decide(ControlInputs(0, Mode.LOCAL, True, depths(2.0, 1.5, 0.3, 4.9)), network)
    assert out.setpoints == tuple(BASE)
    assert out.mode_active == Mode.LOCAL
    assert out.status == 0


def test_lost_center_falls_back_to_base_case(network):
    out = decide(ControlInputs(0, Mode.EFD_PUBLISHED, False, depths(2.0, 1.5, 0.3, 4.9)), network)
    assert out.setpoints == tuple(BASE)
    assert out.mode_active == Mode.LOCAL
    assert out.status == STATUS_FALLBACK


@pytest.mark.parametrize("bad", [float("nan"), -0.01, 5.2])
def test_implausible_level_falls_back(network, bad):
    out = decide(ControlInputs(0, Mode.EFD_INTENDED, True, depths(bad, 1.5, 0.3, 4.9)), network)
    assert out.setpoints == tuple(BASE)
    assert out.status == STATUS_FALLBACK + STATUS_INPUT_FAULT


def test_uncontrolled_tank_level_is_not_validated(network):
    out = decide(
        ControlInputs(0, Mode.EFD_PUBLISHED, True, depths(2.0, 1.5, 0.3, 4.9, t1=float("nan"))), network
    )
    assert out.status == 0


def test_status_bits_are_distinct():
    assert len({STATUS_FALLBACK, STATUS_INPUT_FAULT, STATUS_CLAMPED, STATUS_BAD_PROPOSAL}) == 4


def test_center_mode_follows_a_correct_proposal(network):
    inputs = ControlInputs(0, Mode.EFD_CENTER, True, depths(2.0, 1.5, 0.3, 4.9))
    out = decide(inputs, network)
    assert out.setpoints == (BASE[0], BASE[1], BASE[2], 1.0)
    assert out.mode_active == Mode.EFD_CENTER
    assert out.status == 0


@pytest.mark.parametrize("bad", [float("nan"), 1.3, -0.2])
def test_center_mode_rejects_implausible_proposal(network, bad):
    inputs = ControlInputs(0, Mode.EFD_CENTER, True, depths(2.0, 1.5, 0.3, 4.9))
    out = decide(inputs, network, proposal=[bad, 0.5, 0.5, 0.5])
    assert out.setpoints == tuple(BASE)
    assert out.mode_active == Mode.LOCAL
    assert out.status == STATUS_FALLBACK + STATUS_BAD_PROPOSAL


def test_center_mode_without_link_runs_locally(network):
    inputs = ControlInputs(0, Mode.EFD_CENTER, False, depths(2.0, 1.5, 0.3, 4.9))
    out = decide(inputs, network, proposal=[float("nan")] * 4)
    assert out.setpoints == tuple(BASE)
    assert out.status == STATUS_FALLBACK
