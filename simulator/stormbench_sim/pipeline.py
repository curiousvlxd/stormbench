from __future__ import annotations

import argparse
import importlib
import threading
from collections.abc import Callable
from datetime import datetime
from pathlib import Path

from .control import Mode
from .network import Controls, Network, load_network
from .plc import PlcController, PlcLink
from .runner import Center, CenterController, PythonController, RunResult, RunSpec, StepRecord, run

CONTROLS = {
    "swmm-base": Controls.BASE,
    "swmm-efd": Controls.EFD_RULES,
    "python": Controls.EXTERNAL,
    "plc": Controls.EXTERNAL,
}
MODES = {
    "local": Mode.LOCAL,
    "efd-published": Mode.EFD_PUBLISHED,
    "efd-intended": Mode.EFD_INTENDED,
    "efd-center": Mode.EFD_CENTER,
}

STORMS = {
    "storm-2000-05-30": (datetime(2000, 5, 30, 4, 0), datetime(2000, 5, 31, 10, 0)),
    "storm-2000-10-04": (datetime(2000, 10, 4, 12, 0), datetime(2000, 10, 5, 6, 0)),
}


def parse_dt(text: str) -> datetime:
    return datetime.fromisoformat(text)


def period_of(args: argparse.Namespace) -> tuple[datetime, datetime] | None:
    if args.start and args.end:
        return parse_dt(args.start), parse_dt(args.end)
    return None


CENTERS = ("uns", "emulated", "mpc")


def center_option(text: str) -> str:
    """argparse type of --center: a built-in center or a plugin "package.module:factory"."""
    module, _, attr = text.partition(":")
    if text in CENTERS or (module and attr):
        return text
    raise argparse.ArgumentTypeError(f"expected one of {', '.join(CENTERS)} or package.module:factory")


def load_center(
    spec: str, net: Network, work_dir: Path, period: tuple[datetime, datetime] | None, name: str, args
) -> Center | None:
    """The center this process plays in mode 3: None for the UNS and the emulated EFD (handled by
    PlcController), the MPC, or a plugin factory called as factory(network, work_dir, period)."""
    if spec == "mpc":
        from .mpc import MpcCenter, MpcConfig

        config = MpcConfig(forecast=getattr(args, "mpc_forecast", None) or "perfect")
        return MpcCenter.for_run(net, work_dir, period, config, tag=f"{name}-forecast")
    if spec in CENTERS:
        return None
    module, _, attr = spec.partition(":")
    factory = getattr(importlib.import_module(module), attr)
    return factory(net, work_dir, period)


def execute(
    name: str,
    control: str,
    mode: Mode,
    args: argparse.Namespace,
    net: Network,
    *,
    observer: Callable[[StepRecord], None] | None = None,
    stop: threading.Event | None = None,
) -> RunResult:
    controls = CONTROLS[control]
    controller = None
    link = None
    center_kind = getattr(args, "center", "uns")
    center = None
    if mode == Mode.EFD_CENTER and control in ("python", "plc"):
        center = load_center(center_kind, net, Path(args.work_dir), period_of(args), name, args)
    if control == "python":
        controller = PythonController(net) if center is None else CenterController(center, net)
    elif control == "plc":
        link = PlcLink(args.plc_host, args.plc_port, timeout_s=args.plc_timeout)
        link.connect(attempts=getattr(args, "plc_attempts", 30))
        controller = PlcController(
            link,
            net,
            shadow=not args.no_shadow,
            emulate_center=center_kind == "emulated",
            center_variant=MODES[getattr(args, "center_variant", "efd-published")],
            link_cut=getattr(args, "link_cut", None),
            center=center,
        )
    outage = (parse_dt(args.outage[0]), parse_dt(args.outage[1])) if getattr(args, "outage", None) else None
    spec = RunSpec(
        name,
        net,
        controls,
        mode,
        controller,
        period_of(args),
        outage,
        args.pace,
        observer,
        stop,
        getattr(args, "faults", None),
    )
    try:
        result = run(spec, Path(args.work_dir), progress=print)
    finally:
        if link is not None:
            link.close()
    if isinstance(controller, PlcController):
        result.meta["plc"] = {
            "host": args.plc_host,
            "exchanges": link.stats.exchanges,
            "polls": link.stats.polls,
            "wait_s": round(link.stats.wait_s, 2),
            "center": center_kind if mode == Mode.EFD_CENTER else None,
            "center_fallbacks": controller.center_fallbacks,
        }
        if controller.shadow is not None:
            result.meta["shadow_mismatches"] = controller.shadow.mismatches
            if controller.shadow.first:
                result.meta["first_mismatch"] = repr(controller.shadow.first)
    summary = getattr(center, "summary", None)
    if callable(summary):
        result.meta[center.name] = summary()
    result.save(Path(args.results_dir) / "runs" / name)
    total = sum(result.totals["report_flood_m3"].values())
    print(f"{name}: {total:,.0f} m³ overflow, {result.meta['steps']:,} steps, {result.meta['wall_s']} s")
    return result


def execute_offline(job: tuple[str, str, int, dict]) -> str:
    name, control, mode, arg_dict = job
    args = argparse.Namespace(**arg_dict)
    execute(name, control, Mode(mode), args, load_network(args.network, Path(args.model_dir)))
    return name


def load_runs(results_dir: Path) -> dict[str, RunResult]:
    runs = {}
    for directory in sorted((results_dir / "runs").glob("*/")):
        if (directory / "summary.json").exists():
            runs[directory.name] = RunResult.load(directory)
    return runs
