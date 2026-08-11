# 025 — Rating, ranking & tiers

**Status:** decided
**Date:** 2026-08-10
**Supersedes:** the separate "ELO system" / "rating calculation" / "ranking" items — they are
one system with three layers, not three systems.


> **Built (2026-08-11):** both engines — Glicko-2 for teams, OpenSkill for players — with
> Glickman's published example reproduced exactly. The **team** ranking page is live, provisional
> teams separated, `±RD` shown openly. Seeding uses the same provisional rule.
>
> **Not built:** **the player ranking has no page at all**, and neither do tiers, movement
> arrows or form strips — all three need match history, which the matches slice will produce.

---

## The three layers

Only one of these is a calculation. The other two are **views** over it, which is why they
must not be designed separately — separate designs drift and contradict each other.

| Layer | Answers | Nature |
| --- | --- | --- |
| **Rating** | How good is this team / player? | A **calculation**, updated per match |
| **Ranking** | Where do they stand relative to others? | A **view** — ordered list over rating |
| **Tiers / divisions** | Which bracket do they belong in? | A **view** — buckets over ranking |

**Decision: store rating only.** Ranking and tiers are derived at read time. Storing a rank
column means every rating change invalidates everybody else's row.

---

## Evidence

Head-to-head prediction accuracy over ~10,000 professional CS:GO matches:

| System | Accuracy |
| --- | --- |
| TrueSkill, per-player granularity | **64.1%** |
| **Glicko-2** | **63.1%** |
| TrueSkill, team-level | 62.9% |
| Elo | 62.8% |

**Read this honestly: the spread is 1.3 percentage points.** Choosing the "best" algorithm
buys almost nothing. What genuinely affects whether ratings feel fair is the **policy**
around them — inactivity, uncertainty, provisional periods, what counts as a rated match.
That is where the effort belongs.

The ordering is still useful, because it maps onto the two layers we need.

## Decision

| Layer | System | Why |
| --- | --- | --- |
| **Team rating** | **Glicko-2** | Best of the team-level options on real CS data (63.1%), and it explicitly models **uncertainty and inactivity** — essential for a community scene where teams play in bursts around tournaments |
| **Player rating** | **OpenSkill** | Only per-player-granularity models beat team-level ones (64.1%). TrueSkill is **proprietary**; [OpenSkill](https://arxiv.org/pdf/2401.05451) is free, open source, faster (Bayesian approximation), and built for multi-team/multi-player inference |

### Why Glicko-2 rather than plain Elo

> *Plain version:* Elo stores one number: your rating. Glicko-2 stores three — **rating**,
> **rating deviation (RD)** = how confident the system is, and **volatility** = how
> consistent your results are.
>
> RD is the part that matters here. It **shrinks as you play** and **grows while you don't**.
> So a team returning after three months isn't treated as though its old rating is still
> accurate — the system knows it's unsure, and their first few matches move the rating in
> bigger steps until confidence is restored.
>
> This is exactly the community-scene pattern: play a tournament, disappear for two months,
> come back. Elo handles that badly. Glicko-2 handles it by design.

---

## The policy decisions that actually matter

More consequential than the algorithm choice.

### Inactivity: grow uncertainty, never decay rating

**Rating does not decay.** RD grows instead.

Decaying ratings punishes people for having lives, and it's the single most-complained-about
mechanic in amateur ladders. Growing RD achieves the real goal — "we're less sure about
them now" — without taking anything away.

### New entrants are provisional

New teams and players start with **high RD**. Until RD falls below a threshold, they are
**provisional**: rated internally, but **not shown in public rankings**.

This prevents the "played two matches, ranked #1" problem that makes a leaderboard
worthless on day one.

### Separate rating pools per game

**CS 1.6 and CS2 ratings never mix.** They are different games with different skills.
A player may hold both, independently. This follows the `game` discriminator already
required in the first migration ([001](001-game-scope.md), [008](008-database.md)).

### What counts as a rated match

| Situation | Rated? |
| --- | --- |
| Normal completed match | ✅ |
| **Bye** (non-power-of-two bracket) | ❌ **Never** — a free win must not inflate a rating ([022](022-tournament-formats.md)) |
| Match with integrity `disputed` or `suspect` | ❌ **Not until `confirmed`** ([035](035-match-result-integrity.md)) |
| Forfeit / no-show | ⚠️ Configurable per tournament; default **not rated** — it measures attendance, not skill |
| Match still inside the challenge window | ❌ Rating applies only after the window closes ([007](007-server-hosting-model.md)) |

That last row matters: **ratings must not settle before results do.** Applying a rating
change that later has to be reversed is far worse than applying it a few hours late.

### Anti-farming

Glicko-2 handles this inherently — beating a much weaker opponent gains almost nothing,
losing to them costs a lot. No extra machinery needed.

> `ponytail:` no custom anti-farm logic. Ceiling: doesn't stop *collusive* farming between
> two accounts trading wins. Upgrade path: cap rating gain per opponent pair per period —
> only if it's actually observed happening.

---

## The genuinely hard problem: roster continuity

**When is a team still "the same team"?**

Community rosters churn constantly. If four of five players leave, is the rating still
theirs? There is no correct answer, only a chosen policy:

| Policy | Trade-off |
| --- | --- |
| Team rating belongs to the team entity regardless of roster | Simple; **exploitable** — buy a highly-rated shell team and replace everyone |
| Rating resets if fewer than *N* original players remain | Fairer; needs a threshold nobody will agree on |
| Team rating is **derived** from current roster's player ratings | Self-correcting; loses "this lineup plays well together" |
| **Chosen:** team rating persists, but **RD increases sharply** when the roster changes significantly | Keeps history, expresses reduced confidence, no hard cutoff |

The chosen option reuses the mechanism already present in Glicko-2 rather than inventing a
new rule — a roster change is genuinely "we're less sure about this team now," which is
precisely what RD expresses.

> `ponytail:` roster change bumps RD; no reset rules, no thresholds to argue about.
> Ceiling: a determined exploiter can still inherit a rating by swapping players gradually.
> Upgrade path: scale the RD bump by the fraction of the roster changed — only if abuse
> appears.

---

## Player rating and CS 1.6's data gap

OpenSkill infers individual skill from **match outcomes and team composition**, not from
detailed stats. This matters enormously: CS 1.6 provides far less stat data than CS2
([001](001-game-scope.md)).

Because OpenSkill needs only *who was on which team and who won*, **player ratings work
identically for both games.** A performance-based rating (HLTV-style, computed from
kills/damage/utility) would have been CS2-only and produced two incomparable systems.

This is a strong secondary reason for OpenSkill over a stats-based rating.

---

## Consequences

- Two rating systems to maintain and tune. Accepted deliberately.
- Ranking and tier endpoints are **derived**, cached, and never stored as columns.
- Rating computation runs **after** the challenge window closes, not at match end — so it
  is a background job, not part of the request that receives the result.
- Rating history must be stored per match, so a rating can be **recomputed** if a result is
  later overturned. Without this, a reversed result cannot be properly undone.
- Both engines are pure functions over match outcomes — like the bracket engines
  ([022](022-tournament-formats.md)), they need no database or HTTP, so they are cheap to
  test exhaustively.

## The runnable check

1. A **bye** produces **no** rating change.
2. A `disputed` match produces **no** rating change until confirmed.
3. Beating a much weaker opponent yields a near-zero gain; losing to them is costly.
4. An inactive team's **rating is unchanged** while its **RD grows**.
5. Rating is **deterministic**: replaying the same match sequence yields identical ratings.

Point 5 is what makes recomputation-after-reversal trustworthy.
