from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import matplotlib as mpl
from matplotlib import font_manager

FONT_FAMILY = "Atkinson Hyperlegible Next"


@dataclass(frozen=True)
class Theme:
    name: str
    surface: str
    page: str
    ink: str
    ink_secondary: str
    ink_muted: str
    grid: str
    axis: str
    efd: str
    base: str


LIGHT = Theme(
    "light", "#fcfcfb", "#f9f9f7", "#0b0b0b", "#52514e", "#898781", "#e1e0d9", "#c3c2b7", "#2a78d6", "#eb6834"
)
DARK = Theme(
    "dark", "#1a1a19", "#0d0d0d", "#ffffff", "#c3c2b7", "#898781", "#2c2c2a", "#383835", "#3987e5", "#d95926"
)

HMI = {
    "background": "#e6e6e3",
    "panel": "#efefec",
    "pipe": "#a3a29c",
    "structure": "#6e6d68",
    "tank_empty": "#fbfbfa",
    "water": "#2a78d6",
    "water_light": "#9ec5f4",
    "alarm": "#d03b3b",
    "ink": "#1f1f1d",
    "ink_secondary": "#52514e",
    "ink_muted": "#7a7973",
}


def find_font_dir(start: Path | None = None) -> Path | None:
    here = (start or Path.cwd()).resolve()
    for candidate in [here, *here.parents]:
        fonts = candidate / "fonts"
        if any(fonts.glob("AtkinsonHyperlegibleNext-*.ttf")):
            return fonts
    return None


def use_fonts(font_dir: Path | None = None) -> str:
    """Register the bundled typeface; fall back to the default sans if it is missing."""
    font_dir = font_dir or find_font_dir()
    if font_dir is not None:
        for ttf in sorted(font_dir.glob("AtkinsonHyperlegibleNext-*.ttf")):
            font_manager.fontManager.addfont(str(ttf))
        mpl.rcParams["font.family"] = FONT_FAMILY
        return FONT_FAMILY
    mpl.rcParams["font.family"] = "sans-serif"
    return "sans-serif"
