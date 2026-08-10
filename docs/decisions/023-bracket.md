# 023 — Bracket data model & rendering

**Status:** decided
**Date:** 2026-08-10

---

## Data model: generate the whole bracket up front

When a tournament starts, **every match row is created immediately** — empty, with explicit
routing links.

```
match 07  round 2  upper
  slot_a: (from match 03 winner)
  slot_b: (from match 04 winner)
  winner_to: match 12, slot A
  loser_to:  match 09, slot B     ← explicit, not computed
```

### Why explicit links rather than computed routing

Single elimination has regular maths — winner of match *n* goes to match *n/2*. **Double
elimination does not.** Loser-bracket routing depends on round parity and position, and
crossing patterns exist specifically to avoid early rematches.

> *Plain version:* the alternative is working out "where does this team go next?" every time
> a result lands. That calculation is exactly where the silent bugs live
> ([022](022-tournament-formats.md)) — the wrong team advances, nothing errors, and nobody
> notices until the final. Storing the links means routing is decided **once**, at generation
> time, in one tested function. After that, advancement is a lookup.

Three further benefits:

| Benefit | Why it matters |
| --- | --- |
| The empty bracket is renderable | Tournament pages work before a single match is played |
| Byes are resolved at generation | A bye is a pre-filled result, not a special case at runtime ([022](022-tournament-formats.md)) |
| The structure is inspectable | An organiser can see the whole shape before starting; bugs surface before the event, not during it |

### Schema shape

| Column | Note |
| --- | --- |
| `tournament_id`, `stage`, `round`, `position` | For rendering order |
| `bracket` | `upper` / `lower` / `grand_final` |
| `slot_a_source`, `slot_b_source` | Where each team comes from |
| `winner_to_match`, `winner_to_slot` | Explicit routing |
| `loser_to_match`, `loser_to_slot` | Null in single elimination |
| `integrity` | Gate on advancement ([035](035-match-result-integrity.md)) |

**Advancement is a transaction**: write result → follow links → fill slots → recompute
standings. One database, one transaction ([008](008-database.md)) — which is exactly why
[006](006-architecture.md) rejected splitting these across services.

### The bracket reset

The grand final needs **two** match rows generated up front: the final, and a conditional
"reset" match that only activates if the lower-bracket team wins the first
([022](022-tournament-formats.md)). Generating it up front rather than creating it on demand
means the most-forgotten rule in tournament software cannot be forgotten — it is already
there, waiting.

---

## Rendering

### Desktop — the tree

Columns per round, matches as compact cards, connector lines between. Horizontal scroll is
expected and fine; this is the widest content in the product, which is why public pages use
a headbar rather than a sidebar ([021](021-site-structure.md)).

Upper and lower brackets stack vertically, grand final on the right.

### Mobile — two views, because a tree is a desktop artifact

**On a phone, nobody wants the tree. They want an answer:** when do we play next, did we
advance, who's in the final.

| View | What it shows |
| --- | --- |
| **Round-by-round** (default) | One round as a vertical list, swipe/tab between rounds. Current round opens by default |
| **Follow team** | One team's route through the bracket — played, next, and remaining. Reachable from any team name |

```
◀ Runda 2 ▶                    Pukawka Five
┌────────────────┐              │
│ Pukawka   13 ✓ │              ├─ R1  ✓ 13–4
│ Nokaut    11   │              ├─ R2  ✓ 13–11
├────────────────┤              ├─ PÓŁFINAŁ · dziś 20:00
│ Wataha     7   │              └─ FINAŁ  ?
│ Husaria    9 ✓ │
└────────────────┘
```

**Follow team is the more important of the two** — it answers the question the visitor
actually arrived with, and it is the natural landing view when someone opens a link shared
in Discord about their own match.

> `ponytail:` two mobile views, no pinch-zoom canvas. Ceiling: a spectator wanting the whole
> shape on a phone must page through rounds. Upgrade path: a compact overview only if that's
> actually requested.

### Shared with the veto strip

Both are read-only renderings of match state, both update over SSE
([039](039-public-api.md)), and both must reconstruct correctly from a plain fetch if events
are missed — the live stream is a view, never a record ([035](035-match-result-integrity.md)).

## The runnable check

Beyond [022](022-tournament-formats.md)'s engine tests, one check on generation:

1. A generated 16-team double-elim bracket has **every** `winner_to` and `loser_to` pointing
   at a real match and slot — no dangling links, no two matches feeding the same slot.
2. The grand-final reset match exists and is inactive until triggered.
3. A generated 13-team bracket assigns byes to top seeds and marks them **unrated**
   ([025](025-rating-system.md)).

Check 1 catches generation bugs before a tournament is ever played, which is the only time
they are cheap to fix.
