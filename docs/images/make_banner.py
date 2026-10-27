"""Draws the README banner (docs/images/banner-light.svg, banner-dark.svg): the StormBench mark with
its water sloshing (SVG animation), the name and the line under it as outlines of Source Sans 3, and
three screenshots in layers that drift at their own depth, the Live 3D town in the morning between the operator SCADA and a tank
under the x-ray. Run from the repository root with the screenshots in docs/images/banner/:
python docs/images/make_banner.py"""

import base64
import io
import math
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "docs" / "images"
SHOTS = OUT / "banner"
W, H = 1280, 700
LEFT, LEVEL, RIGHT = 8.2, 17.2, 23.8


def font(weight):
    return instantiateVariableFont(TTFont(ROOT / "fonts" / "SourceSans3-Variable.ttf"), {"wght": weight})


def text_path(f, text, size, tracking=0.0):
    """The outline of `text` with its baseline at 0, from x 0; returns the path and its width."""
    glyphs = f.getGlyphSet()
    cmap = f.getBestCmap()
    scale = size / f["head"].unitsPerEm
    pen = SVGPathPen(glyphs)
    cursor = 0.0
    for ch in text:
        name = cmap[ord(ch)]
        glyphs[name].draw(TransformPen(pen, (scale, 0, 0, -scale, cursor, 0)))
        cursor += glyphs[name].width * scale + tracking * size
    return pen.getCommands(), cursor - tracking * size


def water(slope, phase):
    mid = (LEFT + RIGHT) / 2
    pts = []
    for i in range(33):
        x = LEFT + (RIGHT - LEFT) * i / 32
        y = LEVEL + slope * (x - mid) + 0.85 * math.sin((x - LEFT) / 12 * 2 * math.pi + phase)
        pts.append(f"{x:.2f} {y:.2f}")
    return "M" + "L".join(pts) + "V22.6A3.2 3.2 0 0 1 20.6 25.8H11.4A3.2 3.2 0 0 1 8.2 22.6Z"


def logo(accent, inside):
    """The mark, 32 x 32 units, its water sloshing on a 4 s loop that starts and ends level."""
    frames = 40
    values, times = [], []
    for k in range(frames + 1):
        t = k / frames
        slope = 0.3 * math.sin(2 * math.pi * t) * (0.65 + 0.35 * math.cos(2 * math.pi * t))
        values.append(water(slope, -2 * math.pi * t + math.pi / 2))
        times.append(f"{t:.4f}")
    return f"""<rect x="12.5" y="1.8" width="7" height="4.4" rx="1.2" fill="{accent}"/>
    <rect x="4.5" y="6" width="23" height="23.5" rx="6" fill="{inside}" stroke="{accent}" stroke-width="3"/>
    <clipPath id="inside"><rect x="6" y="7.5" width="20" height="20.5" rx="4.6"/></clipPath>
    <path fill="{accent}" clip-path="url(#inside)" d="{values[0]}">
      <animate attributeName="d" dur="4s" repeatCount="indefinite" keyTimes="{';'.join(times)}" values="{';'.join(values)}"/>
    </path>"""


