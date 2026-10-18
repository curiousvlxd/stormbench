from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass
from enum import IntEnum

from .network import Network

DEPTH_TOLERANCE_M = 0.05
MIN_SETTING = 0.0
MAX_SETTING = 1.0

STATUS_FALLBACK = 1
STATUS_INPUT_FAULT = 2
STATUS_CLAMPED = 4
STATUS_BAD_PROPOSAL = 8


class Mode(IntEnum):
    LOCAL = 0
    EFD_PUBLISHED = 1
    EFD_INTENDED = 2
    EFD_CENTER = 3


@dataclass(frozen=True)
class ControlInputs:
    sim_time_s: int
    mode: Mode
    center_ok: bool
    depths: tuple[float, ...]


@dataclass(frozen=True)
class ControlOutputs:
    setpoints: tuple[float, ...]
    mode_active: Mode
    status: int


def efd_coordinator(
    mode: Mode,
    depth: Sequence[float],
    base: Sequence[float],
    low: Sequence[float],
    threshold: float,
) -> list[float]:
    """Equal Filling Degree over the controlled tanks.

    EFD as published: SWMM gives equal-priority rules first-listed precedence, so in
    EFD_Control.txt only "the strictly fullest tank opens fully" ever acts; the
    low-setting rules and the 1 m threshold rule are always overridden.
    EFD as intended: below the threshold everywhere -> Base Case; otherwise the strictly
    fullest tank opens fully and the strictly emptiest is throttled to its low setting.
    Ties keep the Base Case setting in both modes, as in the SWMM rules.
    """
    n = len(depth)
    all_below = all(d < threshold for d in depth)
    out = []
    for i in range(n):
        others = [depth[j] for j in range(n) if j != i]
        fullest = all(depth[i] > d for d in others)
        emptiest = all(depth[i] < d for d in others)
        if mode == Mode.EFD_INTENDED and all_below:
            out.append(base[i])
        elif fullest:
            out.append(1.0)
        elif mode == Mode.EFD_INTENDED and emptiest:
            out.append(low[i])
        else:
            out.append(base[i])
    return out


def basin_station(remote_setpoint: float, remote_enabled: bool, local_setting: float) -> tuple[float, bool]:
    """Local protection layer of one station: returns (setpoint, clamped)."""
    if remote_enabled:
        setpoint = min(max(remote_setpoint, MIN_SETTING), MAX_SETTING)
        return setpoint, setpoint != remote_setpoint
    return local_setting, False


def center_proposal(
    inputs: ControlInputs, network: Network, variant: Mode = Mode.EFD_PUBLISHED
) -> list[float]:
    """What the central optimiser proposes for these inputs (it runs the same EFD logic)."""
    depth = [inputs.depths[i] for i in network.station_tank_index()]
    base = [s.base_setting for s in network.stations]
    low = [s.low_setting for s in network.stations]
    return efd_coordinator(variant, depth, base, low, network.efd_threshold_m)


def decide(
    inputs: ControlInputs,
    network: Network,
    proposal: Sequence[float] | None = None,
    center_variant: Mode = Mode.EFD_PUBLISHED,
) -> ControlOutputs:
    """PLC decision for one step. In EFD_CENTER mode `proposal` is what the center sent;
    when omitted, the center is assumed to run `center_variant` correctly."""
    stations = network.stations
    base = [s.base_setting for s in stations]
    low = [s.low_setting for s in stations]
    depth = [inputs.depths[i] for i in network.station_tank_index()]
    limit = network.tank_max_depth_m + DEPTH_TOLERANCE_M
    center_mode = inputs.mode == Mode.EFD_CENTER

    inputs_valid = all((d >= 0.0) and (d <= limit) for d in depth)
    if center_mode:
        if proposal is None:
            proposal = center_proposal(inputs, network, center_variant)
        proposal_valid = all((p >= 0.0) and (p <= 1.0) for p in proposal)
        requested = list(proposal)
    else:
        requested = efd_coordinator(inputs.mode, depth, base, low, network.efd_threshold_m)
        proposal_valid = True
    coordinated = inputs.mode in (Mode.EFD_PUBLISHED, Mode.EFD_INTENDED, Mode.EFD_CENTER)
    remote_enabled = coordinated and inputs.center_ok and inputs_valid and proposal_valid

    setpoints, any_clamped = [], False
    for i in range(len(stations)):
        sp, clamped = basin_station(requested[i], remote_enabled, base[i])
        setpoints.append(sp)
        any_clamped = any_clamped or clamped

    status = 0
    if coordinated and not remote_enabled:
        status += STATUS_FALLBACK
    if not inputs_valid:
        status += STATUS_INPUT_FAULT
    if any_clamped:
        status += STATUS_CLAMPED
    if center_mode and inputs.center_ok and not proposal_valid:
        status += STATUS_BAD_PROPOSAL
    mode_active = inputs.mode if remote_enabled else Mode.LOCAL
    return ControlOutputs(tuple(setpoints), mode_active, status)
