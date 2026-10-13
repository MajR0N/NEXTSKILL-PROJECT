"""Digitises 044-logo-family-reference.png into clean vector artwork.

044 requires the SELECTED lettering to be "optically redrawn as deterministic
vector artwork". An earlier attempt built letterforms parametrically from 044's
prose instead, and produced a different logotype with inconsistent terminals --
blade cuts on some letters, square ends on others, ad-hoc constants in the K.
That is not a redraw, and it looked generated.

This traces the reference itself, so the shipped outlines ARE the chosen forms:

  1. split the image into an ink mask and a red mask by hue
  2. marching squares over each mask -> closed boundary loops
  3. Ramer-Douglas-Peucker simplification. The reference lettering is built from
     straight edges, so this is the ideal case: hundreds of stair-stepped pixel
     boundary points collapse onto the handful of real corners.
  4. emit SVG polygons, normalised to a 100-unit cap height

Run:  python docs/design/trace_reference.py
"""
import math
import pathlib

from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
SRC = HERE / "044-logo-family-reference.png"
OUT = HERE / "logo"

EPSILON = 1.4     # RDP tolerance in source pixels. Higher = fewer, straighter
                  # edges; too high and the blade terminals round off.
MIN_AREA = 60     # drop specks left by PNG compression noise


# ── masks ───────────────────────────────────────────────────────────────────
def masks(img):
    """ink = the white letterforms, red = the accent. Split by hue, not
    brightness: the red is dark enough that a luminance threshold would drop it
    into the background."""
    w, h = img.size
    px = img.load()
    ink = bytearray(w * h)
    red = bytearray(w * h)
    for y in range(h):
        row = y * w
        for x in range(w):
            r, g, b = px[x, y][:3]
            if r > 110 and r - g > 55 and r - b > 55:
                red[row + x] = 1
            elif r > 120 and g > 120 and b > 120:
                ink[row + x] = 1
    return ink, red, w, h


# ── marching squares ────────────────────────────────────────────────────────
# Emits boundary segments per cell, then stitches them into closed loops. Chosen
# over Moore-neighbour tracing because it handles several disjoint letters in
# one pass without bookkeeping about which pixels were already visited.
CASES = {
    1:  [((0, .5), (.5, 1))], 2:  [((.5, 1), (1, .5))],
    3:  [((0, .5), (1, .5))], 4:  [((.5, 0), (1, .5))],
    5:  [((0, .5), (.5, 0)), ((.5, 1), (1, .5))],
    6:  [((.5, 0), (.5, 1))], 7:  [((0, .5), (.5, 0))],
    8:  [((0, .5), (.5, 0))], 9:  [((.5, 0), (.5, 1))],
    10: [((0, .5), (.5, 1)), ((.5, 0), (1, .5))],
    11: [((.5, 0), (1, .5))], 12: [((0, .5), (1, .5))],
    13: [((.5, 1), (1, .5))], 14: [((0, .5), (.5, 1))],
}


def loops(mask, w, h):
    def at(x, y):
        return mask[y * w + x] if 0 <= x < w and 0 <= y < h else 0

    segs = {}
    for y in range(-1, h):
        for x in range(-1, w):
            case = (at(x, y) << 3) | (at(x + 1, y) << 2) | \
                   (at(x + 1, y + 1) << 1) | at(x, y + 1)
            if case in (0, 15):
                continue
            for (ax, ay), (bx, by) in CASES[case]:
                a = (round((x + ax) * 2), round((y + ay) * 2))
                b = (round((x + bx) * 2), round((y + by) * 2))
                segs.setdefault(a, []).append(b)
                segs.setdefault(b, []).append(a)

    out, seen = [], set()
    for start in list(segs):
        if start in seen:
            continue
        loop, cur, prev = [start], start, None
        seen.add(start)
        while True:
            nxt = None
            for cand in segs.get(cur, ()):
                if cand != prev and (cand not in seen or cand == start):
                    nxt = cand
                    break
            if nxt is None or nxt == start:
                break
            loop.append(nxt)
            seen.add(nxt)
            prev, cur = cur, nxt
        if len(loop) > 8:
            out.append([(px / 2, py / 2) for px, py in loop])
    return out


# ── simplification ──────────────────────────────────────────────────────────
def rdp(points, eps):
    """Ramer-Douglas-Peucker, iterative so a long boundary cannot blow the
    recursion limit."""
    if len(points) < 3:
        return points
    keep = [False] * len(points)
    keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        i, j = stack.pop()
        if j <= i + 1:
            continue
        (x1, y1), (x2, y2) = points[i], points[j]
        dx, dy = x2 - x1, y2 - y1
        norm = math.hypot(dx, dy) or 1.0
        best, bi = -1.0, i
        for k in range(i + 1, j):
            x0, y0 = points[k]
            d = abs(dy * x0 - dx * y0 + x2 * y1 - y2 * x1) / norm
            if d > best:
                best, bi = d, k
        if best > eps:
            keep[bi] = True
            stack.append((i, bi))
            stack.append((bi, j))
    return [p for p, k in zip(points, keep) if k]


