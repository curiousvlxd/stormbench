from __future__ import annotations

from collections.abc import Iterable, Sequence

FIRST_BLOCK_S = 12 * 3600
NEXT_BLOCK_S = 24 * 3600


def count_12_24(discharge_starts_s: Iterable[float]) -> int:
    """Spills of one overflow point, from the start times (s) of the intervals in which it discharged."""
    count = 0
    end: float | None = None
    for t in sorted(discharge_starts_s):
        if end is not None and t < end:
            continue
        if end is not None and t < end + NEXT_BLOCK_S:
            end += NEXT_BLOCK_S
        else:
            end = t + FIRST_BLOCK_S
        count += 1
    return count


def discharging_steps(
    times_s: Sequence[float], cumulative_m3: Sequence[float], eps_m3: float = 1.0
) -> list[float]:
    """Start times of the control intervals in which a cumulative overflow volume grew by more than eps_m3.

    Row k of a run describes the interval ending at times_s[k] (runner.RunResult), so the interval
    starts at times_s[k - 1]; the first row starts at times_s[0] minus one step.
    """
    starts: list[float] = []
    previous = 0.0
    for k, (t, v) in enumerate(zip(times_s, cumulative_m3, strict=True)):
        if v - previous > eps_m3:
            step = times_s[1] - times_s[0] if len(times_s) > 1 else 0.0
            starts.append(times_s[k - 1] if k > 0 else t - step)
        previous = v
    return starts
