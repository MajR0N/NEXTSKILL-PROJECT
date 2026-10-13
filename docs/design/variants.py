"""Studies over the TRACED wordmark: how the red enters, and the small mark.

Everything reads `logo/wordmark.svg`, so every variant is built from the real
selected lettering. Nothing here invents a letterform.

Two rules the artwork itself dictates, both learned the hard way:

  THE SHARD IS THE X'S FOURTH STROKE. The white X has three arms; the red
  completes it. So the shard is drawn in EVERY treatment and takes the X's
  colour. Colouring letters without drawing it ships a broken three-armed X.

  THE HOUSE ANGLE IS ~33 DEGREES. Measured, not guessed: the shard's long edges
  sit at 32.4 and 33.5 degrees and the X's diagonal at 32.9. Any cut through the
  artwork should use that angle, or it reads as a stripe laid on top rather than
  a slice made by the same blade.

Run:  python docs/design/variants.py
"""
import math
import pathlib
import re

HERE = pathlib.Path(__file__).resolve().parent
OUT = HERE / "logo"

RED, INK, GROUND = "#FF2929", "#F2F2F2", "#0D0D0D"
SURFACE, GRAPHITE, STEEL = "#151515", "#252525", "#6E6E6E"
HOUSE = 33.0          # degrees, measured from the shard and the X's diagonal
KERF = 3.2            # width of the gap a cut leaves behind


# ── geometry ────────────────────────────────────────────────────────────────
def load(name):
    src = (OUT / name).read_text(encoding="utf-8")
    vb = [float(v) for v in re.search(r'viewBox="([^"]+)"', src).group(1).split()]
    shapes = []
    for d, fill in re.findall(r'<path d="([^"]+)" fill="([^"]+)"', src):
        pts = [tuple(map(float, p.split(",")))
               for p in re.findall(r"[ML]([-\d.]+,[-\d.]+)", d)]
        shapes.append((pts, fill))
    return shapes, vb


def bbox(pts):
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return min(xs), min(ys), max(xs), max(ys)


def area(poly):
    return abs(sum(poly[i][0] * poly[(i + 1) % len(poly)][1] -
                   poly[(i + 1) % len(poly)][0] * poly[i][1]
                   for i in range(len(poly)))) / 2


def scale_about(poly, k):
    cx = sum(p[0] for p in poly) / len(poly)
    cy = sum(p[1] for p in poly) / len(poly)
    return [(cx + (x - cx) * k, cy + (y - cy) * k) for x, y in poly]


def move(poly, dx, dy):
    return [(x + dx, y + dy) for x, y in poly]


def path(pts, fill):
    d = " ".join("%s%.2f,%.2f" % ("M" if i == 0 else "L", x, y)
                 for i, (x, y) in enumerate(pts)) + " Z"
    return '  <path d="%s" fill="%s"/>' % (d, fill)


def wrap(vb, body, defs=""):
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="%g %g %g %g" '
            'role="img" aria-label="NEXTSKILL">\n  <title>NEXTSKILL</title>\n'
            '%s%s\n</svg>\n' % (vb[0], vb[1], vb[2], vb[3], defs, body))


def cut(box, frac, angle=HOUSE, reach=900.0, above=False):
    """A line through the artwork at the house angle. Returns (region, kerf):
    the half-plane on one side of the line, and the thin band along it.
    `above` flips which side the accent lands on."""
    x0, y0, x1, y1 = box
    cx = (x0 + x1) / 2
    cy = y0 + (y1 - y0) * frac
    a = math.radians(angle)
    dx, dy = math.cos(a), -math.sin(a)      # along the cut
    nx, ny = math.sin(a), math.cos(a)       # normal, pointing down-screen
    if above:
        nx, ny = -nx, -ny
    p1 = (cx - dx * reach, cy - dy * reach)
    p2 = (cx + dx * reach, cy + dy * reach)
    region = [p1, p2,
              (p2[0] + nx * reach, p2[1] + ny * reach),
              (p1[0] + nx * reach, p1[1] + ny * reach)]
    k = KERF / 2
    kerf = [(p1[0] - nx * k, p1[1] - ny * k), (p2[0] - nx * k, p2[1] - ny * k),
            (p2[0] + nx * k, p2[1] + ny * k), (p1[0] + nx * k, p1[1] + ny * k)]
    return region, kerf


