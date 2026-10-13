# 019 - Visual identity: behavioural rules

**Status:** DECIDED - appearance moved to [044](044-visual-direction.md)
**Date:** 2026-08-10; appearance superseded 2026-08-23
**Relates to:** [020](020-motion-and-ux.md), [025](025-rating-system.md),
[041](041-layout-system.md), [044](044-visual-direction.md)

---

## Scope

This decision owns the behavioural rules that survive every palette and style. Decision
[044](044-visual-direction.md) owns theme, palette roles, typography, geometry, imagery,
composition, and the logo system.

The earlier CT-blue/T-gold palette, Unbounded/IBM Plex type recommendation, Server Browser visual
direction, card-versus-row prescription, and signature veto strip are superseded. They must not be
used as implementation guidance.

## Behavioural rules

### Meaning is redundant

Colour is never the only carrier of state, side, outcome, urgency, or validity. Pair it with text,
position, weight, iconography where useful, or another stable structural signal. The interface
must remain understandable in monochrome.

### Uncertainty and provenance stay visible

NEXTSKILL does not present uncertain data as fact. Rating deviation, provisional state, source,
freshness, reported-versus-verified counts, and integrity status appear wherever they affect a
decision. Hiding uncertainty to make a screen look cleaner is not permitted.

### Spacing and alignment are information

Shared alignment joins related facts; spacing separates groups and indicates hierarchy. A larger
gap must reflect a larger conceptual boundary. Arbitrary gaps and decorative misalignment weaken
the information model.

### State precedes ornament

Every component needs its relevant default, hover, focus, selected, disabled, loading, empty,
error, and live states before decorative treatment. A quiet interface is still fully designed.

### Domain colour remains domain colour

CT/T colour may be used when side identity is genuinely part of the content, such as a match or
veto. It is not a platform-wide accent and must still have a non-colour label or structural cue.

### Data remains readable without motion or JavaScript

Motion may explain a state change but never create the only evidence that it happened. Core
content, state, and navigation remain available in server-rendered markup under the progressive
enhancement rule in [018](018-frontend-architecture.md).

## Consequence

The product remains monochrome until 044's prototype and measured-token exit criteria pass. This
is a deliberate audit of these rules, not an undecided visual identity.
