from __future__ import annotations

import os
import re
import tomllib
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path

from . import backdrop

NETWORKS_DIR = Path(__file__).parent / "networks"
DEFAULT_MODEL_ROOT = Path("models")


@dataclass(frozen=True)
class Station:
    valve: str
    tank: str
    base_setting: float
    low_setting: float


@dataclass(frozen=True)
class Network:
    name: str
    model_dir: Path
    inp: str
    efd_rules: str
    control_step_s: int
    wwtp_outfall: str
    tanks: tuple[str, ...]
    tank_max_depth_m: float
    overflow_points: tuple[str, ...]
    efd_threshold_m: float
    stations: tuple[Station, ...]
    label_offsets: dict[str, tuple[float, float]] = field(default_factory=dict)

    @property
    def inp_path(self) -> Path:
        return self.model_dir / self.inp

    def station_tank_index(self) -> tuple[int, ...]:
        return tuple(self.tanks.index(s.tank) for s in self.stations)


class Controls(Enum):
    """Which control rules stay in the SWMM input file."""

    BASE = "base"
    EFD_RULES = "efd-rules"
    EXTERNAL = "external"


def load_network(name: str = "astlingen", model_dir: Path | None = None) -> Network:
    spec = tomllib.loads((NETWORKS_DIR / f"{name}.toml").read_text(encoding="utf-8"))
    if model_dir is None:
        model_dir = Path(os.environ.get("STORMBENCH_MODEL_DIR", DEFAULT_MODEL_ROOT / name))
    return Network(
        name=spec["name"],
        model_dir=Path(model_dir),
        inp=spec["inp"],
        efd_rules=spec["efd_rules"],
        control_step_s=int(spec["control_step_s"]),
        wwtp_outfall=spec["wwtp_outfall"],
        tanks=tuple(spec["tanks"]),
        tank_max_depth_m=float(spec["tank_max_depth_m"]),
        overflow_points=tuple(spec["overflow_points"]),
        efd_threshold_m=float(spec["efd_threshold_m"]),
        stations=tuple(Station(**s) for s in spec["stations"]),
        label_offsets={
            k: (float(v[0]), float(v[1])) for k, v in spec.get("display", {}).get("label_offsets", {}).items()
        },
    )


_SECTION = r"(?ms)^\[{name}\][^\n]*\n.*?(?=^\[|\Z)"
_MINIMAL_REPORT = "[REPORT]\nSUBCATCHMENTS NONE\nNODES NONE\nLINKS NONE\n\n"


def _replace_section(text: str, name: str, body: str) -> str:
    pattern = _SECTION.format(name=re.escape(name))
    if not re.search(pattern, text):
        raise ValueError(f"section [{name}] not found")
    return re.sub(pattern, lambda _: body, text, count=1)


def _set_option(text: str, key: str, value: str) -> str:
    pattern = rf"(?m)^({re.escape(key)}\s+)\S+"
    if not re.search(pattern, text):
        raise ValueError(f"option {key} not found")
    return re.sub(pattern, lambda m: m.group(1) + value, text, count=1)


@dataclass(frozen=True)
class Layout:
    coords: dict[str, tuple[float, float]]
    links: tuple[tuple[str, str, str], ...]


def _section_rows(text: str, name: str) -> list[list[str]]:
    match = re.search(_SECTION.format(name=re.escape(name)), text)
    if match is None:
        return []
    rows = []
    for line in match.group(0).splitlines()[1:]:
        line = line.split(";", 1)[0].strip()
        if line:
            rows.append(line.split())
    return rows


NODE_KINDS = {"JUNCTIONS": "junction", "STORAGE": "storage", "OUTFALLS": "outfall"}
LINK_KINDS = {
    "CONDUITS": "conduit",
    "ORIFICES": "orifice",
    "WEIRS": "weir",
    "PUMPS": "pump",
    "OUTLETS": "outlet",
}


def read_layout(network: Network) -> Layout:
    """Node coordinates and link topology from the model's map sections."""
    text = network.inp_path.read_text(encoding="latin-1")
    coords = {row[0]: (float(row[1]), float(row[2])) for row in _section_rows(text, "COORDINATES")}
    links = []
    for section in LINK_KINDS:
        links += [(row[0], row[1], row[2]) for row in _section_rows(text, section)]
    return Layout(coords, tuple(links))


