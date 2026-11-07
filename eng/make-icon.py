"""Draws src/Sway/Sway.ico: a dark glass touchpad with a warm left edge (brightness) and a cool right edge (volume).

  py eng/make-icon.py

Each size is drawn on its own 8x canvas and downsampled, so the edge bars stay thick enough to read at 16 px."""
import pathlib
from PIL import Image, ImageDraw, ImageFilter

OUT = pathlib.Path(__file__).resolve().parent.parent / "src/Sway/Sway.ico"
SIZES = [16, 24, 32, 48, 64, 256]


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def vgrad(w, h, stops):
    """Vertical gradient through (t, rgba) stops."""
    im = Image.new("RGBA", (w, h))
    px = im.load()
    for y in range(h):
        t = y / max(1, h - 1)
        for i in range(len(stops) - 1):
            if stops[i][0] <= t <= stops[i + 1][0]:
                c = lerp(stops[i][1], stops[i + 1][1], (t - stops[i][0]) / (stops[i + 1][0] - stops[i][0]))
                break
        for x in range(w):
            px[x, y] = c
    return im


def draw(size):
    S = size * 8
    small = size <= 32
    m = round(S * (0.03 if small else 0.06))  # outer margin
    r = round(S * 0.24)
    tile = vgrad(S, S, [(0, (44, 41, 66, 255)), (0.6, (18, 17, 28, 255)), (1, (11, 10, 18, 255))])
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([m, m, S - m, S - m], r, fill=255)
    img = Image.new("RGBA", (S, S))
    img.paste(tile, (0, 0), mask)
    d = ImageDraw.Draw(img)
    # top highlight + rim
    d.rounded_rectangle([m, m, S - m, S - m], r, outline=(255, 255, 255, 40), width=max(8, S // 110))
    if not small:  # inner pad outline only where it can be seen
        p = round(S * 0.17)
        d.rounded_rectangle([p, round(S * 0.22), S - p, S - round(S * 0.22)], round(S * 0.08), outline=(255, 255, 255, 16), width=S // 160)
    # edge bars: thicker at small sizes
    bw = round(S * (0.16 if small else 0.075))
    inset = round(S * (0.2 if small else 0.235))
    top, bot = round(S * (0.22 if small else 0.29)), round(S * (0.78 if small else 0.71))
    bars = Image.new("RGBA", (S, S))
    for x0, stops in (
        (inset, [(0, (255, 211, 138, 255)), (0.55, (255, 182, 72, 255)), (1, (255, 95, 143, 255))]),
        (S - inset - bw, [(0, (182, 244, 255, 255)), (0.55, (62, 224, 255, 255)), (1, (139, 108, 255, 255))]),
    ):
        g = vgrad(bw, bot - top, stops)
        bm = Image.new("L", (bw, bot - top), 0)
        ImageDraw.Draw(bm).rounded_rectangle([0, 0, bw - 1, bot - top - 1], bw // 2, fill=255)
        bars.paste(g, (x0, top), bm)
    if not small:
        glow = bars.filter(ImageFilter.GaussianBlur(S * 0.04))
        img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (S, S)), mask))
    img = Image.alpha_composite(img, bars)
    return img.resize((size, size), Image.LANCZOS)


imgs = [draw(s) for s in SIZES]
imgs[-1].save(OUT, format="ICO", sizes=[(s, s) for s in SIZES], append_images=imgs[:-1])
imgs[-1].save(OUT.with_suffix(".png"))
print(OUT)
