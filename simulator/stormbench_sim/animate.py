from __future__ import annotations

import shutil
import subprocess
from datetime import datetime, timedelta
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import imageio_ffmpeg
import matplotlib.pyplot as plt
import numpy as np
from matplotlib import animation
from matplotlib.lines import Line2D
from matplotlib.patches import Rectangle

from .kpi import SPILL_EPS_M3, interval_volumes
from .network import Network, read_layout
from .runner import RunResult
from .style import HMI, LIGHT, use_fonts

TANK_W, TANK_H = 420.0, 640.0
LABEL_BOX = {"boxstyle": "square,pad=0.15", "facecolor": HMI["panel"], "edgecolor": "none"}


def window_indices(run: RunResult, start: datetime, end: datetime) -> np.ndarray:
    t0 = (start - run.start).total_seconds()
    t1 = (end - run.start).total_seconds()
    return np.flatnonzero((run.times_s > t0) & (run.times_s <= t1))


def _panel(ax, net: Network, layout, title: str, subtitle: str) -> dict:
    ax.set_facecolor(HMI["panel"])
    for spine in ax.spines.values():
        spine.set_visible(False)
    ax.set_xticks([])
    ax.set_yticks([])
    xs = [c[0] for c in layout.coords.values()]
    ys = [c[1] for c in layout.coords.values()]
    ax.set_xlim(min(xs) - 700, max(xs) + 700)
    ax.set_ylim(min(ys) - 600, max(ys) + 900)
    ax.set_aspect("equal")
    for _, a, b in layout.links:
        (x1, y1), (x2, y2) = layout.coords[a], layout.coords[b]
        ax.plot([x1, x2], [y1, y2], color=HMI["pipe"], linewidth=2.2, solid_capstyle="round", zorder=1)

    ox, oy = layout.coords[net.wwtp_outfall]
    ax.add_patch(
        Rectangle(
            (ox - 260, oy - 200),
            520,
            400,
            facecolor=HMI["tank_empty"],
            edgecolor=HMI["structure"],
            linewidth=1.4,
            zorder=2,
        )
    )
    ax.text(ox, oy - 360, "WWTP", ha="center", va="top", fontsize=8, color=HMI["ink_secondary"], zorder=5)

    artists: dict = {"tanks": {}, "cso": {}, "valve_text": {}}
    controlled = {s.tank: s.valve for s in net.stations}
    for tank in net.tanks:
        x, y = layout.coords[tank]
        body = Rectangle(
            (x - TANK_W / 2, y - TANK_H / 2),
            TANK_W,
            TANK_H,
            facecolor=HMI["tank_empty"],
            edgecolor=HMI["structure"],
            linewidth=1.4,
            zorder=3,
        )
        water = Rectangle(
            (x - TANK_W / 2, y - TANK_H / 2), TANK_W, 0.0, facecolor=HMI["water"], edgecolor="none", zorder=4
        )
        ax.add_patch(body)
        ax.add_patch(water)
        ax.text(
            x,
            y + TANK_H / 2 + 90,
            tank,
            ha="center",
            va="bottom",
            fontsize=9,
            fontweight="bold",
            color=HMI["ink"],
            zorder=6,
            bbox=LABEL_BOX,
        )
        spill = ax.scatter(
            [x], [y + TANK_H / 2 + 420], marker="^", s=70, color=HMI["alarm"], zorder=7, visible=False
        )
        level = ax.text(
            x + TANK_W / 2 + 70,
            y + TANK_H / 2 - 40,
            "",
            ha="left",
            va="top",
            fontsize=7.5,
            color=HMI["ink_secondary"],
            zorder=6,
            bbox=LABEL_BOX,
        )
        artists["tanks"][tank] = (body, water, spill, level)
        valve = controlled.get(tank)
        label = f"{valve} " if valve else "fixed"
        artists["valve_text"][tank] = ax.text(
            x + TANK_W / 2 + 70,
            y - TANK_H / 2 + 20,
            label,
            ha="left",
            va="bottom",
            fontsize=7.5,
            color=HMI["ink_muted"],
            zorder=6,
            bbox=LABEL_BOX,
        )
    for point in net.overflow_points:
        if point in net.tanks:
            continue
        x, y = layout.coords[point]
        marker = ax.scatter(
            [x],
            [y],
            marker="D",
            s=46,
            facecolor=HMI["tank_empty"],
            edgecolor=HMI["structure"],
            linewidths=1.3,
            zorder=5,
        )
        spill = ax.scatter([x], [y + 380], marker="^", s=60, color=HMI["alarm"], zorder=7, visible=False)
        dx, dy = net.label_offsets.get(point, (0.0, -250.0))
        ax.text(
            x + dx,
            y + dy,
            point,
            ha="center",
            va="top",
            fontsize=7.5,
            color=HMI["ink_secondary"],
            zorder=6,
            bbox=LABEL_BOX,
        )
        artists["cso"][point] = (marker, spill)

    ax.text(
        0.02,
        0.97,
        title,
        transform=ax.transAxes,
        ha="left",
        va="top",
        fontsize=12.5,
        fontweight="bold",
        color=HMI["ink"],
    )
    ax.text(
        0.02,
        0.905,
        subtitle,
        transform=ax.transAxes,
        ha="left",
        va="top",
        fontsize=9,
        color=HMI["ink_secondary"],
    )
    artists["counter"] = ax.text(
        0.98,
        0.97,
        "",
        transform=ax.transAxes,
        ha="right",
        va="top",
        fontsize=16,
        fontweight="bold",
        color=HMI["ink"],
    )
    ax.text(
        0.98,
        0.875,
        "overflow in this storm",
        transform=ax.transAxes,
        ha="right",
        va="top",
        fontsize=8.5,
        color=HMI["ink_secondary"],
    )
    artists["delta"] = ax.text(
        0.98,
        0.815,
        "",
        transform=ax.transAxes,
        ha="right",
        va="top",
        fontsize=8.5,
        fontweight="bold",
        color=HMI["ink"],
    )
    return artists


