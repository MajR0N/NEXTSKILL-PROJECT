# 022 — Tournament formats

**Status:** decided
**Date:** 2026-08-10
**Decision:** **All five formats at launch** — single elimination, double elimination,
round robin, group stage, league.

---

## The reframe that makes this affordable

Five formats sounds like five implementations. It isn't. **They are compositions of two
primitives.**

| Primitive | What it does |
| --- | --- |
| **Elimination bracket** | Seeds teams into a tree; losers drop out; winners advance |
| **Round-robin table** | Everyone plays everyone; standings computed from results |

Everything requested is built from those two:

| Format | Composition |
| --- | --- |
| Single elimination | Elimination × 1 |
| **Double elimination** | Elimination × 2 (upper + lower), linked by a drop-down mapping |
| Round robin | Round-robin × 1 |
| **Group stage** | Round-robin × N, then seed the qualifiers into an elimination bracket |
| **League** | Round-robin × 1, spread over time, with a season and a fixture schedule |

**Build two engines correctly and you get all five.** That is the difference between a
long project and an unreasonable one.

```
        ┌──────────────────────┐        ┌──────────────────────┐
        │ Elimination engine   │        │ Round-robin engine   │
        │  seed → advance      │        │  fixtures → standings│
        └──────────┬───────────┘        └──────────┬───────────┘
                   │                                │
   single elim ────┤                                ├──── round robin
   double elim ────┘                                ├──── league (+ schedule)
                   │                                │
                   └──────── group stage ───────────┘
                        (round robins → bracket)
```

## Build order

Each step reuses the last. Do not start step *n+1* until step *n* has a passing test.

1. **Single elimination** — proves seeding, byes, advancement
2. **Round robin** — proves fixtures, standings, tiebreakers
3. **Double elimination** — reuses elimination; adds loser-bracket routing
4. **Group stage** — composes the two engines
5. **League** — round robin plus a season and scheduling

---

## The dangerous parts, named explicitly

These are where tournament software silently ruins events. Each one gets a test.

### 1. Double elimination — loser-bracket routing

When a team loses in the upper bracket, **which lower-bracket match do they drop into?**
The mapping is not intuitive and depends on round and position. Getting it wrong produces
a bracket that *looks* fine and pairs the wrong teams.

It must also avoid immediate rematches where the format says it should.

### 2. Double elimination — the bracket reset

**The most commonly forgotten rule in all of tournament software.**

The grand final is between the upper-bracket winner (undefeated) and the lower-bracket
winner (one loss). If the *lower*-bracket team wins, both teams now have one loss — so
**they must play a second grand final.** If the upper-bracket team wins, the tournament
ends immediately.

Skipping this eliminates an undefeated team on their first loss, which is not double
elimination at all.

### 3. Round robin — tiebreakers

Three teams finish on equal points. Who qualifies? This **must** be an explicit, ordered,
configurable rule chain — never implicit database ordering, which is stable until the day
it isn't and silently changes who advances.

A sensible default chain:

1. head-to-head result
2. round difference (rounds won − rounds lost)
3. total rounds won
4. **explicit tie flag** requiring an organiser decision — never a coin flip in code

> `ponytail:` default chain plus an organiser override. Ceiling: doesn't cover exotic
> per-tournament rules. Upgrade path: make the chain fully configurable per tournament —
> but only once a real organiser asks.

### 4. Byes — non-power-of-two team counts

13 teams don't fit a clean bracket. Byes must be assigned to the **top seeds** and must not
count as played matches for ratings ([025](025)/[026](026)) — a free win must never inflate
a rating.

### 5. Group stage → playoff seeding

Which group winners meet whom? Standard practice avoids pairing teams from the same group
in the first knockout round. This is a rule that must be stated, not improvised.

### 6. League — scheduling

Fixtures across a season, with rescheduling, forfeits, and standings that update as results
land. This is the only format that introduces a genuine *time* dimension, which is why it
is built last.

---

## Mandatory tests

Per the project's engineering principles, and reinforced by
[003](003-build-basis.md) — bracket logic gets a test **before** it gets a UI. No framework,
no fixtures; one runnable file that asserts:

| # | Test | Asserts |
| --- | --- | --- |
| 1 | 8-team single elim, fixed results | correct champion; correct match count (7) |
| 2 | **13-team single elim** | byes go to top seeds; no phantom matches; bye ≠ rated win |
| 3 | 8-team double elim, fixed results | every loser lands in the correct LB match |
| 4 | **Double elim, LB team wins grand final** | **a second grand final is created** (bracket reset) |
| 5 | Double elim, UB team wins grand final | tournament ends; no extra match |
| 6 | Round robin, engineered 3-way tie | tiebreak chain applied in the correct order |
| 7 | Round robin, unbreakable tie | surfaces for organiser decision; does **not** pick silently |
| 8 | Group stage, 4 groups of 4 | correct qualifiers; same-group teams not paired in round 1 |

**Test 4 is the one most likely to be missing.** If it passes, the implementation is
probably sound. If it's absent, assume double elimination is broken.

---

## Consequences

- Longer before *any* format is proven correct — accepted deliberately, in exchange for a
  complete product with nothing to explain away to organisers.
- The two engines are pure functions over match results: given a bracket state and a
  result, return the next state. **No database, no HTTP, no time** — which is exactly why
  they are cheap to test exhaustively.
- Bracket advancement is gated on match integrity ([035](035-match-result-integrity.md)):
  a `disputed` result never advances a bracket, in any format.
- Formats live in their own feature slice ([006](006-architecture.md)):
  `Features/Brackets/`.
