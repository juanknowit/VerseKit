#!/usr/bin/env python3
"""
Generates AppIcon-1024.png for VerseKit.

Design: a dotted wireframe globe (global-data motif) on a blue squircle,
with the wordmark "VERSE" spaced wide across the centre so it reads as the
globe's equator line. Brand blue, white linework, soft top sheen.

Usage:
    python3 scripts/create-icon.py

Requires:
    pip install Pillow

Output:
    src/VerseKit.App/Assets/AppIcon-1024.png

Run generate-icon.sh afterwards to convert that PNG into AppIcon.icns.
"""
import math
import os
import sys

try:
    from PIL import Image, ImageDraw, ImageFilter, ImageFont
except ImportError:
    print("Pillow not found — install it with:  pip install Pillow")
    sys.exit(1)

SIZE = 1024
SS = 2                      # supersample, then downscale once for crisp edges
S = SIZE * SS

RADIUS = int(0.221 * S)     # ~22 % squircle corner (Apple icon shape)

# Brand blue, diagonal: lighter top-left → deeper bottom-right.
BLUE_TL = (77, 168, 255)
BLUE_BR = (10, 87, 208)

# Globe geometry as fractions of the icon side (from the approved mock).
CX = CY = S / 2
R_OUTLINE = 0.323 * S       # sphere radius
MERID_RX = 0.123 * S        # meridian (vertical ellipse) half-width
LAT_RX = 0.262 * S          # latitude (horizontal ellipse) half-width
LAT_RY = 0.069 * S
LAT_DY = 0.146 * S          # latitude offset above/below centre

STROKE = 0.0170 * S         # dotted line thickness
LAT_STROKE = 0.0154 * S
DOT_PERIOD = 0.040 * S      # centre-to-centre spacing of the dots

WHITE = (255, 255, 255)


def rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return mask


