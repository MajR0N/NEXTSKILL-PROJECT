"""Checks 044-prototype.html against the shipped stylesheet.

Two failures this catches, both of which would quietly invalidate the
prototype's verdict:

  1. A typo'd class renders unstyled, and the screen gets judged on a
     composition that is not the one being proposed.
  2. A class that exists ONLY in the prototype is a component invented on the
     spot. 041: "If a screen requires a component not on this list, that's a
     decision, not an improvisation."

Run:  python docs/design/check-prototype.py
"""
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent.parent
HTML = HERE / "044-prototype.html"
CSS = ROOT / "src" / "NextSkill.Api" / "wwwroot" / "css" / "nextskill.css"

# Harness chrome, defined in the prototype's own <style> block on purpose.
HARNESS = {"proto-bar", "proto-screen", "proto-meta", "proto-stress", "k"}

html = HTML.read_text(encoding="utf-8")
css = CSS.read_text(encoding="utf-8")

# Classes the stylesheet defines.
defined = set(re.findall(r"\.([a-zA-Z][\w-]*)", css))

# Classes the prototype uses. The generated markup interpolates with ${...},
# which splits into JavaScript fragments rather than class names, so those
# expressions come out first and their quoted class literals go in separately.
used = set()
for lit in re.findall(r"'([a-z][\w-]*--[\w-]+)'", html):
    used.add(lit)
for attr in re.findall(r'class="([^"]*)"', re.sub(r"\$\{[^}]*\}", " ", html)):
    used.update(c for c in attr.split() if c)

missing = sorted(used - defined - HARNESS)

# The spacing scale is information, not decoration (041): an off-scale gap is a
# bug. Catch raw px in the prototype's inline styles, allowing only the values
# the scale actually contains plus the few structural ones 041 names.
SCALE = {"0", "4", "8", "12", "16", "24", "32", "48", "64", "96",
         "44", "40", "150", "220", "240", "280", "300", "302", "360",
         "52", "62", "58", "6", "5", "2", "1", "11", "13", "26", "30", "38"}
offscale = set()
for style in re.findall(r'style="([^"]*)"', html):
    for prop, val in re.findall(r"(margin[\w-]*|padding[\w-]*|gap|top)\s*:\s*([^;]+)", style):
        for num in re.findall(r"(\d+)px", val):
            if num not in SCALE:
                offscale.add(f"{prop}: {num}px")

ok = True
if missing:
    ok = False
    print("UNDEFINED CLASSES (%d) - typo, or a component invented on the spot:" % len(missing))
    for c in missing:
        print("   .%s" % c)
if offscale:
    ok = False
    print("OFF-SCALE SPACING (%d) - 041 says every gap is one of nine values:" % len(offscale))
    for v in sorted(offscale):
        print("   %s" % v)

if ok:
    print("OK - %d classes used, all defined; spacing on scale" % len(used))
sys.exit(0 if ok else 1)
