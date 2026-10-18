from __future__ import annotations

import json
import re
import threading
import time
from collections.abc import Callable, Sequence
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Protocol

import numpy as np
from pyswmm import Links, Nodes, RainGages, Simulation

from .control import ControlInputs, ControlOutputs, Mode, decide
from .network import Controls, Network, prepare_inp


class Controller(Protocol):
    """Decides the settings of every station for one control step: the whole field side.

    `decide` is called once per control step, after the model has advanced one interval, with the
    levels the field reports (in Network.tanks order); the setpoints it returns (in Network.stations
    order) act during the next interval. Implementations: the Python reference of the PLC program
    (PythonController), the PLC itself (plc.PlcController), any center behind the Python station
    checks (CenterController). See docs/controllers.md."""

    name: str

    def decide(self, inputs: ControlInputs) -> ControlOutputs: ...


class Center(Protocol):
    """A central algorithm: proposes one setting per station, the stations decide (mode 3,
    ADR 0004 and 0007). `observe` reports what the stations applied: the proposal, or their local
    setting when they did not take it. See docs/controllers.md."""

    name: str

    def propose(self, inputs: ControlInputs) -> Sequence[float]: ...

    def observe(self, inputs: ControlInputs, outputs: ControlOutputs) -> None: ...


class PythonController:
    name = "python"

    def __init__(self, network: Network):
        self.network = network

    def decide(self, inputs: ControlInputs) -> ControlOutputs:
        return decide(inputs, self.network)


class CenterController:
    """A center behind the Python reference of the PLC stations: the offline twin of the PLC in
    mode 3 with this center (same checks, same fallback), for runs without a PLC."""

    def __init__(self, center: Center, network: Network):
        self.center, self.network = center, network
        self.name = f"python-{center.name}"

    def decide(self, inputs: ControlInputs) -> ControlOutputs:
        proposal = None
        if inputs.mode == Mode.EFD_CENTER and inputs.center_ok:
            proposal = list(self.center.propose(inputs))
        out = decide(inputs, self.network, proposal)
        self.center.observe(inputs, out)
        return out


@dataclass(frozen=True)
class StepRecord:
    """Row k of a run, handed to RunSpec.observer as soon as the runner has recorded it."""

    k: int
    time: datetime
    depths: tuple[float, ...]
    flood_cum: tuple[float, ...]
    wwtp_cum: float
    rain_mmph: tuple[float, ...]
    outputs: ControlOutputs
    mismatches: int = 0
    center_link_up: bool | None = None
    measured: tuple[float, ...] | None = None
    openings: tuple[float, ...] | None = None
    plc_step: int | None = None


@dataclass
class FieldFaults:
    """Faults of the simulated field, switched live by the test console (None: healthy).

    `stuck_gate`: this controlled outlet keeps its opening whatever the PLC asks for.
    `frozen_sensor`: this tank's level reading stays at the value it had when the fault began.
    """

    stuck_gate: str | None = None
    frozen_sensor: str | None = None


@dataclass
class RunSpec:
    """`observer` sees every step; setting `stop` ends the run after the current step."""

    name: str
    network: Network
    controls: Controls
    mode: Mode = Mode.LOCAL
    controller: Controller | None = None
    period: tuple[datetime, datetime] | None = None
    center_outage: tuple[datetime, datetime] | None = None
    pace_s: float = 0.0
    observer: Callable[[StepRecord], None] | None = None
    stop: threading.Event | None = None
    faults: FieldFaults | None = None