shapes, VB = load("wordmark.svg")
ink = sorted([s for s in shapes if s[1] != RED], key=lambda s: bbox(s[0])[0])
shard_raw = [s for s in shapes if s[1] == RED][0][0]
LETTERS = [g for g, _ in ink]          # N E X T S K I L L, left to right
X_AT = 2


# ── STUDY 1: how the red enters ─────────────────────────────────────────────
def treatment(mode):
    reds = {"next": {0, 1, 2, 3},
            "skill": set(range(4, len(LETTERS))),
            "bookend": {0, len(LETTERS) - 1},
            "all": set(range(len(LETTERS)))}.get(mode, set())
    red = [LETTERS[i] for i in sorted(reds)]
    black = [LETTERS[i] for i in range(len(LETTERS)) if i not in reds]
    # The shard still draws in every treatment -- it is the X's fourth stroke,
    # not an option. It simply takes the X's colour now that `shard` mode itself
    # is gone.
    (red if X_AT in reds else black).append(shard_raw)
    body = "\n".join(path(g, INK) for g in black)
    if red:
        body += ("\n" if body else "") + "\n".join(path(g, RED) for g in red)
    tot = sum(map(area, LETTERS)) + area(shard_raw)
    return wrap(VB, body), sum(map(area, red)) / tot * 100


print("STUDY 1 - red treatment")
# shard and letter removed 2026-08-23. The remaining treatments all carry
# substantially more red than the reference draws -- that is the direction.
for mode in ["bookend", "next", "skill", "all"]:
    svg, share = treatment(mode)
    (OUT / ("red-%s.svg" % mode)).write_text(svg, encoding="utf-8")
    print("  %-9s %5.1f%%" % (mode, share))

# PLANE removed 2026-08-23 -- every tuned variant, and the base cut with it.
# The generator for it is gone rather than left dormant: a commented-out branch
# is just a thing the next person has to decide about again.


# ── STUDY 2: the small mark ─────────────────────────────────────────────────
GLYPH_FRAC = 0.60
WIDE_FRAC = 0.78
RADIUS = 0.18
SHARD_SCALE = 1.45

N, X, S = LETTERS[0], LETTERS[2], LETTERS[4]
shard_x = scale_about(shard_raw, SHARD_SCALE)

def tile(glyphs, ground, size=100.0, cutspec=None):
    """cutspec = (frac, accent) applies the house-angle cut to the glyph."""
    allp = [p for g, _ in glyphs for p in g]
    x0, y0, x1, y1 = bbox(allp)
    k = min((size * GLYPH_FRAC) / (y1 - y0), (size * WIDE_FRAC) / (x1 - x0))
    ox = (size - (x1 - x0) * k) / 2 - x0 * k
    oy = (size - (y1 - y0) * k) / 2 - y0 * k
    place = lambda g: [(x * k + ox, y * k + oy) for x, y in g]

    body, defs = [], ""
    if ground:
        body.append('  <rect width="%.0f" height="%.0f" rx="%.0f" fill="%s"/>'
                    % (size, size, size * RADIUS, ground))
    placed = [(place(g), f) for g, f in glyphs]
    for g, f in placed:
        body.append(path(g, f))
    if cutspec:
        frac, accent = cutspec[0], cutspec[1]
        flip = len(cutspec) > 2 and cutspec[2]
        below, gap = cut(bbox([p for g, _ in placed for p in g]), frac, reach=400,
                         above=flip)
        uid = "t%d%s" % (int(frac * 1000), "a" if flip else "")
        defs = ('  <defs><clipPath id="%s"><polygon points="%s"/></clipPath></defs>\n'
                % (uid, " ".join("%.2f,%.2f" % q for q in below)))
        body.append('  <g clip-path="url(#%s)">' % uid)
        for g, _ in placed:
            body.append(path(g, accent))
        body.append("  </g>")
        body.append(path(gap, ground or GROUND))
    return wrap([0, 0, size, size], "\n".join(body), defs)


