# 019 — Visual identity

**Status:** proposed — awaiting decision
**Date:** 2026-08-10
**Lens applied:** frontend-design

---

## Constraints that decide more than taste does

| Constraint | Source | Effect |
| --- | --- | --- |
| Font must cover **Latin Extended + Cyrillic** | 4 locales ([018](018-frontend-architecture.md)) | Disqualifies most display/"gaming" faces outright |
| Layouts tolerate **~30% longer strings** | German | Favours condensed or width-variable type; no fixed-width labels |
| Must not look like FACEIT | Competitive positioning | **FACEIT is dark + saturated orange (#FF5500).** That combination is taken |
| Server-rendered, progressive | [018](018-frontend-architecture.md) | Design must work before JS loads |
| Link previews matter | [018](018-frontend-architecture.md) | Open Graph images are a **design surface**, not an afterthought |

## The idea: take the palette from the game, not from the genre

Esports platforms all look like each other — dark grey, one hot accent, card grids, glow.
NEXTSKILL's audience doesn't live in "esports". They live in **de_dust2, cs_assault, and a
server browser**.

So the palette comes from the maps and the HUD, not from the category.

### Side colours as information, not decoration

Every CS player reads **CT blue** and **T tan/gold** instantly, without a legend. NEXTSKILL
uses them as its two structural colours:

- team identity in brackets and match pages
- **who banned which map** in the veto
- score attribution on the scoreboard

This encodes something true about the content — the thing the design skill asks structural
devices to do — rather than decorating with arbitrary brand colours.

## Colour system

```
--ground        #121110   warm near-black. Not pure black; the inside of a map at night
--surface       #1C1A17   panels, rows
--surface-up    #262320   raised: active row, open dropdown
--line          #34302B   hairlines. The primary structural device
--text          #EDE7DC   warm off-white
--text-dim      #9A9285   labels, metadata, secondary

--t             #E0B44A   T-side gold — PRIMARY accent
--ct            #6E9FD4   CT-side blue — SECONDARY accent
--live          #D9483B   C4 red. Live matches and destructive actions ONLY
```

**Why gold and not orange:** `#E0B44A` is a desaturated gold — dust2 sand, T-side kit — not
FACEIT's saturated `#FF5500`. Close enough to feel like Counter-Strike, far enough to never
be mistaken for the incumbent.

**`--live` is rationed.** If red appears anywhere except a live match or a destructive
action, it stops meaning anything.

## Typography

Three roles. Cyrillic coverage is a **hard requirement**, so this is a short list.

| Role | Face | Why |
| --- | --- | --- |
| **Display** | **Unbounded**, used sparingly — wordmark, page titles only | Geometric, wide, genuinely distinctive. Has Cyrillic. This is the one bold spend |
| **Body / UI** | **IBM Plex Sans** (Condensed for dense tables) | Full Cyrillic + Latin Extended. Engineered, slightly technical — suits a server-browser feel. Condensed absorbs German |
| **Data** | **IBM Plex Mono**, **tabular figures** | Scores, ratings, ping, seeds, match IDs, timestamps. Numbers must align in columns — proportional figures make a scoreboard unreadable |

> ⚠️ **Verify Cyrillic coverage before committing** — add to the verification backlog. A
> display face that silently falls back for Russian would be discovered by a Russian user,
> not by us.

**Tabular figures are not a detail.** A scoreboard, a bracket and a leaderboard are columns
of numbers. Proportional digits make `11` narrower than `13`, and the column visibly wobbles.

## Layout: rows, not cards

The dominant esports-dashboard look is card grids with shadows and glow. NEXTSKILL goes the
other way, taking its cue from the **server browser and the TAB scoreboard**: dense,
information-first rows separated by hairlines.

```
┌──────────────────────────────────────────────────────────┐
│ RANKING                                    pl  en  ru  de│
├──────────────────────────────────────────────────────────┤
│  1   ▌ Pukawka Five          1847 ±32    24-6    CS2     │   ▌ = side colour
│  2   ▌ Nokaut                1802 ±41    19-8    CS2     │
│  3   ▌ Stara Szkoła          1776 ±55    31-14   1.6     │   mono numerals,
│  4   ▌ Wataha                1740 ±38    17-11   1.6     │   tabular, aligned
└──────────────────────────────────────────────────────────┘
     ↑         ↑                  ↑    ↑
   rank    team name          rating  RD shown openly (025)
```

**Rating deviation is displayed, not hidden.** `1847 ±32` tells the truth: how good, and how
sure we are ([025](025-rating-system.md)). Most platforms hide uncertainty; showing it is
honest and explains why a returning team's rating moves fast.

Hairlines carry the structure. No shadows, no glow, no gradient fills.

## Signature: the veto strip

The one thing NEXTSKILL is remembered by.

Map veto is *the* ritual of competitive CS. Rendered as a single horizontal strip that reads
as one artifact — the whole veto history at a glance:

```
 ban      ban      pick     ban      ban      pick     decider
┌──────┐┌──────┐┌──────┐┌──────┐┌──────┐┌──────┐┌──────┐
│ ░░░░ ││ ░░░░ ││dust2 ││ ░░░░ ││ ░░░░ ││train ││ nuke │
│ ▚▚▚▚ ││ ▚▚▚▚ ││      ││ ▚▚▚▚ ││ ▚▚▚▚ ││      ││      │
└──────┘└──────┘└──────┘└──────┘└──────┘└──────┘└──────┘
   ▌CT     ▌T      ▌CT     ▌T      ▌CT     ▌T      auto
```

- Banned maps are **struck through in the banning team's side colour** — you can read who
  removed what without a legend
- Picked maps stay lit
- The strip is **the same component live and historical** — during veto it fills in real time
  ([039](039-public-api.md) SSE); afterwards it's the permanent record on the match page

One component, two jobs, and it's the most screenshot-and-paste-into-Discord moment in the
product — which matters given links are the growth mechanism ([018](018-frontend-architecture.md)).

## Motion budget: small and functional

| Allowed | Not allowed |
| --- | --- |
| Veto strike-through as a ban lands | Page-load animation sequences |
| Score flip on round end | Scroll-triggered reveals |
| Row highlight on live update | Parallax, ambient motion, glow pulses |
| Focus and hover states | Anything decorative |

Motion marks **state change** — something happened in a real match. Decorative motion would
dilute the only signal worth having. `prefers-reduced-motion` respected throughout.

## Open Graph images are a design surface

Since growth runs through links pasted into Discord and forums
([018](018-frontend-architecture.md)), preview cards are rendered server-side per page:

| Page | Card shows |
| --- | --- |
| Match | Both teams, side colours, score, map |
| Tournament | Name, format, team count, dates |
| Profile | Nickname, rating ±RD, game |

Generated from the same tokens as the site — one identity, not two.

## Quality floor (not negotiable)

- Responsive to mobile — **brackets on a phone are the hard case** ([023](023))
- Visible keyboard focus, using `--t`
- Contrast: `--text` on `--ground` and both side colours on `--surface` meet WCAG AA
- Works with JavaScript disabled for all content pages
- `prefers-reduced-motion` honoured

## Decision

_Pending._
