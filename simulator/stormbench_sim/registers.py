from __future__ import annotations

import struct
from collections.abc import Sequence

HR_ML_BASE = 4096
REGS_PER_VALUE = 4
N_TANKS = 6
N_STATIONS = 4


def ml(index: int) -> int:
    """Holding register address of %ML<index>."""
    return HR_ML_BASE + REGS_PER_VALUE * index


IN_SIM_TIME_S = 0
IN_CMD_MODE = 1
IN_CENTER_OK = 2
IN_DEPTH_FIRST = 3
IN_STEP_REQ = IN_DEPTH_FIRST + N_TANKS
IN_COUNT = IN_STEP_REQ + 1

CENTER_SETPOINT_FIRST = 10
CENTER_STEP = CENTER_SETPOINT_FIRST + N_STATIONS
CENTER_COUNT = CENTER_STEP - CENTER_SETPOINT_FIRST + 1

OUT_FIRST = 16
OUT_SETPOINT_FIRST = 16
OUT_MODE_ACTIVE = OUT_SETPOINT_FIRST + N_STATIONS
OUT_STATUS = OUT_MODE_ACTIVE + 1
OUT_SCAN_COUNT = OUT_STATUS + 1
OUT_STEP_ACK = OUT_SCAN_COUNT + 1
OUT_COUNT = OUT_STEP_ACK - OUT_FIRST + 1


def _words(raw: bytes) -> list[int]:
    return list(struct.unpack(">4H", raw))


def _raw(words: Sequence[int]) -> bytes:
    return struct.pack(">4H", *words)


def pack_lint(value: int) -> list[int]:
    return _words(struct.pack(">q", value))


def pack_lreal(value: float) -> list[int]:
    return _words(struct.pack(">d", value))


def unpack_lint(words: Sequence[int]) -> int:
    return struct.unpack(">q", _raw(words))[0]


def unpack_lreal(words: Sequence[int]) -> float:
    return struct.unpack(">d", _raw(words))[0]


def encode_inputs(
    step_req: int, sim_time_s: int, mode: int, center_ok: bool, depths: Sequence[float]
) -> list[int]:
    if len(depths) != N_TANKS:
        raise ValueError(f"expected {N_TANKS} tank depths, got {len(depths)}")
    regs = pack_lint(sim_time_s) + pack_lint(int(mode)) + pack_lint(1 if center_ok else 0)
    for depth in depths:
        regs += pack_lreal(depth)
    regs += pack_lint(step_req)
    assert len(regs) == IN_COUNT * REGS_PER_VALUE
    return regs


def encode_center(step: int, proposal: Sequence[float]) -> list[int]:
    if len(proposal) != N_STATIONS:
        raise ValueError(f"expected {N_STATIONS} proposals, got {len(proposal)}")
    regs: list[int] = []
    for value in proposal:
        regs += pack_lreal(value)
    regs += pack_lint(step)
    return regs


def decode_outputs(regs: Sequence[int]) -> tuple[int, tuple[float, ...], int, int, int]:
    """Return (step_ack, setpoints, mode_active, status, scan_count)."""
    if len(regs) != OUT_COUNT * REGS_PER_VALUE:
        raise ValueError(f"expected {OUT_COUNT * REGS_PER_VALUE} registers, got {len(regs)}")

    def value(index: int) -> Sequence[int]:
        offset = (index - OUT_FIRST) * REGS_PER_VALUE
        return regs[offset : offset + REGS_PER_VALUE]

    setpoints = tuple(unpack_lreal(value(OUT_SETPOINT_FIRST + i)) for i in range(N_STATIONS))
    return (
        unpack_lint(value(OUT_STEP_ACK)),
        setpoints,
        unpack_lint(value(OUT_MODE_ACTIVE)),
        unpack_lint(value(OUT_STATUS)),
        unpack_lint(value(OUT_SCAN_COUNT)),
    )