def area(poly):
    return abs(sum(poly[i][0] * poly[(i + 1) % len(poly)][1] -
                   poly[(i + 1) % len(poly)][0] * poly[i][1]
                   for i in range(len(poly)))) / 2


# ── run ─────────────────────────────────────────────────────────────────────
img = Image.open(SRC).convert("RGB")
ink_mask, red_mask, W, H = masks(img)

ink = [rdp(p, EPSILON) for p in loops(ink_mask, W, H)]
red = [rdp(p, EPSILON) for p in loops(red_mask, W, H)]
ink = [p for p in ink if len(p) >= 3 and area(p) > MIN_AREA]
red = [p for p in red if len(p) >= 3 and area(p) > MIN_AREA]

# The reference holds two marks stacked; split them on the vertical gap so each
# can be exported on its own.
def band(polys, lo, hi):
    return [p for p in polys
            if lo <= sum(y for _, y in p) / len(p) < hi]


split = H * 0.55
GROUPS = {
    "wordmark": (band(ink, 0, split), band(red, 0, split)),
    "nxs": (band(ink, split, H), band(red, split, H)),
}

RED, INK, INK_DARK = "#FF2929", "#F2F2F2", "#0D0D0D"


def emit(ink_polys, red_polys, ink_fill, red_fill, pad=6):
    everything = ink_polys + red_polys
    xs = [x for p in everything for x, _ in p]
    ys = [y for p in everything for _, y in p]
    x0, y0, y1 = min(xs), min(ys), max(ys)
    k = 100.0 / (y1 - y0)          # normalise cap height to 100 units

    def path(polys, fill):
        out = []
        for p in polys:
            d = " ".join("%s%.2f,%.2f" % ("M" if i == 0 else "L",
                                          (x - x0) * k + pad, (y - y0) * k + pad)
                         for i, (x, y) in enumerate(p)) + " Z"
            out.append('    <path d="%s" fill="%s"/>' % (d, fill))
        return "\n".join(out)

    w = (max(xs) - x0) * k + pad * 2
    h = (y1 - y0) * k + pad * 2
    body = path(ink_polys, ink_fill)
    if red_polys:
        body += "\n" + path(red_polys, red_fill)
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %.2f %.2f" '
            'role="img" aria-label="NEXTSKILL">\n  <title>NEXTSKILL</title>\n'
            '%s\n</svg>\n' % (w, h, body))


OUT.mkdir(exist_ok=True)
written = 0
for name, (i_polys, r_polys) in GROUPS.items():
    if not i_polys:
        continue
    variants = {
        "%s.svg" % name: (i_polys, r_polys, INK, RED),
        "%s-mono.svg" % name: (i_polys + r_polys, [], INK, RED),
        "%s-inverse.svg" % name: (i_polys + r_polys, [], INK_DARK, RED),
    }
    for fn, (ip, rp, inkf, redf) in variants.items():
        (OUT / fn).write_text(emit(ip, rp, inkf, redf), encoding="utf-8")
        written += 1
    pts = sum(len(p) for p in i_polys + r_polys)
    ri = sum(map(area, r_polys))
    tot = ri + sum(map(area, i_polys))
    print("%-9s %2d shapes  %4d points  red %.1f%%"
          % (name, len(i_polys) + len(r_polys), pts, ri / tot * 100 if tot else 0))

# ── favicon ─────────────────────────────────────────────────────────────────
# DECIDED 2026-08-23: the lone X in ink. NXS is an unreadable smudge at 16px,
# and a RED X collides with the universal close/delete/error glyph. The X is
# the wordmark's own third letter -- lifted from the traced artwork, not
# redrawn -- so it is not the separate pictorial emblem 044 forbids.
# THE X HAS THREE WHITE STROKES, NOT FOUR. In this lettering the red shard IS
# the X's upper-right stroke -- the letter is completed by the accent, it is not
# decorated by it. So the X can never be exported on its own: drop the shard and
# you ship a broken three-armed glyph. Any one-colour rendering must MERGE the
# shard into the ink rather than discard it.
word_ink = sorted(GROUPS["wordmark"][0], key=lambda p: min(x for x, _ in p))
x_body = word_ink[2]
x_arm = GROUPS["wordmark"][1][0]
(OUT / "favicon.svg").write_text(emit([x_body], [x_arm], INK, RED), encoding="utf-8")
(OUT / "favicon-mono.svg").write_text(emit([x_body, x_arm], [], INK, RED), encoding="utf-8")
(OUT / "favicon-inverse.svg").write_text(emit([x_body, x_arm], [], INK_DARK, RED), encoding="utf-8")
written += 3

for fn in ("favicon.svg", "favicon-mono.svg", "favicon-inverse.svg"):
    assert (OUT / fn).read_text(encoding="utf-8").count("<path") == 2,         "%s lost the X's fourth stroke" % fn

print("wrote %d files to %s" % (written, OUT))
