from __future__ import annotations

import csv
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np

from .runner import RunResult

SPILL_EPS_M3 = 0.1
EVENT_GAP_S = 3600


@dataclass(frozen=True)
class PointKpi:
    point: str
    volume_m3: float
    events: int
    spill_hours: float
    swmm_hours_flooded: float


def interval_volumes(flood_cum: np.ndarray) -> np.ndarray:
    first = flood_cum[:1]
    return np.vstack([first, np.diff(flood_cum, axis=0)])


def count_events(spilling: np.ndarray, step_s: int, gap_s: int = EVENT_GAP_S) -> int:
    idx = np.flatnonzero(spilling)
    if idx.size == 0:
        return 0
    dry_s = (np.diff(idx) - 1) * step_s
    return 1 + int(np.sum(dry_s >= gap_s))


def point_kpis(run: RunResult, points: tuple[str, ...], eps_m3: float = SPILL_EPS_M3) -> list[PointKpi]:
    volumes = interval_volumes(run.flood_cum)
    out = []
    for k, point in enumerate(points):
        spilling = volumes[:, k] > eps_m3
        out.append(
            PointKpi(
                point=point,
                volume_m3=float(run.flood_cum[-1, k]),
                events=count_events(spilling, run.step_s),
                spill_hours=float(spilling.sum() * run.step_s / 3600),
                swmm_hours_flooded=float(run.totals["hours_flooded"][point]),
            )
        )
    return out


def total_row(kpis: list[PointKpi]) -> PointKpi:
    return PointKpi(
        point="TOTAL",
        volume_m3=sum(k.volume_m3 for k in kpis),
        events=sum(k.events for k in kpis),
        spill_hours=sum(k.spill_hours for k in kpis),
        swmm_hours_flooded=sum(k.swmm_hours_flooded for k in kpis),
    )


def write_csv(path: Path, rows: dict[str, list[PointKpi]]) -> None:
    """rows: run name -> KPIs (without total); writes one line per run and point."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["run", "point", "volume_m3", "events", "spill_hours", "swmm_hours_flooded"])
        for name, kpis in rows.items():
            for k in [*kpis, total_row(kpis)]:
                d = asdict(k)
                writer.writerow(
                    [
                        name,
                        d["point"],
                        f"{d['volume_m3']:.1f}",
                        d["events"],
                        f"{d['spill_hours']:.2f}",
                        f"{d['swmm_hours_flooded']:.2f}",
                    ]
                )
