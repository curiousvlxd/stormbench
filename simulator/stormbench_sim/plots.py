from __future__ import annotations

from datetime import timedelta
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.dates as mdates
import matplotlib.pyplot as plt
import numpy as np

from .runner import RunResult
from .style import DARK, LIGHT, Theme, use_fonts


def _dates(run: RunResult) -> list:
    return [run.start + timedelta(seconds=int(s)) for s in run.times_s]


def cumulative_overflow(base: RunResult, efd: RunResult, out: Path, theme: Theme = LIGHT) -> Path:
    use_fonts()
    fig, ax = plt.subplots(figsize=(10, 5.2), dpi=150)
    fig.patch.set_facecolor(theme.surface)
    ax.set_facecolor(theme.surface)

    series = [
        (base, "Base Case, fixed throttles", theme.base),
        (efd, "EFD on the PLC (OpenPLC, Modbus TCP)", theme.efd),
    ]

    def report_total(run: RunResult) -> float:
        return float(sum(run.totals["report_flood_m3"].values()))

    base_total = report_total(base)
    for run, label, color in series:
        x = _dates(run)
        y = run.flood_cum.sum(axis=1) / 1000.0
        ax.plot(x, y, color=color, linewidth=2, solid_capstyle="round", solid_joinstyle="round", label=label)
        ax.scatter(
            [x[-1]],
            [y[-1]],
            s=64,
            color=color,
            edgecolors=theme.surface,
            linewidths=2,
            zorder=3,
            clip_on=False,
        )
        total = report_total(run)
        text = f"{total:,.0f} m³"
        if run is efd:
            text += f"  ({(total / base_total - 1) * 100:+.1f}%)"
        ax.annotate(
            text,
            (x[-1], y[-1]),
            xytext=(10, 0),
            textcoords="offset points",
            va="center",
            fontsize=11,
            color=theme.ink,
            fontweight="semibold",
            annotation_clip=False,
        )

    ax.set_ylim(0, None)
    ax.yaxis.set_major_formatter(matplotlib.ticker.FuncFormatter(lambda v, _: f"{v:,.0f}k"))
    ax.xaxis.set_major_locator(mdates.MonthLocator())
    ax.xaxis.set_major_formatter(mdates.DateFormatter("%b"))
    ax.set_xlim(base.start, base.start + timedelta(seconds=int(base.times_s[-1])))
    ax.grid(axis="y", color=theme.grid, linewidth=1)
    ax.set_axisbelow(True)
    for side in ("top", "right", "left"):
        ax.spines[side].set_visible(False)
    ax.spines["bottom"].set_color(theme.axis)
    ax.tick_params(colors=theme.ink_muted, labelsize=10, length=0)
    ax.set_ylabel("Cumulative overflow volume (m³)", color=theme.ink_secondary, fontsize=10)

    fig.text(
        0.06,
        0.95,
        "Cumulative overflow, Astlingen benchmark, rain year 2000",
        fontsize=15,
        fontweight="bold",
        color=theme.ink,
        ha="left",
    )
    fig.text(
        0.06,
        0.905,
        "Equal Filling Degree control runs as IEC 61131-3 code on a PLC runtime; "
        "the Python reference gives identical volumes.",
        fontsize=10.5,
        color=theme.ink_secondary,
        ha="left",
    )
    legend = ax.legend(loc="upper left", frameon=False, fontsize=10, labelcolor=theme.ink_secondary)
    for handle in legend.legend_handles:
        handle.set_linewidth(2)
    fig.subplots_adjust(left=0.08, right=0.8, top=0.85, bottom=0.08)
    out.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(out, facecolor=theme.surface)
    plt.close(fig)
    return out


def cumulative_overflow_both_themes(base: RunResult, efd: RunResult, out_dir: Path) -> list[Path]:
    return [
        cumulative_overflow(base, efd, out_dir / "cumulative_overflow.png", LIGHT),
        cumulative_overflow(base, efd, out_dir / "cumulative_overflow_dark.png", DARK),
    ]


def daily_overflow(run: RunResult) -> dict[int, float]:
    """Overflow volume per simulated day (day index since start)."""
    total = run.flood_cum.sum(axis=1)
    days = run.times_s // 86400
    out, previous = {}, 0.0
    for day in np.unique(days):
        last = np.flatnonzero(days == day)[-1]
        out[int(day)] = float(total[last] - previous)
        previous = float(total[last])
    return out
