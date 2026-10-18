from __future__ import annotations

import argparse
import json
import time
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from .spills import count_12_24, discharging_steps

STORM = "2000-05-30"
INJECT_STEP = 250
PACE_S = 0.05


@dataclass(frozen=True)
class Fault:
    key: str
    label: str
    endpoint: str | None
    body: dict | None
    clear: dict | None
    alarm: str | None


FAULTS = (
    Fault("healthy", "Healthy", None, None, None, None),
    Fault(
        "link",
        "Link to the center cut",
        "/api/faults/center-link",
        {"cut": True},
        {"cut": False},
        "PLC01.Fallback",
    ),
    Fault(
        "gate",
        "Gate V2 stuck",
        "/api/faults/stuck-gate",
        {"target": "V2"},
        {"target": None},
        "V2.GateDeviation",
    ),
    Fault(
        "sensor",
        "Level sensor T4 frozen",
        "/api/faults/frozen-sensor",
        {"target": "T4"},
        {"target": None},
        "T4.LevelFrozen",
    ),
)


class Stack:
    def __init__(self, scada: str, vm: str):
        self.scada, self.vm = scada.rstrip("/"), vm.rstrip("/")

    def get(self, path: str):
        with urllib.request.urlopen(self.scada + path, timeout=30) as r:
            return json.load(r)

    def post(self, path: str, body: dict) -> int:
        request = urllib.request.Request(
            self.scada + path,
            data=json.dumps(body).encode(),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(request, timeout=30) as r:
            return r.status

    def series(self, metric: str, run: str) -> dict[str, tuple[list[float], list[float]]]:
        """point -> (model time in s, values) of one run, from VictoriaMetrics."""
        query = urllib.parse.urlencode({"match[]": f'{metric}{{run="{run}"}}'})
        with urllib.request.urlopen(f"{self.vm}/api/v1/export?{query}", timeout=30) as r:
            out = {}
            for line in r.read().decode().splitlines():
                row = json.loads(line)
                out[row["metric"].get("point", "")] = ([t / 1000 for t in row["timestamps"]], row["values"])
            return out


def clear_faults(stack: Stack) -> None:
    for fault in FAULTS:
        if fault.endpoint:
            stack.post(fault.endpoint, fault.clear)


def wait_idle(stack: Stack) -> dict:
    while True:
        status = stack.get("/api/scenarios")
        if status["state"] not in ("running", "preparing"):
            return status
        time.sleep(0.5)


def finished_run(stack: Stack, sim_run: int, timeout_s: float = 20.0) -> dict:
    """The run's summary once Scada's run store has written its final figures (it writes in the background)."""
    deadline = time.monotonic() + timeout_s
    while True:
        run = next((r for r in stack.get("/api/runs") if r["simRun"] == sim_run), None)
        if (
            run
            and run["state"] != "running"
            and run.get("overflow") is not None
            and run["steps"] == run["stepsPlanned"]
        ):
            return run
        if time.monotonic() > deadline:
            raise RuntimeError(f"run {sim_run} has no final summary: {run}")
        time.sleep(0.5)


def run_one(stack: Stack, algorithm: str, fault: Fault) -> dict:
    wait_idle(stack)
    clear_faults(stack)
    before = stack.get("/api/scenarios")["run"]
    stack.post("/api/scenarios/start", {"storm": STORM, "algorithm": algorithm, "paceSeconds": PACE_S})
    injected_at = None
    while True:
        status = stack.get("/api/scenarios")
        if status["run"] != before and status["state"] in ("finished", "failed", "stopped"):
            break
        if (
            fault.endpoint
            and injected_at is None
            and status["run"] != before
            and status["state"] == "running"
            and status["step"] >= INJECT_STEP
        ):
            stack.post(fault.endpoint, fault.body)
            injected_at = status["clock"]
        time.sleep(0.05)
    clear_faults(stack)
    run = finished_run(stack, status["run"])
    journal = stack.get(f"/api/alarms/journal?run={urllib.parse.quote(run['id'])}")
    detected = None
    if fault.alarm and injected_at:
        raised = [
            e["simTime"]
            for e in journal
            if e["alarmId"] == fault.alarm
            and e["to"] == "Unacknowledged"
            and e["simTime"] >= injected_at[:19]
        ]
        if raised:
            minutes = (
                datetime.fromisoformat(min(raised)) - datetime.fromisoformat(injected_at[:19])
            ).total_seconds() / 60
            detected = round(minutes)
    volumes = stack.series("stormbench_overflow_volume_cubic_meters", run["id"])
    spills = sum(count_12_24(discharging_steps(t, v)) for t, v in volumes.values())
    return {
        "algorithm": algorithm,
        "fault": fault.key,
        "label": fault.label,
        "run": run["id"],
        "overflow_m3": run["overflow"],
        "base_case_m3": run["baseCase"],
        "change_pct": run["changePercent"],
        "spills_12_24": spills,
        "mismatches": run["mismatches"],
        "injected_at": injected_at,
        "detected_after_min": detected,
        "alarm": fault.alarm,
    }


def base_spills(stack: Stack) -> dict:
    """The Base Case run of the storm, for its spill count."""
    return run_one(stack, "base", FAULTS[0])


def markdown(rows: list[dict], base: dict) -> str:
    healthy = {r["algorithm"]: r["overflow_m3"] for r in rows if r["fault"] == "healthy"}
    lines = [
        f"# Fault matrix, storm of {STORM}",
        "",
        f"Each fault is injected at control step {INJECT_STEP} (20:50, before the downpour) and cleared at the end. "
        "Measured on the full stack (PLC, edge gateway, UNS, Scada); spills counted with the 12/24 method of the "
        "English regulators over the ten overflow points. Generated by `python -m stormbench_sim.faultmatrix`.",
        "",
        f"Base Case (no control): {base['overflow_m3']:,.0f} m³, {base['spills_12_24']} spills.",
        "",
        "| Control | Fault | Overflow | vs Base Case | Cost of the fault | Spills (12/24) | Scada alarm | Found after | PLC mismatches |",
        "|---|---|---:|---:|---:|---:|---|---:|---:|",
    ]
    for r in rows:
        cost = r["overflow_m3"] - healthy[r["algorithm"]]
        found = (
            "n/a"
            if r["fault"] == "healthy"
            else ("not raised" if r["detected_after_min"] is None else f"{r['detected_after_min']} min")
        )
        lines.append(
            f"| {r['algorithm']} | {r['label']} | {r['overflow_m3']:,.0f} m³ | {r['change_pct']:+.1f}% | "
            f"{'' if r['fault'] == 'healthy' else f'{cost:+,.0f} m³'} | {r['spills_12_24']} | {r['alarm'] or ''} | {found} | {r['mismatches']} |"
        )
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Fault matrix: what each field fault costs and whether Scada notices it, measured on the live stack."
    )
    parser.add_argument("--scada", default="http://localhost:5113")
    parser.add_argument("--vm", default="http://localhost:28428")
    parser.add_argument("--algorithms", nargs="+", default=["efd-center", "mpc-center"])
    parser.add_argument(
        "--out", type=Path, default=Path(__file__).resolve().parents[2] / "results" / "faults"
    )
    args = parser.parse_args()
    stack = Stack(args.scada, args.vm)
    base = base_spills(stack)
    print(f"base: {base['overflow_m3']:.0f} m3, {base['spills_12_24']} spills")
    rows = []
    for algorithm in args.algorithms:
        for fault in FAULTS:
            row = run_one(stack, algorithm, fault)
            rows.append(row)
            print(
                f"{algorithm:12} {fault.key:8} {row['overflow_m3']:9.0f} m3 {row['change_pct']:+6.1f}% spills {row['spills_12_24']:3} found {row['detected_after_min']} mism {row['mismatches']}"
            )
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "faults.json").write_text(
        json.dumps({"base": base, "rows": rows}, indent=2), encoding="utf-8", newline="\n"
    )
    (args.out / "faults.md").write_text(markdown(rows, base), encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