def read_map(network: Network) -> dict:
    """What the model holds about the town besides the sewers: subcatchments with their polygons,
    rain gauges; plus the receiving waters, areas and place names traced from the benchmark's
    backdrop picture, if the model has one (backdrop.py)."""
    text = network.inp_path.read_text(encoding="latin-1")
    polygons: dict[str, list[list[float]]] = {}
    for row in _section_rows(text, "Polygons"):
        polygons.setdefault(row[0], []).append([float(row[1]), float(row[2])])
    catchments = [
        {
            "id": row[0],
            "rainGauge": row[1],
            "outlet": row[2],
            "areaHa": float(row[3]),
            "polygon": polygons.get(row[0], []),
        }
        for row in _section_rows(text, "SUBCATCHMENTS")
    ]
    gauges = [
        {"id": row[0], "x": float(row[1]), "y": float(row[2])} for row in _section_rows(text, "SYMBOLS")
    ]
    traced = backdrop.trace(network.inp_path)
    return {
        "catchments": catchments,
        "gauges": gauges,
        "waters": traced.get("waters", []),
        "areas": traced.get("areas", []),
        "places": traced.get("places", []),
        "tints": traced.get("tints", {}),
    }


def read_kinds(network: Network) -> tuple[dict[str, str], dict[str, str]]:
    """Kind of every node and of every link, from the .inp section that declares it."""
    text = network.inp_path.read_text(encoding="latin-1")

    def kinds(sections: dict[str, str]) -> dict[str, str]:
        return {row[0]: kind for section, kind in sections.items() for row in _section_rows(text, section)}

    return kinds(NODE_KINDS), kinds(LINK_KINDS)


def read_period(network: Network) -> tuple[datetime, datetime]:
    """Simulation period of the model as written in its [OPTIONS]."""
    text = network.inp_path.read_text(encoding="latin-1")
    options = {row[0]: row[1] for row in _section_rows(text, "OPTIONS") if len(row) > 1}

    def moment(date_key: str, time_key: str) -> datetime:
        clock = options[time_key]
        pattern = "%m/%d/%Y %H:%M:%S" if clock.count(":") == 2 else "%m/%d/%Y %H:%M"
        return datetime.strptime(f"{options[date_key]} {clock}", pattern)

    return moment("START_DATE", "START_TIME"), moment("END_DATE", "END_TIME")


def rewrite_timeseries_files(text: str, model_dir: Path) -> str:
    """Point every `FILE "..."` time series at the file of the same name in model_dir."""

    def local(match: re.Match[str]) -> str:
        basename = re.split(r"[\\/]", match.group(2))[-1]
        target = (model_dir / basename).resolve()
        if not target.exists():
            raise FileNotFoundError(f"time series file {target} missing; run models/fetch_astlingen.py")
        return f'{match.group(1)}"{target.as_posix()}"'

    section = re.search(_SECTION.format(name="TIMESERIES"), text)
    if section is None:
        return text
    body = re.sub(r'(FILE\s+)"([^"]+)"', local, section.group(0))
    return text[: section.start()] + body + text[section.end() :]


def prepare_inp(
    network: Network,
    work_dir: Path,
    controls: Controls,
    tag: str,
    period: tuple[datetime, datetime] | None = None,
) -> Path:
    """Write a runnable copy of the model: local rain paths, chosen controls, optional period."""
    text = network.inp_path.read_text(encoding="latin-1")
    text = rewrite_timeseries_files(text, network.model_dir)
    if controls is Controls.EFD_RULES:
        rules = (network.model_dir / network.efd_rules).read_text(encoding="latin-1")
        text = _replace_section(text, "CONTROLS", "[CONTROLS]\n" + rules.strip() + "\n\n")
    elif controls is Controls.EXTERNAL:
        text = _replace_section(text, "CONTROLS", "[CONTROLS]\n\n")
    text = _replace_section(text, "REPORT", _MINIMAL_REPORT)
    if period is not None:
        start, end = period
        for key, value in (
            ("START_DATE", start.strftime("%m/%d/%Y")),
            ("START_TIME", start.strftime("%H:%M:%S")),
            ("REPORT_START_DATE", start.strftime("%m/%d/%Y")),
            ("REPORT_START_TIME", start.strftime("%H:%M:%S")),
            ("END_DATE", end.strftime("%m/%d/%Y")),
            ("END_TIME", end.strftime("%H:%M:%S")),
        ):
            text = _set_option(text, key, value)
    work_dir.mkdir(parents=True, exist_ok=True)
    target = work_dir / f"{network.name}_{tag}.inp"
    target.write_text(text, encoding="latin-1")
    return target
