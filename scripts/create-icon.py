#!/usr/bin/env python3
"""
Generates AppIcon-1024.png for VerseKit.

Design: App Store-style lettermark — bold white "VK" with a faint
top-to-bottom gradient on the glyph, on a blue gradient squircle.

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
RADIUS = 226            # ~22 % of side (Apple icon shape)
SS = 4                  # supersampling factor for crisp edges

BLUE_TOP = (64, 156, 255)
BLUE_BOT = (0, 91, 216)
WHITE = (255, 255, 255, 255)


def rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return mask


def vertical_gradient(size: int, top: tuple, bot: tuple) -> Image.Image:
    img = Image.new("RGBA", (size, size))
    d = ImageDraw.Draw(img)
    for y in range(size):
        t = y / size
        r = int(top[0] + (bot[0] - top[0]) * t)
        g = int(top[1] + (bot[1] - top[1]) * t)
        b = int(top[2] + (bot[2] - top[2]) * t)
        d.line([(0, y), (size, y)], fill=(r, g, b, 255))
    return img


def find_bold_font(size: int) -> ImageFont.FreeTypeFont:
    """Searches system font collections for a bold sans face."""
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
                family, style = f.getname()
                if "bold" in style.lower() and "italic" not in style.lower():
                    return f
            except Exception:
                break
    # Fall back to whatever loads
    for path in candidates:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except Exception:
                continue
    return ImageFont.load_default()


def make_lettermark(canvas: int) -> Image.Image:
    """Draws 'VK' centred, filled with a faint white gradient
    (brighter at the top, slightly dimmer at the bottom) — the same
    treatment Apple uses on the App Store glyph."""
    s = canvas * SS
    layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    font = find_bold_font(int(s * 0.38))
    tmp = ImageDraw.Draw(layer)
    mark = "VK"
    bb = tmp.textbbox((0, 0), mark, font=font)
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    x = (s - tw) // 2 - bb[0]
    y = (s - th) // 2 - bb[1]

    # Text mask
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).text((x, y), mark, fill=255, font=font)

    # Faint vertical gradient fill: pure white → slightly translucent
    fill = Image.new("RGBA", (s, s))
    fd = ImageDraw.Draw(fill)
    top_a, bot_a = 255, 214
    g_top = y - int(s * 0.02)
    g_bot = y + th + int(s * 0.02)
    for yy in range(s):
        t = min(max((yy - g_top) / max(g_bot - g_top, 1), 0.0), 1.0)
        a = int(top_a + (bot_a - top_a) * t)
        fd.line([(0, yy), (s, yy)], fill=(255, 255, 255, a))

    layer = Image.composite(fill, layer, mask)
    return layer.resize((canvas, canvas), Image.LANCZOS)


def make_icon(out_path: str) -> None:
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    # Gradient background clipped to the squircle
    bg = vertical_gradient(SIZE, BLUE_TOP, BLUE_BOT)
    bg.putalpha(rounded_mask(SIZE, RADIUS))
    img.alpha_composite(bg)

    # Soft drop shadow under the glyph for a little depth
    glyph = make_lettermark(SIZE)
    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    shadow.paste((0, 30, 80, 110), (0, 14), glyph.split()[3])
    shadow = shadow.filter(ImageFilter.GaussianBlur(18))
    img.alpha_composite(shadow)
    img.alpha_composite(glyph)

    img.save(out_path, "PNG")
    print(f"Icon saved → {out_path}")


if __name__ == "__main__":
    script_dir = os.path.dirname(os.path.abspath(__file__))
    out = os.path.normpath(
        os.path.join(script_dir, "../src/VerseKit.App/Assets/AppIcon-1024.png")
    )
    make_icon(out)