# X and N+X prototypes removed 2026-08-23: ruled out. The X's own shard stays
# in the wordmark where it belongs -- it is the letter's fourth stroke -- but
# the X is no longer a candidate for the small mark.
#
# The N shard is also gone. It was placed by translating the X's wedge onto a
# letter it was never drawn for, and it looked it.
MARKS = {
    # -- N, plain. Checked against Netflix's N, the obvious neighbour: theirs is
    #    UPRIGHT, built from folded ribbons in several reds with splayed legs.
    #    Ours is a hard italic, straight-edged, blade-terminalled, one flat
    #    colour. Different construction -- and the slant is the feature that
    #    survives longest as the mark shrinks, so small sizes separate them
    #    rather than blur them.
    "mark-n-dark":     ([(N, INK)], GROUND, None),
    "mark-n-surface":  ([(N, INK)], SURFACE, None),
    "mark-n-graphite": ([(N, INK)], GRAPHITE, None),
    "mark-n-light":    ([(N, GROUND)], INK, None),
    "mark-n-red":      ([(N, GROUND)], RED, None),
}

# -- N cut, RETUNED ---------------------------------------------------------
# The single cut at 0.54 put the boundary straight through the N's crossing,
# which is the busiest part of the letter -- the accent landed as a shapeless
# wedge on the right diagonal. A cut wants to meet a letter where the letter is
# SIMPLE. So the position is swept rather than guessed, and both directions are
# generated: `below` keeps the accent low and heavy, `above` clips the entry
# stroke instead.
# 0.66 and 0.78 cut 2026-08-23. They cleared the N's crossing, which is why I
# recommended them, but the accent ended up as a thin sliver along the foot --
# technically correct placement, no presence. The high cuts survive.
for frac in (0.30, 0.42):
    MARKS["mark-n-cut%02d-red" % (frac * 100)] = ([(N, INK)], GRAPHITE, (frac, RED))
    MARKS["mark-n-cut%02d-steel" % (frac * 100)] = ([(N, INK)], GRAPHITE, (frac, STEEL))
MARKS["mark-n-cut30-above-red"] = ([(N, INK)], GRAPHITE, (0.30, RED, True))
MARKS["mark-n-cut42-above-red"] = ([(N, INK)], GRAPHITE, (0.42, RED, True))


def lockup(groups, overlap=6):
    out, cursor = [], None
    for glyphs in groups:
        x0, _, x1, _ = bbox([q for g in glyphs for q in g])
        dx = 0 if cursor is None else cursor - x0
        out.append([[(x + dx, y) for x, y in g] for g in glyphs])
        cursor = x1 + dx - overlap
    return out


# N+X removed 2026-08-23 along with the standalone X.
#
# CAVEAT ON N+S, found by tracing the source: the S is NOT an independent glyph
# in this artwork. The T's crossbar and the S's top arm are one continuous fused
# stroke -- there is no gap between them -- so the tracer split the pair where
# their outlines happened to meet. The standalone S therefore inherits a flat
# vertical cut at its lower left where every other letter ends on a blade.
# Shipping N+S means RECONSTRUCTING a terminal that was never drawn.
NS = lockup([[N], [S]])
NS_INK = [(NS[0][0], INK), (NS[1][0], INK)]
NS_OUT = [(NS[0][0], GROUND), (NS[1][0], GROUND)]
MARKS.update({
    # N+S gets the same treatment set as the lone N, so the two can be compared
    # like for like rather than one being shown at its best and the other not.
    "mark-ns-dark":      (NS_INK, GROUND, None),
    "mark-ns-graphite":  (NS_INK, GRAPHITE, None),
    "mark-ns-red":       (NS_OUT, RED, None),
    "mark-ns-light":     (NS_OUT, INK, None),
    # Mirrors whatever cuts the lone N still carries, so the head-to-head stays
    # like-for-like. If a treatment is dropped from N it must be dropped here
    # too, or one finalist gets shown at its best against the other's worst.
    "mark-ns-cut42-red":       (NS_INK, GRAPHITE, (0.42, RED)),
    "mark-ns-cut42-steel":     (NS_INK, GRAPHITE, (0.42, STEEL)),
    "mark-ns-cut30-above-red": (NS_INK, GRAPHITE, (0.30, RED, True)),
})

print("\nSTUDY 2 - the small mark")
for name, (glyphs, ground, cutspec) in MARKS.items():
    (OUT / (name + ".svg")).write_text(tile(glyphs, ground, cutspec=cutspec),
                                       encoding="utf-8")
print("  %d candidates" % len(MARKS))
print("\nhouse angle %.0f deg; kerf %.1f; shard %.2fx; glyph %d%% of tile"
      % (HOUSE, KERF, SHARD_SCALE, GLYPH_FRAC * 100))