def render_storm(
    base: RunResult,
    efd: RunResult,
    net: Network,
    start: datetime,
    end: datetime,
    out_mp4: Path,
    fps: int = 10,
    gif: Path | None = None,
    gif_width: int = 800,
) -> list[Path]:
    use_fonts()
    layout = read_layout(net)
    idx = window_indices(base, start, end)
    if idx.size == 0 or not np.array_equal(base.times_s[idx], efd.times_s[idx]):
        raise ValueError("runs do not cover the same window")
    k0 = idx[0] - 1 if idx[0] > 0 else None

    fig = plt.figure(figsize=(12.8, 7.2), dpi=100)
    fig.patch.set_facecolor(HMI["background"])
    ax_l = fig.add_axes((0.015, 0.33, 0.48, 0.55))
    ax_r = fig.add_axes((0.505, 0.33, 0.48, 0.55))
    ax_rain = fig.add_axes((0.08, 0.19, 0.895, 0.1))
    ax_cum = fig.add_axes((0.08, 0.045, 0.895, 0.12))

    panels = [
        (base, _panel(ax_l, net, layout, "Base Case", "fixed throttle settings, local control")),
        (
            efd,
            _panel(ax_r, net, layout, "EFD on the PLC", "IEC 61131-3 on OpenPLC, setpoints over Modbus TCP"),
        ),
    ]
    fig.text(
        0.015,
        0.965,
        "Same storm, same network, two control strategies",
        fontsize=17,
        fontweight="bold",
        color=HMI["ink"],
        ha="left",
        va="center",
    )
    fig.text(
        0.015,
        0.922,
        "Astlingen RTC benchmark in EPA SWMM, closed loop with a PLC runtime in lockstep",
        fontsize=10.5,
        color=HMI["ink_secondary"],
        ha="left",
        va="center",
    )
    clock = fig.text(
        0.985, 0.955, "", fontsize=17, fontweight="bold", color=HMI["ink"], ha="right", va="center"
    )
    legend_text = fig.text(
        0.985, 0.915, "overflowing now", fontsize=9.5, color=HMI["ink_secondary"], ha="right", va="center"
    )
    fig.canvas.draw()
    legend_left = legend_text.get_window_extent().transformed(fig.transFigure.inverted()).x0
    fig.add_artist(
        Line2D(
            [legend_left - 0.012],
            [0.916],
            marker="^",
            markersize=8,
            color=HMI["alarm"],
            linestyle="none",
            transform=fig.transFigure,
        )
    )

    dates = [base.start + timedelta(seconds=int(s)) for s in base.times_s[idx]]
    hours = (base.times_s[idx] - base.times_s[idx[0]]) / 3600.0
    rain = base.rain_mmph[idx].mean(axis=1)
    ax_rain.bar(hours, rain, width=5 / 60, color=HMI["water_light"], align="edge")
    ax_rain.set_xlim(hours[0], hours[-1] + 5 / 60)
    ax_rain.set_ylabel(
        "rain\nmm/h", fontsize=8, color=HMI["ink_secondary"], rotation=0, ha="right", va="center"
    )
    rain_cursor = ax_rain.axvline(hours[0], color=HMI["ink"], linewidth=1.2)

    def window_cum(run: RunResult) -> np.ndarray:
        total = run.flood_cum.sum(axis=1)
        before = total[k0] if k0 is not None else 0.0
        return total[idx] - before

    cum_base, cum_efd = window_cum(base), window_cum(efd)
    ax_cum.plot(hours, cum_base, color=LIGHT.base, linewidth=2, label="Base Case")
    ax_cum.plot(hours, cum_efd, color=LIGHT.efd, linewidth=2, label="EFD on the PLC")
    ax_cum.set_xlim(hours[0], hours[-1] + 5 / 60)
    ax_cum.set_ylim(0, max(cum_base.max(), cum_efd.max(), 1.0) * 1.12)
    ax_cum.set_ylabel(
        "overflow\nm³", fontsize=8, color=HMI["ink_secondary"], rotation=0, ha="right", va="center"
    )
    ax_cum.legend(loc="upper left", frameon=False, fontsize=8.5, ncol=2, labelcolor=HMI["ink_secondary"])
    cum_cursor = ax_cum.axvline(hours[0], color=HMI["ink"], linewidth=1.2)
    for ax in (ax_rain, ax_cum):
        ax.set_facecolor(HMI["panel"])
        for side in ("top", "right", "left"):
            ax.spines[side].set_visible(False)
        ax.spines["bottom"].set_color(HMI["pipe"])
        ax.tick_params(colors=HMI["ink_muted"], labelsize=8, length=0)
        ax.grid(axis="y", color=HMI["background"], linewidth=1)
        ax.set_axisbelow(True)
    ax_rain.set_xticklabels([])
    ticks = np.arange(0, hours[-1] + 1e-9, 3)
    ax_cum.set_xticks(ticks)
    ax_cum.set_xticklabels(
        [f"{(dates[0] - timedelta(minutes=5) + timedelta(hours=float(h))):%H:%M}" for h in ticks]
    )

    volumes = {id(run): interval_volumes(run.flood_cum) for run, _ in panels}
    tank_index = {t: i for i, t in enumerate(net.tanks)}
    point_index = {p: i for i, p in enumerate(net.overflow_points)}
    station_of_tank = {s.tank: i for i, s in enumerate(net.stations)}

    def draw(frame: int):
        k = idx[frame]
        clock.set_text(f"{dates[frame]:%d %b %Y  %H:%M}")
        rain_cursor.set_xdata([hours[frame]] * 2)
        cum_cursor.set_xdata([hours[frame]] * 2)
        for run, art in panels:
            vol = volumes[id(run)][k]
            for tank, (body, water, spill, level) in art["tanks"].items():
                depth = run.depths[k, tank_index[tank]]
                fill = min(max(depth / net.tank_max_depth_m, 0.0), 1.0)
                water.set_height(TANK_H * fill)
                spilling = vol[point_index[tank]] > SPILL_EPS_M3
                body.set_edgecolor(HMI["alarm"] if spilling else HMI["structure"])
                body.set_linewidth(2.4 if spilling else 1.4)
                spill.set_visible(bool(spilling))
                level.set_text(f"{fill:.0%}")
                if tank in station_of_tank:
                    valve = net.stations[station_of_tank[tank]].valve
                    art["valve_text"][tank].set_text(f"{valve} {run.setpoints[k, station_of_tank[tank]]:.0%}")
            for point, (marker, spill) in art["cso"].items():
                spilling = vol[point_index[point]] > SPILL_EPS_M3
                marker.set_facecolor(HMI["alarm"] if spilling else HMI["tank_empty"])
                spill.set_visible(bool(spilling))
            total = run.flood_cum[k].sum() - (run.flood_cum[k0].sum() if k0 is not None else 0.0)
            art["counter"].set_text(f"{total:,.0f} m³")
        diff = cum_efd[frame] - cum_base[frame]
        if cum_base[frame] > 1.0:
            panels[1][1]["delta"].set_text(f"{diff:+,.0f} m³ ({diff / cum_base[frame]:+.0%}) vs Base Case")
        return []

    matplotlib.rcParams["animation.ffmpeg_path"] = imageio_ffmpeg.get_ffmpeg_exe()
    anim = animation.FuncAnimation(fig, draw, frames=len(idx), interval=1000 / fps, blit=False)
    out_mp4.parent.mkdir(parents=True, exist_ok=True)
    writer = animation.FFMpegWriter(fps=fps, bitrate=2400, extra_args=["-pix_fmt", "yuv420p"])
    anim.save(str(out_mp4), writer=writer, dpi=100)
    draw(poster_frame(base, efd, net, idx))
    poster = out_mp4.with_suffix(".png")
    fig.savefig(poster, dpi=100, facecolor=HMI["background"])
    plt.close(fig)
    outputs = [out_mp4, poster]
    if gif is not None:
        outputs.append(mp4_to_gif(out_mp4, gif, width=gif_width, fps=fps))
    return outputs


def poster_frame(base: RunResult, efd: RunResult, net: Network, idx: np.ndarray) -> int:
    """Frame where the controlled tanks differ most in spread of filling between the two strategies."""
    cols = list(net.station_tank_index())
    spread_base = base.depths[idx][:, cols].std(axis=1)
    spread_efd = efd.depths[idx][:, cols].std(axis=1)
    return int(np.argmax(spread_base - spread_efd))


def mp4_to_gif(mp4: Path, gif: Path, width: int = 800, fps: int = 10) -> Path:
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe() or shutil.which("ffmpeg")
    graph = (
        f"fps={fps},scale={width}:-1:flags=lanczos,split[a][b];"
        "[a]palettegen=max_colors=96:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4"
    )
    subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-i", str(mp4), "-vf", graph, str(gif)], check=True)
    return gif
