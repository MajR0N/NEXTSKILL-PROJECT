# 050 — How a result becomes a rating

**Status:** PARTIALLY REOPENED — the result ledger and timing stand; rating eligibility was
amended by [044](044-visual-direction.md) on 2026-08-23
**Date:** 2026-08-11; eligibility amended 2026-08-23
**Relates to:** [007](007-server-hosting-model.md) (challenge window, BYO servers),
[010](010-identity-binding.md) / [011](011-accounts-and-login.md) (identity binding),
[023](023-bracket.md) (advancement), [025](025-rating-system.md) (ratings),
[029](029-profile-login-footer.md) (no fabricated history),
[034](034-test-strategy.md) (simulator), [035](035-match-result-integrity.md) (integrity),
[036](036-authorization.md) (who may decide), [045](045-teams-and-rosters.md) (fieldable roster),
[044](044-visual-direction.md) (rated/unrated event contract),
[046](046-tournament-lifecycle.md) (check-in), [049](049-team-page.md) (rating trend, trophies)

---

## Why this document exists

Phases 0–3 built the rating engines, the wire contract and the integrity rules, and every
one of them passes its checks. **None of them is connected to anything.** Before this
decision:

- nothing in the application ever wrote a `Rating` row — `db.Ratings` appeared only in
  `SELECT`s, so the ranking pages were "live data" over a table with no writer;
- `map_end` set a score and a status and never set `WinnerTeamId`, never called
  `BracketBuilder.AdvanceAsync` — whose only production caller was bye resolution;
- `PlayerBoundPayload` existed in the contract and the ingest handler ignored that event
  type, so nothing recorded who played;
- `organiser.trusted` had been in the schema since the first migration and affected nothing.

This document is the join. It is the largest single piece of Phase 4 and everything else in
the phase reads from it.

## 1. A match records who played, and how confidently

New table, one row per `(match, account)`.

| Column | Note |
| --- | --- |
| `MatchId`, `AccountId`, `TeamId` | |
| `Slot` | the `RosterSlot` they occupied ([045](045-teams-and-rosters.md)) |
| `Provenance` | `Declared` or `Bound` |
| `BoundAt` | null until proven |

**`Declared`** is written when the match loads, from the lineup the captain froze at
check-in. **`Bound`** replaces it when a `player_bound` event proves both factors — the
persistent `setinfo` token *and* the per-match code ([011](011-accounts-and-login.md)).

**A `Declared` row records history but does not, by itself, qualify a player or match for global
rating.** Rating now has two gates: the tournament or scrim was locked as **Rating affected**, and
complete participant/result/provenance data arrived through the NEXTSKILL plugin or an approved
owner-authorised integration. A missing bind or incomplete trusted feed leaves the rating pending
or produces an explicit audited unrated outcome; it never silently falls back to captain claims.

This accepts that an environment which cannot produce the required technical evidence does not
contribute to global NEXTSKILL rating. It may still keep the result and declared participant
history. Per-server metrics remain separate from global rating under [044](044-visual-direction.md).

> **This does not reopen [029](029-profile-login-footer.md).** That decision refused to
> infer an old match's participants from *current* team membership, and it was right: a
> roster read today says nothing about a roster last March. A `Declared` row is not an
> inference — it is a captain's attributable statement, frozen at a known moment, rendered
> as such. The provenance column is what keeps the difference visible instead of
> collapsing it.

## 2. The lineup is declared at check-in

`RosterRules.IsFieldable` admits **up to eight** people (5 players + 3 substitutes) for a
lineup of at most five. Check-in previously asserted only that *enough* fieldable bodies
existed; it never asked which.

So the check-in window ([046](046-tournament-lifecycle.md)) is also where the lineup is settled.
One lineup per tournament, copied into each match as it loads.

> **Amended 2026-08-11 to match 046's non-blocking check-in.** The lineup **defaults
> automatically** to the team's starting players, in slot then seniority order, capped at the
> format size. Layer 2 — a captain confirming explicitly — is *how you deviate from the default*,
> not a step required to have one. **No captain action is needed for a team to have a valid
> lineup.**

- **No picker is shown when there is nothing to pick.** Fieldable count equal to the format
  size auto-fills, and so does every team that is happy with its starters.
- **Rows are written at `match_loaded`, not at bracket generation.** [023](023-bracket.md)
  generates the whole bracket up front, so rounds 2+ have no teams yet and nothing to
  snapshot. Writing at load also means a match nobody ever played correctly has no
  participants.

**What was given up:** a team that substitutes someone in for the semifinal has a `Declared`
list that is wrong for that match until trusted evidence corrects it. The history may show that
attributed claim, but the match cannot affect global rating while the rated participant set is
incomplete. A per-match lineup editor remains rejected as speculative.

## 3. Rating changes are an append-only ledger

Required by [049](049-team-page.md): *"the trend comes from immutable post-result rating
changes, not from trying to reconstruct an old rating from the current aggregate."*

One row per settled `(match × subject)`, unique on `(MatchId, SubjectId, Game)`:

- **rated** rows carry before/after value, RD and volatility;
- **unrated** rows carry null deltas and the reason string `RatingPolicy.ExcludedBecause`
  already returns — *"bye — nobody played"*, *"integrity is Disputed — not confirmed"*,
  *"forfeit — measures attendance, not skill"*;
- `MatchId` is **nullable**, because [021](021-site-structure.md) promises a soft RD
  widening at season start, which is a rating change with no match behind it.

One table does four jobs, which is the tell that it is the right seam:

