# 041 — Layout system

**Status:** decided
**Date:** 2026-08-10

Closes a gap: [021](021-site-structure.md) settled *which pages exist* and
[019](019-visual-identity.md) settled *how things look*. Neither settled **how a page is
composed**.

---

## Five archetypes, not twenty layouts

Every page in [021](021-site-structure.md) is one of five shapes. This is what makes the
layout work finite.

| # | Archetype | Pages | Width | Shape |
| --- | --- | --- | --- | --- |
| **1** | **Index / list** | ranking, tournaments, matches, LFT, news, teams | default | Header → filters → rows → pagination |
| **2** | **Detail** | match, team, player, tournament, news post | default | Header → primary content → meta rail |
| **3** | **Wide canvas** | **bracket** | wide | Full-bleed, horizontal scroll, own rules |
| **4** | **Focused task** | lobby, veto, login, register, confirm flows | narrow | Single purpose, nothing else on screen |
| **5** | **Panel** | admin, organiser | fluid | Sidebar + dense tables and forms |

**If the design survives all five, it survives the product.** That's the prototype scope.

## Content widths

```
narrow    560px    focused task — one thing to do, nothing to scan past
default  1080px    index + detail — the workhorse
wide      full     bracket only, min 20px gutters
panel     240px sidebar + fluid content, capped 1440px
```

**Why 1080 and not wider:** a ranking row has six columns; beyond ~1080px the eye has to
travel too far between team name and rating, and the row stops reading as one unit. Wider is
not more generous, it is harder to scan.

**Why 560 for tasks:** a login form or a lobby has one job. Anything wider invites putting
something else next to it, which is exactly what a focused task must not have.

## Spacing scale

A single 4px-based scale. **No arbitrary values** — if a gap isn't on the scale, it's a bug.

```
4   8   12   16   24   32   48   64   96
```

| Use | Value |
| --- | --- |
| Inside a chip / pill | 4–8 |
| Inside a card, cell padding | 12–16 |
| Between related elements | 16–24 |
| Between component groups | 32 |
| **Between page sections** | **64** ([019](019-visual-identity.md) — sections earn their space) |
| Above a page header | 48 |

> *Plain version:* the point of a scale isn't tidiness — it's that **spacing becomes
> information**. When every gap is one of nine values, the size of a gap tells the reader how
> related two things are. Arbitrary spacing destroys that signal, and it's why ad-hoc layouts
> feel vaguely wrong without anyone being able to say why.

## Breakpoints — only two

```
mobile      < 720px      single column, drawer nav
desktop    ≥ 720px       full layout
panel      ≥ 1024px      sidebar becomes persistent (drawer below)
```

**Two breakpoints, not five.** Everything between is handled by fluid widths, `clamp()` for
type, and `auto-fit` grids. Every breakpoint is a layout that must be built and tested;
adding them is not free.

## Density: two modes

| Mode | Row height | Where |
| --- | --- | --- |
| **Comfortable** | 48px rows, 12–16px padding | Public pages — read occasionally, on any device |
| **Dense** | 36px rows, 8–10px padding | Panels — worked in daily, information over comfort ([015](015-admin-panel.md)) |

Same components, one modifier class. **Not a second design system** — an organiser scanning
80 matches has different needs from a player checking one ranking.

## Component inventory

Everything the product needs. **If a screen requires a component not on this list, that's a
decision, not an improvisation.**

| # | Component | Notes |
| --- | --- | --- |
| 1 | **Button** | primary / secondary / ghost / **destructive** (destructive confirms — [020](020-motion-and-ux.md)) |
| 2 | **Input, select, textarea** | With label, hint, and **error state that says how to fix it** |
| 3 | **Table + row** | Real `<table>` semantics for screen readers ([020](020-motion-and-ux.md)) |
| 4 | **Team cell** | Avatar + name + tag + meta. Used in ~8 places — build once |
| 5 | **Rating cell** | Value + ±RD + confidence bar ([025](025-rating-system.md)) |
| 6 | **Form strip** | Last-5 W/L, colour **and** letter |
| 7 | **Pill / status chip** | Confirmed, live, disputed, provisional |
| 8 | **Match card** | Compact 3-column, accent edge by state |
| 9 | **Veto tile + strip** | Live and historical, same component ([028](028-veto-and-lobby.md)) |
| 10 | **Bracket node** | Two slots, score, state |
| 11 | **Avatar** | Generated monogram ([019](019-visual-identity.md)) |
| 12 | **Headbar / sidebar** | ([021](021-site-structure.md)) |
| 13 | **Empty state** | Title, one line, one action. **Never a blank panel** ([020](020-motion-and-ux.md)) |
| 14 | **Confirm dialog** | Destructive actions only |
| 15 | **Inline alert / toast** | Toast for transient, inline for anything needing action |
| 16 | **Pagination** | Cursor-based ([039](039-public-api.md)) |
| 17 | **Tabs** | Round switching on mobile bracket, profile sections |
| 18 | **Locale switcher** | Header **and** footer ([018](018-frontend-architecture.md)) |

Eighteen components cover every screen in [021](021-site-structure.md).

## Archetype composition rules

### Index

```
[ eyebrow ]
[ H2 ]                            ← 48 above, 24 below
[ filters/sort ]                  ← sticky on scroll
[ table rows ]                    ← comfortable density
[ pagination ]
```

Filters are **stateful in the URL** — a filtered ranking must be shareable, since links are
the growth mechanism ([018](018-frontend-architecture.md)).

### Detail

```
[ header: title, status pills, primary action ]
[ primary content        ][ meta rail 280px ]
                          ← rail drops below content on mobile
```

The meta rail carries facts (map, server, time, demo, integrity state), never actions.
Actions live in the header where they're found without hunting.

### Wide canvas — bracket

Full-bleed, its own horizontal scroll container, **page body never scrolls sideways**.
Mobile switches to round-by-round + follow-team ([023](023-bracket.md)) — a different layout,
not a shrunken one.

### Focused task

560px, centred. **No navigation clutter**: the headbar reduces to the wordmark and locale
switcher. Nothing competes with the one thing being done.

### Panel

Persistent sidebar ≥1024px, drawer below. Dense mode. Content capped at 1440px so tables
don't stretch to absurd widths on large monitors.

## The rule that keeps this honest

**A page is an archetype plus content.** If a new page doesn't fit one of the five, either it
belongs in an existing archetype and is being over-thought, or the archetype list is genuinely
missing one — which is a decision to record here, not a one-off layout to invent.