def picture(name, box, w, h):
    """A screenshot cropped to `box` (fractions), then to the card's proportions (from the top), at twice its size, as a JPEG data URI."""
    im = Image.open(SHOTS / name).convert("RGB")
    x0, y0, x1, y1 = box
    im = im.crop((int(x0 * im.width), int(y0 * im.height), int(x1 * im.width), int(y1 * im.height)))
    want = w / h
    if im.width / im.height > want:
        cut = round(im.height * want)
        im = im.crop(((im.width - cut) // 2, 0, (im.width - cut) // 2 + cut, im.height))
    else:
        im = im.crop((0, 0, im.width, round(im.width / want)))
    im = im.resize((w * 2, h * 2), Image.LANCZOS)
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=84, optimize=True, progressive=True)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def card(key, uri, x, y, w, h, drift, period, edge):
    """A card at (x, y) that drifts by `drift` px and back over `period` seconds: nearer cards drift further (parallax)."""
    dx, dy = drift
    return f"""<g filter="url(#shadow)">
    <animateTransform attributeName="transform" type="translate" dur="{period}s" repeatCount="indefinite" calcMode="spline"
      keyTimes="0;0.5;1" keySplines="0.45 0 0.55 1;0.45 0 0.55 1" values="0 0;{dx} {dy};0 0"/>
    <clipPath id="clip-{key}"><rect x="{x}" y="{y}" width="{w}" height="{h}" rx="16"/></clipPath>
    <image href="{uri}" x="{x}" y="{y}" width="{w}" height="{h}" preserveAspectRatio="xMidYMid slice" clip-path="url(#clip-{key})"/>
    <rect x="{x}" y="{y}" width="{w}" height="{h}" rx="16" fill="none" stroke="{edge}" stroke-width="1.5"/>
  </g>"""


def banner(theme):
    dark = theme == "dark"
    bg_top, bg_bottom = ("#141b27", "#0f141d") if dark else ("#ffffff", "#e9f1f9")
    ink, ink2 = ("#e8eef6", "#b2bfcf") if dark else ("#2a3446", "#5b687c")
    accent = "#7cb9ee" if dark else "#4a90d9"
    inside = "#1b2230" if dark else "#ffffff"
    edge = "rgba(190,210,235,0.18)" if dark else "rgba(60,85,120,0.12)"
    shadow, shadow_color = ("0.5", "#000000") if dark else ("0.14", "#2a3446")
    name, name_w = text_path(font(800), "StormBench", 92, -0.015)
    line, line_w = text_path(font(500), "A flight simulator for sewer control code", 30)
    mark, gap = 112, 28
    block = mark + gap + max(name_w, line_w)
    x0 = (W - block) / 2
    tx = x0 + mark + gap
    scada = picture(f"scada-{theme}.jpg", (0.0, 0.0, 0.74, 0.62), 480, 300)
    tank = picture("live-closeup.jpg", (0.12, 0.16, 0.92, 0.98), 480, 300)
    town = picture("live-morning.jpg", (0.0, 0.0, 1.0, 1.0), 640, 360)
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-label="StormBench: a flight simulator for sewer control code. The Live 3D town between the operator SCADA and a tank under the x-ray">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0.3" y2="1">
      <stop offset="0" stop-color="{bg_top}"/><stop offset="1" stop-color="{bg_bottom}"/>
    </linearGradient>
    <radialGradient id="glow" cx="0.5" cy="0.62" r="0.62">
      <stop offset="0" stop-color="{accent}" stop-opacity="{0.22 if dark else 0.2}"/><stop offset="1" stop-color="{accent}" stop-opacity="0"/>
    </radialGradient>
    <filter id="shadow" x="-20%" y="-20%" width="140%" height="150%">
      <feDropShadow dx="0" dy="10" stdDeviation="14" flood-color="{shadow_color}" flood-opacity="{shadow}"/>
    </filter>
  </defs>
  <rect width="{W}" height="{H}" rx="28" fill="url(#bg)"/>
  <rect width="{W}" height="{H}" rx="28" fill="url(#glow)"/>
  <g transform="translate({x0:.1f} 40) scale({mark / 32})">
    {logo(accent, inside)}
  </g>
  <path fill="{ink}" transform="translate({tx:.1f} 116)" d="{name}"/>
  <path fill="{ink2}" transform="translate({tx + 3:.1f} 158)" d="{line}"/>
  {card("scada", scada, 64, 226, 480, 300, (6, -3), 9, edge)}
  {card("tank", tank, 736, 226, 480, 300, (-6, -3), 9, edge)}
  {card("town", town, 320, 296, 640, 360, (0, -9), 9, edge)}
</svg>
"""


for theme in ("light", "dark"):
    (OUT / f"banner-{theme}.svg").write_text(banner(theme), encoding="utf-8", newline="\n")
print("written")