@dataclass
class RunResult:
    """One simulation. Row k describes the control interval ending at times_s[k]: depths and
    cumulative volumes at its end, and the settings, mode and status in effect during it."""

    name: str
    controller: str
    mode: Mode
    start: datetime
    step_s: int
    times_s: np.ndarray
    depths: np.ndarray
    setpoints: np.ndarray
    mode_active: np.ndarray
    status: np.ndarray
    flood_cum: np.ndarray
    wwtp_cum: np.ndarray
    rain_mmph: np.ndarray
    totals: dict
    meta: dict = field(default_factory=dict)

    def save(self, directory: Path) -> None:
        directory.mkdir(parents=True, exist_ok=True)
        np.savez_compressed(
            directory / "series.npz",
            times_s=self.times_s,
            depths=self.depths,
            setpoints=self.setpoints,
            mode_active=self.mode_active,
            status=self.status,
            flood_cum=self.flood_cum,
            wwtp_cum=self.wwtp_cum,
            rain_mmph=self.rain_mmph,
        )
        summary = {
            "name": self.name,
            "controller": self.controller,
            "mode": self.mode.name,
            "start": self.start.isoformat(),
            "step_s": self.step_s,
            "totals": self.totals,
            "meta": self.meta,
        }
        (directory / "summary.json").write_text(
            json.dumps(summary, indent=2, default=str), encoding="utf-8", newline="\n"
        )

    @classmethod
    def load(cls, directory: Path) -> RunResult:
        summary = json.loads((directory / "summary.json").read_text(encoding="utf-8"))
        data = np.load(directory / "series.npz")
        return cls(
            name=summary["name"],
            controller=summary["controller"],
            mode=Mode[summary["mode"]],
            start=datetime.fromisoformat(summary["start"]),
            step_s=summary["step_s"],
            times_s=data["times_s"],
            depths=data["depths"],
            setpoints=data["setpoints"],
            mode_active=data["mode_active"],
            status=data["status"],
            flood_cum=data["flood_cum"],
            wwtp_cum=data["wwtp_cum"],
            rain_mmph=data["rain_mmph"],
            totals=summary["totals"],
            meta=summary["meta"],
        )


def parse_report(rpt: Path) -> dict:
    """Continuity errors and the Node Flooding Summary from a SWMM .rpt file."""
    text = rpt.read_text(encoding="latin-1")
    errors = [float(x) for x in re.findall(r"Continuity Error \(%\) \.+\s+(-?\d+\.\d+)", text)]
    flooding = {}
    section = re.search(r"Node Flooding Summary\s*\n\s*\*+\s*\n(.*?)(?=\n\s*\*{5,}\s*\n|\Z)", text, re.S)
    body = section.group(1) if section else ""
    block = re.search(r"-{20,}\n.*?-{20,}\n(.*?)(?:\n\s*\n|\Z)", body, re.S)
    if block:
        for line in block.group(1).splitlines():
            parts = line.split()
            if len(parts) >= 6:
                flooding[parts[0]] = float(parts[5]) * 1000.0
    return {
        "runoff_continuity_error_pct": errors[0] if errors else None,
        "routing_continuity_error_pct": errors[1] if len(errors) > 1 else None,
        "report_flood_m3": flooding,
    }


