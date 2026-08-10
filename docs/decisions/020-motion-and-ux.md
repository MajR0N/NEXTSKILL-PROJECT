# 020 — Motion & UX behaviour

**Status:** decided
**Date:** 2026-08-10

---

## Motion budget

**Motion marks state change. Nothing else moves.**

| Allowed | Not allowed |
| --- | --- |
| Veto strike-through as a ban lands | Page-load animation sequences |
| Score flip on round end | Scroll-triggered reveals |
| Row highlight when a live result updates | Parallax, ambient drift |
| Live pulse on the live indicator only | Glow pulses on static elements |
| Focus and hover states | Anything purely decorative |

**Why so strict:** the only motion signal worth having is *"something just happened in a real
match."* If the page is already moving for decorative reasons, that signal is lost in noise.
Rationing motion is the same logic as rationing `--live` red ([019](019-visual-identity.md)).

`prefers-reduced-motion: reduce` is respected everywhere — and because motion only ever marks
state change, removing it costs nothing. The state is always also expressed in text or colour.

## Progressive enhancement is a hard rule

[018](018-frontend-architecture.md) chose server-rendered Razor Pages with TypeScript islands.
That only pays off if the pages genuinely work without the islands:

| Must work with JavaScript disabled | May require JavaScript |
| --- | --- |
| Every content page — rankings, profiles, tournaments, matches, news, LFT | Live veto ([028](028-veto-and-lobby.md)) |
| Every form — login, register, team management, admin | Live bracket updates |
| Bracket in its static state | Live match view |
| Navigation, locale switching | Copy-to-clipboard convenience |

**A page that renders empty without JS breaks link unfurling** ([018](018-frontend-architecture.md)),
which is the growth mechanism. This is not a purity exercise — it is the same requirement as
Discord previews, arrived at from the other side.

## UX decisions worth stating

### Show uncertainty, don't hide it

`1847 ±32` and the confidence bar ([019](019-visual-identity.md)). Most platforms hide rating
uncertainty. Showing it is honest, and it pre-empts the most common complaint — *"why did my
rating jump so much?"* — with a visible answer ([025](025-rating-system.md)).

### Surface blockers early, not at the moment of failure

The `setinfo` warning appears in the lobby, **not** when a player fails to join a team
([028](028-veto-and-lobby.md)). The same principle applies throughout: a problem shown early
is a fix; shown late it is a delay.

### Empty states are a designed surface

A new player has no team, no rating, no matches. **Designs look good with data and fall apart
without it**, so every list has a designed empty state that says what the thing is and offers
the next action — never a blank panel.

Flagged for prototyping ([019](019-visual-identity.md)) precisely because it is the classic
blind spot.

### Never a spinner where a state exists

"Waiting for opponent", "server starting", "veto in progress — Nokaut's turn" are all real
states with real information. A generic spinner throws that away.

### Destructive actions confirm; ordinary ones don't

Disband team, revoke token, cancel tournament, override a result — confirm, and audit
([036](036-authorization.md)). Everything else is one click.

### Accessibility floor

- Visible keyboard focus, using `--t`
- Colour never carries meaning alone — form dots and movement arrows use **shape too**
  ([019](019-visual-identity.md))
- Contrast meets WCAG AA
- Tables use real table semantics, so screen readers announce them properly
- The veto strip is operable by keyboard

Accessibility sits with security in CONTEXT §3: never simplified away.
