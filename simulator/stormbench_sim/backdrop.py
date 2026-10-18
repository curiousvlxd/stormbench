from __future__ import annotations

import re
from functools import cache
from pathlib import Path

import numpy as np
from PIL import Image

BACKDROP = "astingen.jpg"

LABELS = {
    "Main River": [(139, 393), (1060, 330)],
    "Park Creek": [(633, 97)],
    "Park": [(565, 393)],
    "Town Centre": [(565, 462)],
}
PARK_WINDOW = (slice(322, 460), slice(476, 616))


def dimensions(inp_text: str) -> tuple[float, float, float, float]:
    section = inp_text[inp_text.index("[BACKDROP]") :]
    match = re.search(r"DIMENSIONS\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)", section)
    if match is None:
        raise ValueError("no backdrop dimensions in the model")
    x0, y0, x1, y1 = map(float, match.groups())
    return x0, y0, x1, y1


def walk(mask: np.ndarray, start: tuple[int, int], step: float = 7.0) -> list[tuple[float, float]]:
    """Walk along a line mask from `start`: each step goes to the centroid of the mask pixels in a
    ring ahead of the current point, away from where the walk came from."""
    ys, xs = np.nonzero(mask)
    pts = np.stack([xs, ys], axis=1).astype(float)
    current = np.array(start, dtype=float)
    heading = None
    path = [(float(current[0]), float(current[1]))]
    for _ in range(2000):
        d = np.linalg.norm(pts - current, axis=1)
        ring = pts[(d > step * 0.6) & (d < step * 1.4)]
        if heading is not None and len(ring):
            ring = ring[(ring - current) @ heading > 0]
        if not len(ring):
            break
        nxt = ring.mean(axis=0)
        heading = (nxt - current) / np.linalg.norm(nxt - current)
        current = nxt
        path.append((float(current[0]), float(current[1])))
    return path


def simplify(points: list[tuple[float, float]], tolerance: float) -> list[tuple[float, float]]:
    """Ramer-Douglas-Peucker."""
    if len(points) < 3:
        return points
    a, b = points[0], points[-1]
    ab = (b[0] - a[0], b[1] - a[1])
    norm = float(np.hypot(*ab)) or 1.0
    distances = [abs(ab[0] * (p[1] - a[1]) - ab[1] * (p[0] - a[0])) / norm for p in points[1:-1]]
    i = int(np.argmax(distances)) + 1
    if distances[i - 1] <= tolerance:
        return [a, b]
    return simplify(points[: i + 1], tolerance)[:-1] + simplify(points[i:], tolerance)


def convex_hull(points: list[tuple[int, int]]) -> list[tuple[int, int]]:
    pts = sorted(set(points))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower: list[tuple[int, int]] = []
    upper: list[tuple[int, int]] = []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


@cache
def trace(inp_path: Path) -> dict:
    """waters, areas, places and catchment tints in map units; empty when the model has no backdrop."""
    image_path = inp_path.with_name(BACKDROP)
    if not image_path.exists():
        return {}
    text = inp_path.read_text(encoding="latin-1")
    x0, y0, x1, y1 = dimensions(text)
    im = np.asarray(Image.open(image_path).convert("RGB")).astype(int)
    h, w, _ = im.shape
    r, g, b = im[..., 0], im[..., 1], im[..., 2]

    def to_map(u: float, v: float) -> list[float]:
        return [round(x0 + (u + 0.5) / w * (x1 - x0), 1), round(y1 - (v + 0.5) / h * (y1 - y0), 1)]

    river = (b - r > 60) & (b > 150) & (r < 140)
    creek = (g - b > 45) & (g - r > 20)
    for name, spots in LABELS.items():
        mask = {"Main River": river, "Park Creek": creek}.get(name)
        if mask is None:
            continue
        half = int(len(name) * 4.0)
        for u, v in spots:
            mask[max(0, v - 9) : v + 9, max(0, u - half) : u + half] = False

    def line(name: str, mask: np.ndarray) -> dict:
        ys, xs = np.nonzero(mask)
        i = int(np.argmin(xs)) if name == "Main River" else int(np.argmin(ys))
        path = simplify(walk(mask, (int(xs[i]), int(ys[i]))), 1.2)
        return {"name": name, "line": [to_map(u, v) for u, v in path]}

    pale = (abs(r - 226) < 12) & (abs(g - 240) < 12) & (abs(b - 217) < 12)
    window = np.zeros_like(pale)
    window[PARK_WINDOW] = True
    ys, xs = np.nonzero(pale & window)
    hull = simplify(
        [(float(x), float(y)) for x, y in convex_hull(list(zip(xs.tolist(), ys.tolist(), strict=True)))], 1.5
    )

    tints = {}
    body = text[text.index("[Polygons]") :]
    body = body[: body.index("\n[", 1)]
    polygons: dict[str, list[tuple[float, float]]] = {}
    for row in body.splitlines()[1:]:
        parts = row.split(";", 1)[0].split()
        if len(parts) == 3:
            polygons.setdefault(parts[0], []).append((float(parts[1]), float(parts[2])))
    for name, poly in polygons.items():
        cx = sum(p[0] for p in poly) / len(poly)
        cy = sum(p[1] for p in poly) / len(poly)
        u, v = int((cx - x0) / (x1 - x0) * w), int((y1 - cy) / (y1 - y0) * h)
        patch = im[max(0, v - 4) : v + 5, max(0, u - 4) : u + 5].reshape(-1, 3)
        rr, gg, bb = (int(c) for c in np.median(patch, axis=0))
        tints[name] = f"#{rr:02x}{gg:02x}{bb:02x}"

    return {
        "waters": [line("Main River", river), line("Park Creek", creek)],
        "areas": [{"name": "Park", "polygon": [to_map(u, v) for u, v in hull]}],
        "places": [
            {"name": name, "x": to_map(u, v)[0], "y": to_map(u, v)[1]}
            for name, spots in LABELS.items()
            for u, v in spots
        ],
        "tints": tints,
    }