def run(spec: RunSpec, work_dir: Path, progress: Callable[[str], None] | None = print) -> RunResult:
    net = spec.network
    external = spec.controls is Controls.EXTERNAL
    if external and spec.controller is None:
        raise ValueError("external control needs a controller")
    inp = prepare_inp(net, work_dir, spec.controls, spec.name, spec.period)

    times, depths, setpoints, modes, statuses, flood, wwtp, rain = [], [], [], [], [], [], [], []
    wall0 = time.perf_counter()
    with Simulation(str(inp)) as sim:
        nodes, links = Nodes(sim), Links(sim)
        tanks = [nodes[t] for t in net.tanks]
        points = [nodes[p] for p in net.overflow_points]
        valves = [links[s.valve] for s in net.stations]
        outfall = nodes[net.wwtp_outfall]
        rain_gauges = list(RainGages(sim))
        in_effect = ControlOutputs(tuple(s.base_setting for s in net.stations), Mode.LOCAL, 0)
        sim.step_advance(net.control_step_s)
        sim.start()
        if external:
            for valve, sp in zip(valves, in_effect.setpoints, strict=True):
                valve.target_setting = sp
        start = sim.start_time
        end = sim.end_time
        total_s = (end - start).total_seconds()
        shadow = getattr(spec.controller, "shadow", None)
        stopped = False
        held_level: tuple[str, float] | None = None
        next_report = 0.1
        tick = time.perf_counter()
        for _ in sim:
            now = sim.current_time
            t_s = round((now - start).total_seconds())
            level = tuple(t.depth for t in tanks)
            measured = level
            if external:
                setpoints.append(in_effect.setpoints)
                modes.append(int(in_effect.mode_active))
                statuses.append(in_effect.status)
                faults = spec.faults
                frozen = faults.frozen_sensor if faults is not None else None
                if frozen in net.tanks:
                    if held_level is None or held_level[0] != frozen:
                        held_level = (frozen, level[net.tanks.index(frozen)])
                    measured = tuple(
                        held_level[1] if name == frozen else value
                        for name, value in zip(net.tanks, level, strict=True)
                    )
                else:
                    held_level = None
                center_ok = not (spec.center_outage and spec.center_outage[0] <= now < spec.center_outage[1])
                in_effect = spec.controller.decide(ControlInputs(t_s, spec.mode, center_ok, measured))
                stuck = faults.stuck_gate if faults is not None else None
                for station, valve, sp in zip(net.stations, valves, in_effect.setpoints, strict=True):
                    if station.valve != stuck:
                        valve.target_setting = sp
            else:
                setpoints.append(tuple(v.target_setting for v in valves))
                modes.append(int(Mode.EFD_PUBLISHED if spec.controls is Controls.EFD_RULES else Mode.LOCAL))
                statuses.append(0)
            times.append(t_s)
            depths.append(level)
            flood.append([p.statistics["flooding_volume"] for p in points])
            wwtp.append(outfall.cumulative_inflow)
            rain.append([g.total_precip for g in rain_gauges])
            if spec.observer is not None:
                spec.observer(
                    StepRecord(
                        k=len(times) - 1,
                        time=now,
                        depths=level,
                        flood_cum=tuple(flood[-1]),
                        wwtp_cum=wwtp[-1],
                        rain_mmph=tuple(rain[-1]),
                        outputs=ControlOutputs(setpoints[-1], Mode(modes[-1]), statuses[-1]),
                        mismatches=shadow.mismatches if shadow is not None else 0,
                        center_link_up=getattr(spec.controller, "center_link_up", None),
                        measured=measured if external else None,
                        openings=tuple(v.target_setting for v in valves),
                        plc_step=getattr(getattr(spec.controller, "link", None), "seq", None),
                    )
                )
            if spec.pace_s:
                tick += spec.pace_s
                delay = tick - time.perf_counter()
                if delay > 0:
                    if spec.stop is None:
                        time.sleep(delay)
                    else:
                        spec.stop.wait(delay)
            if progress and t_s / total_s >= next_report:
                progress(f"  {spec.name}: {now:%Y-%m-%d} ({next_report:.0%})")
                next_report += 0.1
            if spec.stop is not None and spec.stop.is_set():
                stopped = True
                break
        final_flood = {
            p: n.statistics["flooding_volume"] for p, n in zip(net.overflow_points, points, strict=True)
        }
        hours_flooded = {
            p: n.statistics["flooding_duration"] for p, n in zip(net.overflow_points, points, strict=True)
        }
        wwtp_total = outfall.cumulative_inflow
    wall_s = time.perf_counter() - wall0
    report = parse_report(inp.with_suffix(".rpt"))

    totals = {
        "flood_m3": final_flood,
        "flood_total_m3": sum(final_flood.values()),
        "hours_flooded": hours_flooded,
        "wwtp_inflow_m3": wwtp_total,
        **report,
    }
    meta = {"wall_s": round(wall_s, 2), "steps": len(times), "inp": inp.name}
    if stopped:
        meta["stopped"] = True
    return RunResult(
        name=spec.name,
        controller=spec.controller.name if spec.controller else f"swmm-{spec.controls.value}",
        mode=spec.mode if external else Mode(modes[0]),
        start=start,
        step_s=net.control_step_s,
        times_s=np.asarray(times, dtype=np.int64),
        depths=np.asarray(depths, dtype=float),
        setpoints=np.asarray(setpoints, dtype=float),
        mode_active=np.asarray(modes, dtype=np.int64),
        status=np.asarray(statuses, dtype=np.int64),
        flood_cum=np.asarray(flood, dtype=float),
        wwtp_cum=np.asarray(wwtp, dtype=float),
        rain_mmph=np.asarray(rain, dtype=float),
        totals=totals,
        meta=meta,
    )
