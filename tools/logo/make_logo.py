"""Render the WinLaunch Enhanced logo at 4x and downsample (Pillow only).

Writes assets PNGs (16..1024) and a multi-size .ico into the app's Assets folder
and a 512 px copy into docs/ for the README.
"""
import math, os, sys
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ASSETS = os.path.join(ROOT, "src", "WinLaunch.Enhanced", "Assets")
DOCS = os.path.join(ROOT, "docs")
S = 4096  # supersampled canvas


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def squircle_mask(size, inset, radius):
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle((inset, inset, size - inset, size - inset), radius, fill=255)
    return m


def render():
    blue, mauve, deep = (137, 180, 250), (203, 166, 247), (116, 199, 236)
    # Diagonal gradient body.
    grad = Image.new("RGBA", (S, S))
    px = grad.load()
    for y in range(0, S, 4):
        for x in range(0, S, 4):
            t = (x + y) / (2 * S)
            c = lerp(deep, blue, min(1, t * 1.6)) if t < 0.5 else lerp(blue, mauve, (t - 0.5) * 2)
            for dy in range(4):
                for dx in range(4):
                    px[x + dx, y + dy] = c + (255,)
    inset, radius = int(S * 0.06), int(S * 0.24)
    body = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    body.paste(grad, (0, 0), squircle_mask(S, inset, radius))

    # Soft top highlight.
    hi = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(hi).ellipse((-S * 0.2, -S * 0.75, S * 1.2, S * 0.42), fill=(255, 255, 255, 46))
    hi = hi.filter(ImageFilter.GaussianBlur(S * 0.05))
    hi_masked = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hi_masked.paste(hi, (0, 0), squircle_mask(S, inset, radius))
    body = Image.alpha_composite(body, hi_masked)

    # Drop shadow for the tiles.
    tiles = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(tiles)
    g0, g1, gap = S * 0.25, S * 0.75, S * 0.07
    cell = (g1 - g0 - gap) / 2
    r = cell * 0.28
    boxes = []
    for row in range(2):
        for col in range(2):
            x0 = g0 + col * (cell + gap)
            y0 = g0 + row * (cell + gap)
            boxes.append((x0, y0, x0 + cell, y0 + cell))
    for i, b in enumerate(boxes):
        if i == 1:
            continue
        d.rounded_rectangle(b, r, fill=(255, 255, 255, 245))
    # Sparkle in the top-right cell: a four-point star.
    x0, y0, x1, y1 = boxes[1]
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    R, rr = cell * 0.62, cell * 0.16
    pts = []
    for k in range(8):
        ang = -math.pi / 2 + k * math.pi / 4
        rad = R if k % 2 == 0 else rr
        pts.append((cx + rad * math.cos(ang), cy + rad * math.sin(ang)))
    d.polygon(pts, fill=(255, 255, 255, 255))
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    shadow.paste((30, 30, 46, 110), (0, int(S * 0.012)), tiles.split()[3])
    shadow = shadow.filter(ImageFilter.GaussianBlur(S * 0.018))
    out = Image.alpha_composite(body, shadow)
    out = Image.alpha_composite(out, tiles)
    return out


def main():
    os.makedirs(ASSETS, exist_ok=True)
    big = render()
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024]
    imgs = {}
    for sz in sizes:
        im = big.resize((sz, sz), Image.LANCZOS)
        imgs[sz] = im
        if sz in (256, 512, 1024):
            im.save(os.path.join(ASSETS, f"logo-{sz}.png"))
    imgs[512].save(os.path.join(DOCS, "logo.png"))
    ico_sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs[256].save(os.path.join(ASSETS, "WinLaunchEnhanced.ico"),
                   sizes=[(s, s) for s in ico_sizes], append_images=[imgs[s] for s in ico_sizes[:-1]])
    print("logo written:", ASSETS)


if __name__ == "__main__":
    main()
