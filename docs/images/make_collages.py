"""Lays the README's screenshots out as collages in the StormBench style (rounded cards with soft shadows on
the pale blue ground) and cuts the Live 3D clip from a 60 fps recording. Run from the repository root:
python docs/images/make_collages.py [frames-dir]"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
IMAGES = ROOT / "docs" / "images"
SHOTS = IMAGES / "banner"
GROUND = {"light": ((255, 255, 255), (233, 241, 249)), "dark": ((20, 27, 39), (15, 20, 29))}
EDGE = {"light": (60, 85, 120, 30), "dark": (190, 210, 235, 46)}
RADIUS = 26


def ground(w, h, theme):
    top, bottom = GROUND[theme]
    im = Image.new("RGB", (w, h))
    px = ImageDraw.Draw(im)
    for y in range(h):
        t = y / max(1, h - 1)
        px.line([(0, y), (w, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    return im


def card(canvas, shot, box, theme, crop=None):
    """Pastes a screenshot into `box` (x, y, w, h) as a rounded card with a soft shadow."""
    x, y, w, h = box
    im = Image.open(shot).convert("RGB")
    if crop:
        cx0, cy0, cx1, cy1 = crop
        im = im.crop((int(cx0 * im.width), int(cy0 * im.height), int(cx1 * im.width), int(cy1 * im.height)))
    scale = max(w / im.width, h / im.height)
    im = im.resize((round(im.width * scale), round(im.height * scale)), Image.LANCZOS)
    left, top = (im.width - w) // 2, (im.height - h) // 2
    im = im.crop((left, top, left + w, top + h))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w - 1, h - 1), RADIUS, fill=255)
    shadow = Image.new("L", canvas.size, 0)
    ImageDraw.Draw(shadow).rounded_rectangle((x, y + 18, x + w, y + h + 18), RADIUS, fill=110 if theme == "dark" else 60)
    shadow = shadow.filter(ImageFilter.GaussianBlur(26))
    canvas.paste(Image.new("RGB", canvas.size, (28, 43, 64)), (0, 0), shadow)
    canvas.paste(im, (x, y), mask)
    edge = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(edge).rounded_rectangle((0, 0, w - 1, h - 1), RADIUS, outline=EDGE[theme], width=2)
    canvas.paste(edge, (x, y), edge)


def save(canvas, name):
    canvas.save(IMAGES / name, optimize=True)
    print(name, canvas.size)


def live_collage():
    c = ground(2400, 964, "light")
    card(c, SHOTS / "live-morning.jpg", (60, 60, 1500, 844), "light")
    card(c, SHOTS / "live-closeup.jpg", (1620, 60, 720, 402), "light", (0.1, 0.18, 0.9, 0.95))
    card(c, SHOTS / "live-night.jpg", (1620, 502, 720, 402), "light")
    save(c, "live-3d.png")


def scada_collage():
    c = ground(2400, 1424, "light")
    card(c, SHOTS / "scada-light.jpg", (60, 60, 1110, 1304), "light")
    card(c, SHOTS / "scada-dark.jpg", (1230, 60, 1110, 1304), "dark")
    save(c, "hmi-overview.png")


def clip(frames):
    """Four seconds of a 60 fps recording (PNG frames), 960 px wide, as an animated WebP."""
    files = sorted(Path(frames).glob("*.png"))[60:300]
    pictures = [Image.open(f).convert("RGB").resize((960, 540), Image.LANCZOS) for f in files]
    pictures[0].save(IMAGES / "live-3d.webp", save_all=True, append_images=pictures[1:], duration=17, loop=0, quality=72, method=6)
    print("live-3d.webp", len(pictures), "frames")


live_collage()
scada_collage()
if len(sys.argv) > 1:
    clip(sys.argv[1])
