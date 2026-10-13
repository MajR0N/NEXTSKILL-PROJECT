"""Measures every load-bearing colour pair in nextskill.css against WCAG 2.2 AA.

044 requires contrast "measured, not assumed, including --text-dim on --surface".
That sentence exists because --text-dim is where a dark theme always fails: it is
the token a designer nudges down one more step to make a screen feel calmer, and
it is the token carrying provenance, freshness and uncertainty -- exactly the
facts 019 says may never be hidden to make a screen look cleaner.

Reads the tokens straight out of the stylesheet, so it cannot drift from what
ships. Run:  python docs/design/check-contrast.py [--theme candidate.css]
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent.parent
CSS = ROOT / "src" / "NextSkill.Api" / "wwwroot" / "css" / "nextskill.css"

AA_TEXT = 4.5          # normal text
AA_LARGE = 3.0         # >=18.66px bold or >=24px, and meaningful boundaries
AAA_TEXT = 7.0


def srgb_to_linear(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def luminance(hex_colour):
    h = hex_colour.lstrip("#")
    if len(h) == 3:
        h = "".join(ch * 2 for ch in h)
    r, g, b = (int(h[i:i + 2], 16) for i in (0, 2, 4))
    return 0.2126 * srgb_to_linear(r) + 0.7152 * srgb_to_linear(g) + 0.0722 * srgb_to_linear(b)


def ratio(fg, bg):
    a, b = luminance(fg), luminance(bg)
    lo, hi = min(a, b), max(a, b)
    return (hi + 0.05) / (lo + 0.05)


def blend(fg_hex, alpha, bg_hex):
    """Flatten a translucent colour onto its background. --line is rgba, and a
    border's real contrast is what the eye sees after compositing, not the
    nominal value."""
    def parts(h):
        h = h.lstrip("#")
        if len(h) == 3:
            h = "".join(ch * 2 for ch in h)
        return [int(h[i:i + 2], 16) for i in (0, 2, 4)]
    f, b = parts(fg_hex), parts(bg_hex)
    return "#%02X%02X%02X" % tuple(round(f[i] * alpha + b[i] * (1 - alpha)) for i in range(3))


def load_tokens(path):
    css = path.read_text(encoding="utf-8")
    root = css[css.index(":root {"):css.index("/* ── reset")]
    tokens = dict(re.findall(r"--([\w-]+):\s*(#[0-9A-Fa-f]{3,8});", root))
    rgba = dict(re.findall(r"--([\w-]+):\s*rgba\(([^)]+)\);", root))
    for name, args in rgba.items():
        r, g, b, a = [x.strip() for x in args.split(",")]
        tokens[name] = blend("#%02X%02X%02X" % (int(r), int(g), int(b)), float(a), tokens["surface"])
    return tokens


# fg, bg, required, why it is load-bearing
PAIRS = [
    ("text", "ground", AA_TEXT, "body copy"),
    ("text", "surface", AA_TEXT, "body copy on a card"),
    ("text", "surface-2", AA_TEXT, "body copy on a raised surface"),
    ("text-mid", "ground", AA_TEXT, "lede, secondary copy"),
    ("text-mid", "surface", AA_TEXT, "secondary copy on a card"),
    ("text-dim", "ground", AA_TEXT, "044 names this: provenance, freshness"),
    ("text-dim", "surface", AA_TEXT, "044 names this explicitly"),
    ("text-dim", "surface-2", AA_TEXT, "table headers, meta"),
    ("accent", "ground", AA_LARGE, "routine accent as boundary/underline"),
    ("accent", "surface", AA_LARGE, "tab underline, sidebar current"),
    ("on-accent", "accent", AA_TEXT, "primary button label"),
    # Red is rationed to four states, so it is measured on every surface it can
    # legally appear on. The margin on surface-2 is ~1%: this is the assertion
    # that stops a later surface tweak from quietly breaking AA.
    ("red", "ground", AA_TEXT, "RATIONED: live, urgent, error, destructive"),
    ("red", "surface", AA_TEXT, "live/urgent pill on a card"),
    ("red", "surface-2", AA_TEXT, "red text on a raised surface - TIGHTEST PAIR"),
    ("on-red", "red-solid", AA_TEXT, "white on the one filled red (destructive confirm)"),
    ("win", "surface", AA_TEXT, "form strip W"),
    ("loss", "surface", AA_TEXT, "form strip L - a lost match is NOT an error"),
    ("t", "surface", AA_LARGE, "T-side attribution, veto only"),
    ("ct", "surface", AA_LARGE, "CT-side attribution, veto only"),
    # WCAG 1.4.11 governs what IDENTIFIES a control, not decorative separation.
    # --line-soft/--line divide table rows and sections: losing them costs
    # tidiness, not comprehension, and a 3:1 hairline between every row would
    # fight the content. --line-ctl draws button and input borders, which are
    # the only thing distinguishing those controls from the page.
    ("line-ctl", "surface-2", AA_LARGE, "button/input border IS the control"),
    ("line-ctl", "ground", AA_LARGE, "control border against the page"),
]

path = CSS
if "--theme" in sys.argv:
    path = pathlib.Path(sys.argv[sys.argv.index("--theme") + 1])

tokens = load_tokens(path)
print("Tokens read from %s\n" % path.name)
print("%-28s %-11s %7s  %-6s %s" % ("PAIR", "REQUIRED", "ACTUAL", "", "PURPOSE"))
print("-" * 96)

failures = []
for fg, bg, need, why in PAIRS:
    if fg not in tokens or bg not in tokens:
        print("%-28s %-11s %7s  %-6s %s" % ("%s on %s" % (fg, bg), "-", "MISSING", "!!", why))
        failures.append((fg, bg, 0, need))
        continue
    r = ratio(tokens[fg], tokens[bg])
    ok = r >= need
    mark = "pass" if ok else "FAIL"
    if ok and need == AA_TEXT and r >= AAA_TEXT:
        mark = "AAA"
    print("%-28s %-11s %6.2f:1  %-6s %s"
          % ("%s on %s" % (fg, bg), "%.1f:1" % need, r, mark, why))
    if not ok:
        failures.append((fg, bg, r, need))

print("-" * 96)
if failures:
    print("\n%d FAILURE(S) - these are accessibility defects, not taste:" % len(failures))
    for fg, bg, r, need in failures:
        print("   --%s on --%s: %.2f:1, needs %.1f:1" % (fg, bg, r, need))
    sys.exit(1)
print("\nOK - %d pairs measured, all meet WCAG 2.2 AA" % len(PAIRS))
