from __future__ import annotations

import argparse
import os
import sys
import time
from concurrent.futures import ProcessPoolExecutor
from datetime import timedelta
from pathlib import Path

from .control import Mode
from .network import load_network
from .pipeline import CONTROLS, MODES, STORMS, center_option, execute, execute_offline, load_runs, parse_dt


def cmd_run(args: argparse.Namespace) -> int:
    net = load_network(args.network, Path(args.model_dir))
    result = execute(args.name, args.control, MODES[args.mode], args, net)
    mismatches = result.meta.get("shadow_mismatches")
    return 1 if mismatches else 0


def cmd_report(args: argparse.Namespace) -> int:
    from .plots import cumulative_overflow_both_themes
    from .report import write_report

    net = load_network(args.network, Path(args.model_dir))
    runs = load_runs(Path(args.results_dir))
    summary = write_report(runs, net, Path(args.results_dir))
    efd = runs.get("efd-plc") or runs.get("efd-python") or runs["efd-swmm"]
    cumulative_overflow_both_themes(runs["bc"], efd, Path(args.results_dir))
    print((Path(args.results_dir) / "validation.md").read_text(encoding="utf-8"))
    check = summary.get("plc_vs_python")
    return 1 if check and not check["identical_setpoints"] else 0


def cmd_animate(args: argparse.Namespace) -> int:
    from .animate import render_storm

    net = load_network(args.network, Path(args.model_dir))
    runs = load_runs(Path(args.results_dir))
    base, efd = runs[args.left], runs[args.right]
    if args.storm:
        windows = {args.storm: STORMS[args.storm]}
    elif args.start:
        start = parse_dt(args.start)
        windows = {f"storm-{start:%Y-%m-%d}": (start, start + timedelta(hours=args.hours))}
    else:
        windows = STORMS
    for i, (label, (start, end)) in enumerate(windows.items()):
        gif = Path(args.results_dir) / f"{label}.gif" if i == 0 else None
        outputs = render_storm(base, efd, net, start, end, Path(args.results_dir) / f"{label}.mp4", gif=gif)
        print("wrote", ", ".join(str(p) for p in outputs))
    return 0


def cmd_demo(args: argparse.Namespace) -> int:
    t0 = time.perf_counter()
    net = load_network(args.network, Path(args.model_dir))
    print(f"Astlingen model: {net.model_dir}")
    offline = [
        ("bc", "swmm-base", Mode.LOCAL),
        ("efd-swmm", "swmm-efd", Mode.EFD_PUBLISHED),
        ("efd-python", "python", Mode.EFD_PUBLISHED),
        ("efd-intended", "python", Mode.EFD_INTENDED),
    ]
    arg_dict = {k: v for k, v in vars(args).items() if k != "func"}
    arg_dict.update(pace=0.0, start=None, end=None, outage=None)
    with ProcessPoolExecutor(max_workers=min(4, os.cpu_count() or 1)) as pool:
        futures = [pool.submit(execute_offline, (n, c, int(m), arg_dict)) for n, c, m in offline]
        if args.plc_host:
            print(
                f"PLC in the loop: {args.plc_host}:{args.plc_port}, EFD as published, lockstep over Modbus TCP"
            )
            execute("efd-plc", "plc", Mode.EFD_PUBLISHED, argparse.Namespace(**arg_dict), net)
        for future in futures:
            future.result()
    code = cmd_report(args)
    if not args.skip_animation:
        args.left, args.right = "bc", "efd-plc" if args.plc_host else "efd-python"
        args.storm, args.start = None, None
        cmd_animate(args)
    print(f"demo finished in {time.perf_counter() - t0:.0f} s; results in {args.results_dir}")
    return code


def cmd_compare(args: argparse.Namespace) -> int:
    from .evaluate import cmd_compare as compare

    return compare(args)