1. [049](049-team-page.md)'s trend chart reads it directly;
2. [025](025-rating-system.md)'s recomputation seam replays it;
3. **the unique index is the sweeper's idempotency** — a database constraint rather than
   application logic hoping to be right, the same trick [023](023-bracket.md) used for
   routing links;
4. *"why didn't this match move my rating?"* answers itself, which is why
   `ExcludedBecause` returns a reason rather than a bool.

## 4. What settles a rating, and when

[025](025-rating-system.md):169 already forbids rating at match end, so a match has two
distinct endings.

| Ending | Trigger | Effect |
| --- | --- | --- |
| **Result received** | `map_end` reconciles ([035](035-match-result-integrity.md)) | `WinnerTeamId` set, bracket advances if `confirmed` |
| **Result final** | challenge window closes, or both captains confirm | ledger rows written only when the event label and trusted-data gates both pass; otherwise pending or explicitly unrated |

A `BackgroundService` sweeps matches where `EndedAt + window < now AND RatedAt IS NULL`,
in **`(EndedAt, Id)` order** — Glicko-2 is rating-period based, so out-of-order application
gives different numbers. Same shape as `DiscordAnnouncementWorker`: claim one, do it, idle
delay. No scheduler, no new dependency.

`Match.RatedAt` is the query filter; the ledger's unique index is the safety net. Both,
deliberately — without the column the sweeper rescans every completed match forever.

## 5. Confirmation accelerates; dispute holds

Two human actions, one rule between them:

> **Human judgement holds the reversible thing and never the irreversible one.**

A rating lives in a ledger built to be recomputed. Teams already sitting in a server cannot
be un-sat. So both actions touch the rating and neither touches the bracket.

**Confirmation** (`Operation.ConfirmResult`, captain-only) closes the challenge window early
when both captains agree. Neither confirming is not a stall — the window expires and the
result is deemed accepted. **A trusted organiser skips the wait entirely**, which is what
`organiser.trusted` has been reserved for since the first migration. Forfeits and byes bypass
confirmation, because a team that did not show up cannot click a button.

Trust accelerates result finality only. It never bypasses the locked event label or complete
trusted-data gates in §1.

Shape: two nullable timestamps on `Match`. There are exactly two teams; a table for a
two-valued fact is scaffolding that never pays rent.

**Dispute** (`Operation.DisputeResult`, captain-only) is **not** `MatchIntegrity.Disputed`.
Integrity is a machine statement about whether we *received* the result correctly; a dispute
is a human claim that the result is *illegitimate*. Conflating them would let a captain
retroactively flip a machine-verified fact and make `Reconcile` non-deterministic on replay.
Separate fields: `DisputedAt`, `DisputedByAccountId`, `DisputeReason`.

A dispute pauses the sweep for that match and alerts the organiser. **It never un-advances
the bracket.** Blocking downstream advancement was rejected outright: it hands a losing
captain a lever to stall a Saturday-night bracket by clicking one button
([040](040-threat-model.md) §9).

**Referee reversal** ([036](036-authorization.md)) marks affected ledger rows superseded and
re-runs the sweep in the same deterministic order — including rows downstream of the
reversal, because reversing a semifinal changes who played the final.

**What was given up:** a wrong result can propagate one round before a human intervenes.
Reconciliation already catches the machine failure modes at `map_end`, so what is left is
match-fixing and roster fraud — where a referee was always going to be needed.

## 6. Scope: bind in, demos out

Both have no real producer until [Phase 6](../BUILD-PLAN.md). They are answered in opposite
directions by one test: *does building it now surface a design error that would otherwise
surface late?*

**Bind — yes, build it.** `Declared → Bound` is a state transition in a table designed this
phase. If the first real `player_bound` event arrives in Phase 6, that is when we discover
whether slot-scoped codes, provenance promotion and the participant key fit together.
[034](034-test-strategy.md) says the simulator covers *"flow, integrity, security and
identity"* — identity is currently the one word in that sentence with nothing behind it.
Leaving `Bound` unexercised would ship a provenance column with one reachable value, which
is indistinguishable from never having added it.

**Demos — no, defer to Phase 6.** [024](024-demos.md)'s upload shares no state with anything
Phase 4 designs, and CS 1.6 has no demo pipeline at all, so demos are CS2-only with no CS2
plugin yet. Hardening it early means guessing at headers, zip layout and size limits that
Phase 6 will learn for real. [012](012-security.md)'s zip-bomb and path-traversal checks stay
⊘ honestly and ship with the code they test. **The UI treats demos as nullable from the
start.**

## The runnable checks

Per the project's engineering principles, in `tests/NextSkill.Checks`:

1. **Sweep twice → identical ratings, no duplicate ledger rows.** The unique index, proven.
2. **Sweep in shuffled input order → identical ratings.** Determinism, which is what makes
   recomputation after a reversal trustworthy.
3. **A `disputed` match is never rated**, and its ledger row carries the reason.
4. **A captain dispute after advancement** → rating held, bracket unchanged.
5. **Both captains confirm** → an eligible rated match applies before the window would have closed.
6. **A trusted organiser's eligible result** rates without confirmation — but a `suspect` one
   from the same organiser still does not; organiser trust never bypasses eligibility.
7. **`player_bound` upgrades a `Declared` row to `Bound`** in the simulator, end to end,
   without creating a second participant row.
8. **A forfeited match** settles without waiting for a confirmation that can never arrive.
9. A **Rating not affected** event never writes rating ledger rows, even with a complete feed.
10. A **Rating affected** event with missing trusted participant or result evidence stays pending
    or closes explicitly unrated; it never rates from `Declared` rows alone.