def diagonal_gradient(size: int, tl: tuple, br: tuple) -> Image.Image:
    """Smooth TL→BR gradient built from a 2×2 corner image and upscaled."""
    mid = tuple((a + b) // 2 for a, b in zip(tl, br))
    small = Image.new("RGB", (2, 2))
    small.putpixel((0, 0), tl)
    small.putpixel((1, 0), mid)
    small.putpixel((0, 1), mid)
    small.putpixel((1, 1), br)
    return small.resize((size, size), Image.BICUBIC).convert("RGBA")


def sphere_sheen(size: int) -> Image.Image:
    """A soft off-centre highlight giving the globe a lit, spherical feel,
    plus a faint top sheen on the squircle."""
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    # Globe highlight — radial alpha computed on a small grid, then scaled.
    g = 160
    rad = Image.new("L", (g, g), 0)
    px = rad.load()
    hx, hy = 0.40 * g, 0.34 * g           # light source toward top-left
    maxd = math.hypot(g, g)
    for y in range(g):
        for x in range(g):
            d = math.hypot(x - hx, y - hy) / maxd
            a = max(0.0, 0.34 - d * 0.62)  # ~0.34 at the highlight → 0 at rim
            px[x, y] = int(a * 255)
    d = int(R_OUTLINE * 2)
    rad = rad.resize((d, d), Image.BICUBIC)
    circle_mask = Image.new("L", (d, d), 0)
    ImageDraw.Draw(circle_mask).ellipse([0, 0, d - 1, d - 1], fill=255)
    white = Image.new("RGBA", (d, d), (255, 255, 255, 255))
    box = (int(CX - R_OUTLINE), int(CY - R_OUTLINE))
    sphere = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    # combine: alpha = radial * circle confinement
    from PIL import ImageChops
    alpha = ImageChops.multiply(rad, circle_mask)
    sphere.paste(white, box, alpha)
    layer.alpha_composite(sphere)

    # Top sheen across the whole squircle.
    sheen = Image.new("L", (g, g), 0)
    sp = sheen.load()
    sx, sy = 0.5 * g, -0.05 * g
    for y in range(g):
        for x in range(g):
            dd = math.hypot(x - sx, y - sy) / (0.9 * g)
            sp[x, y] = int(max(0.0, 0.20 - dd * 0.30) * 255)
    sheen = sheen.resize((size, size), Image.BICUBIC)
    top = Image.new("RGBA", (size, size), (255, 255, 255, 255))
    top.putalpha(sheen)
    layer.alpha_composite(top)
    return layer


def dotted_ellipse(draw: ImageDraw.ImageDraw, cx, cy, rx, ry, dot_r, alpha, period):
    """Walk the ellipse by arc length, placing round dots `period` apart."""
    n = 3000
    prev = None
    acc = period          # so the first point gets a dot
    for i in range(n + 1):
        t = 2 * math.pi * i / n
        x = cx + rx * math.cos(t)
        y = cy + ry * math.sin(t)
        if prev is not None:
            acc += math.hypot(x - prev[0], y - prev[1])
            if acc >= period:
                draw.ellipse([x - dot_r, y - dot_r, x + dot_r, y + dot_r],
                             fill=(255, 255, 255, alpha))
                acc = 0.0
        prev = (x, y)


def find_bold_font(size: int) -> ImageFont.FreeTypeFont:
    candidates = [
        "/System/Library/Fonts/HelveticaNeue.ttc",
        "/System/Library/Fonts/Helvetica.ttc",
        "/System/Library/Fonts/SFNS.ttf",
        "/Library/Fonts/Arial Bold.ttf",
    ]
    for path in candidates:
        if not os.path.exists(path):
            continue
        for index in range(0, 18):
            try:
                f = ImageFont.truetype(path, size, index=index)
                _, style = f.getname()
                if "bold" in style.lower() and "italic" not in style.lower():
                    return f
            except Exception:
                break
    for path in candidates:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except Exception:
                continue
    return ImageFont.load_default()


def draw_spaced_text(draw: ImageDraw.ImageDraw, text, font, cx, cy, tracking, fill):
    advances = [draw.textlength(ch, font=font) for ch in text]
    total = sum(advances) + tracking * (len(text) - 1)
    x = cx - total / 2
    for ch, adv in zip(text, advances):
        draw.text((x, cy), ch, font=font, fill=fill, anchor="lm")
        x += adv + tracking


def make_icon(out_path: str) -> None:
    # 1. Blue squircle.
    base = diagonal_gradient(S, BLUE_TL, BLUE_BR)
    base.putalpha(rounded_mask(S, RADIUS))

    # 2. Spherical sheen.
    base.alpha_composite(sphere_sheen(S))

    # 3. Dotted globe + VERSE equator on one ink layer.
    ink = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(ink)

    # Outline (sphere edge)
    dotted_ellipse(d, CX, CY, R_OUTLINE, R_OUTLINE, STROKE / 2, 175, DOT_PERIOD)
    # Meridian (no equator — the wordmark takes its place)
    dotted_ellipse(d, CX, CY, MERID_RX, R_OUTLINE, STROKE / 2, 140, DOT_PERIOD)
    # Upper / lower latitudes
    dotted_ellipse(d, CX, CY - LAT_DY, LAT_RX, LAT_RY, LAT_STROKE / 2, 105, DOT_PERIOD)
    dotted_ellipse(d, CX, CY + LAT_DY, LAT_RX, LAT_RY, LAT_STROKE / 2, 105, DOT_PERIOD)

    # VERSE across the centre, wide-tracked so it reads as the equator.
    font = find_bold_font(int(0.138 * S))
    draw_spaced_text(d, "VERSE", font, CX, CY, tracking=0.035 * S, fill=(255, 255, 255, 255))

    base.alpha_composite(ink)

    # 4. Downscale once for clean anti-aliasing.
    base.resize((SIZE, SIZE), Image.LANCZOS).save(out_path, "PNG")
    print(f"Icon saved → {out_path}")


if __name__ == "__main__":
    script_dir = os.path.dirname(os.path.abspath(__file__))
    out = os.path.normpath(
        os.path.join(script_dir, "../src/VerseKit.App/Assets/AppIcon-1024.png")
    )
    make_icon(out)