def cmd_serve(args: argparse.Namespace) -> int:
    from .service import run_service

    return run_service(args)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="stormbench_sim",
        description="Command line: run simulations, write the report, render the storm animation.",
    )
    parser.add_argument("--network", default="astlingen")
    parser.add_argument("--model-dir", default=os.environ.get("STORMBENCH_MODEL_DIR", "models/astlingen"))
    parser.add_argument("--results-dir", default=os.environ.get("STORMBENCH_RESULTS_DIR", "results"))
    parser.add_argument("--work-dir", default=os.environ.get("STORMBENCH_WORK_DIR", "work"))
    sub = parser.add_subparsers(required=True)

    def plc_options(p: argparse.ArgumentParser, default_host: str | None) -> None:
        p.add_argument("--plc-host", default=os.environ.get("PLC_HOST", default_host))
        p.add_argument("--plc-port", type=int, default=int(os.environ.get("PLC_PORT", "502")))
        p.add_argument("--plc-timeout", type=float, default=10.0)
        p.add_argument("--no-shadow", action="store_true", help="skip the per-step Python cross-check")
        p.add_argument(
            "--center",
            type=center_option,
            default="uns",
            help="efd-center mode: proposals from the central optimiser over the UNS (uns), or played by "
            "this process: EFD (emulated), model predictive control (mpc), or a plugin "
            "package.module:factory (docs/controllers.md)",
        )
        p.add_argument(
            "--center-variant",
            choices=["efd-published", "efd-intended"],
            default="efd-published",
            help="EFD variant the center runs (used for the emulated center and the shadow check)",
        )

    p = sub.add_parser("run", help="one simulation")
    p.add_argument("name")
    p.add_argument("--control", choices=sorted(CONTROLS), required=True)
    p.add_argument("--mode", choices=sorted(MODES), default="local")
    p.add_argument("--start", help="ISO datetime, overrides the model period (with --end)")
    p.add_argument("--end")
    p.add_argument(
        "--outage", nargs=2, metavar=("START", "END"), help="link to the center lost in this window"
    )
    p.add_argument("--pace", type=float, default=0.0, help="wall seconds per control step (demo mode)")
    p.add_argument(
        "--mpc-forecast",
        choices=["perfect", "persistence"],
        default="perfect",
        help="--center mpc: perfect inflow forecast from a pre-run, or only the last measured inflow",
    )
    plc_options(p, "localhost")
    p.set_defaults(func=cmd_run)

    p = sub.add_parser("report", help="validation report and charts from results/runs")
    p.set_defaults(func=cmd_report)

    p = sub.add_parser("animate", help="side-by-side storm animation")
    p.add_argument("--left", default="bc")
    p.add_argument("--right", default="efd-plc")
    p.add_argument("--storm", choices=sorted(STORMS))
    p.add_argument("--start")
    p.add_argument("--hours", type=float, default=24.0)
    p.set_defaults(func=cmd_animate)

    p = sub.add_parser("demo", help="reference runs, PLC run, report and animation")
    plc_options(p, None)
    p.add_argument("--skip-animation", action="store_true")
    p.set_defaults(func=cmd_demo)

    p = sub.add_parser("compare", help="Base Case, EFD and MPC offline on the console storms")
    p.add_argument("--year", action="store_true", help="also the rain year (the MPC takes about 25 minutes)")
    p.add_argument("--storm", action="append", help="only this storm id (repeatable)")
    p.add_argument("--jobs", type=int, default=0, help="worker processes (default: one per CPU)")
    p.add_argument("--report-only", action="store_true", help="only rebuild the table from saved runs")
    p.set_defaults(func=cmd_compare)

    p = sub.add_parser("serve", help="edge node Sim01: field telemetry and the test console on the UNS")
    p.add_argument("--mqtt-host", default=os.environ.get("MQTT_HOST", "localhost"))
    p.add_argument("--mqtt-port", type=int, default=int(os.environ.get("MQTT_PORT", "1883")))
    p.add_argument("--plc-host", default=os.environ.get("PLC_HOST", "localhost"))
    p.add_argument("--plc-port", type=int, default=int(os.environ.get("PLC_PORT", "502")))
    p.add_argument("--plc-timeout", type=float, default=60.0, help="seconds to wait for a PLC answer")
    p.set_defaults(func=cmd_serve)
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
