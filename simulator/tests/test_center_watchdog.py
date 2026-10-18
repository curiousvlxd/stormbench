from __future__ import annotations

import threading
from dataclasses import replace

import pytest

from stormbench_sim.control import STATUS_FALLBACK, ControlInputs, Mode, center_proposal, decide
from stormbench_sim.plc import PlcController

LEVELS = (1.0, 2.0, 1.5, 0.3, 0.0, 4.9)


class FakeLink:
    """Plays the PLC: answers as the reference would; `center_answers` decides whether the
    center's proposal arrives before the deadline."""

    def __init__(self, network, center_answers=True):
        self.network = network
        self.center_answers = center_answers
        self.calls = []

    def exchange(self, inputs, proposal=None, center_deadline_s=None):
        self.calls.append((inputs, center_deadline_s))
        gave_up = False
        if inputs.mode == Mode.EFD_CENTER and inputs.center_ok and proposal is None:
            if self.center_answers:
                proposal = center_proposal(inputs, self.network, Mode.EFD_PUBLISHED)
            else:
                assert center_deadline_s is not None, "would wait for the center forever"
                inputs, gave_up = replace(inputs, center_ok=False), True
        return decide(inputs, self.network, proposal, Mode.EFD_PUBLISHED), gave_up


def step(controller, t_s=300):
    return controller.decide(ControlInputs(t_s, Mode.EFD_CENTER, True, LEVELS))


def test_center_that_answers_keeps_the_link_up(network):
    link = FakeLink(network)
    controller = PlcController(link, network)

    out = step(controller)

    assert out.status & STATUS_FALLBACK == 0
    assert controller.center_link_up
    assert controller.shadow.mismatches == 0
    assert link.calls[0][1] == controller.center_deadline_s


def test_silent_center_lets_the_plc_fall_back_and_the_run_go_on(network):
    link = FakeLink(network, center_answers=False)
    controller = PlcController(link, network, center_retry_s=3600)

    first = step(controller)
    second = step(controller, 600)

    assert first.status & STATUS_FALLBACK
    assert second.status & STATUS_FALLBACK
    assert not controller.center_link_up
    assert controller.center_fallbacks == 1
    assert controller.shadow.mismatches == 0
    assert link.calls[1][0].center_ok is False
    assert link.calls[1][1] is None


def test_center_is_tried_again_and_the_link_comes_back(network):
    link = FakeLink(network, center_answers=False)
    controller = PlcController(link, network, center_retry_s=0.0)
    step(controller)

    link.center_answers = True
    out = step(controller, 600)

    assert out.status & STATUS_FALLBACK == 0
    assert controller.center_link_up
    assert link.calls[1][0].center_ok is True


def test_a_cut_link_sends_centerok_0_without_asking_the_center(network):
    link = FakeLink(network)
    cut = threading.Event()
    cut.set()
    controller = PlcController(link, network, link_cut=cut)

    out = step(controller)

    assert out.status & STATUS_FALLBACK
    assert link.calls[0][0].center_ok is False
    assert link.calls[0][1] is None
    assert not controller.center_link_up
    assert controller.shadow.mismatches == 0


@pytest.mark.parametrize("mode", [Mode.LOCAL, Mode.EFD_PUBLISHED])
def test_modes_without_the_center_never_wait_for_it(network, mode):
    link = FakeLink(network)
    controller = PlcController(link, network)

    controller.decide(ControlInputs(300, mode, True, LEVELS))

    assert link.calls[0][1] is None
