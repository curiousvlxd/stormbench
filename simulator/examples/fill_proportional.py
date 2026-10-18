from __future__ import annotations

from collections.abc import Sequence
from datetime import datetime
from pathlib import Path

from stormbench_sim.control import ControlInputs, ControlOutputs
from stormbench_sim.network import Network


class FillProportional:
    name = "fill-proportional"

    def __init__(self, network: Network):
        self.network = network

    def propose(self, inputs: ControlInputs) -> Sequence[float]:
        """One setting per station (Network.stations order), each in 0..1."""
        settings = []
        for station in self.network.stations:
            depth = inputs.depths[self.network.tanks.index(station.tank)]
            fill = depth / self.network.tank_max_depth_m
            settings.append(min(1.0, max(station.base_setting, fill)))
        return settings

    def observe(self, inputs: ControlInputs, outputs: ControlOutputs) -> None:
        """What the stations applied: the proposal, or their local setting on fallback."""


def make_center(
    network: Network, work_dir: Path, period: tuple[datetime, datetime] | None
) -> FillProportional:
    """The factory --center fill_proportional:make_center calls once per run."""
    return FillProportional(network)
