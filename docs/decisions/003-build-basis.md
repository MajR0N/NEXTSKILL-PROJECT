# 003 — Build basis: what NEXTSKILL is structurally

**Status:** decided
**Date:** 2026-08-10
**Decision:** **Write the NEXTSKILL platform from scratch.** Do not fork
`matchzy-auto-tournament`.

---

## Context

[`matchzy-auto-tournament`](https://github.com/sivert-io/matchzy-auto-tournament) (MIT,
785 commits, actively maintained, TypeScript + React + Node + PostgreSQL + Docker) already
implements roughly 12 of the 27 originally-listed NEXTSKILL features:

- single elim, double elim, Swiss, round robin, shuffle brackets
  (via `brackets-manager.js` / `brackets-viewer.js`)
- FaceIT-style map veto for BO1/BO3/BO5
- OpenSkill-backed ELO with leaderboards
- automatic CS2 server allocation, match loading, bracket progression
- demo recording, public team pages, authentication
- WebSocket live updates for scores/connections/status

This was raised explicitly as an argument against building from scratch.

## Decision and rationale

The concern was raised and the decision reaffirmed: **build from scratch**, for two
stated reasons — integrating it *our* way, and learning the domain properly.

This is accepted as a product decision, not a technical oversight, and it is applied
consistently: [002](002-cs2-plugin.md) reaches the same conclusion for the CS2 plugin.
NEXTSKILL is original work end to end — platform, schema, integration model, and both
game plugins.

Existing projects are **read as reference, never forked or copied**. The boundary is
defined in [002](002-cs2-plugin.md#the-inspiration-not-derivation-boundary) and applies
project-wide.

## Scope boundary of "from scratch"

| Layer | From scratch? | Why |
| --- | --- | --- |
| NEXTSKILL web platform, schema, API, admin panel, CMS, CI/CD | **Yes** | This is the product. |
| CS2 server plugin | **Yes — original plugin** | See [002](002-cs2-plugin.md). Existing plugins are read as reference; the code is ours. |
| CS 1.6 server plugin | **Yes** | No prior art to lean on regardless. See [001](001-game-scope.md). |
| Third-party libraries (bracket math, rating math, etc.) | **Open — see below** | Needs an explicit call. |

`matchzy-auto-tournament` remains useful as a **reference to read, not code to inherit** —
particularly its schema design and its event flow between panel and game server.

## Non-negotiable consequence: bracket logic needs a test

Double-elimination loser-bracket routing is the classic source of tournament-ruining bugs,
and it **fails silently** — the wrong team advances and nobody notices until the final.
Round-robin tiebreakers and group→playoff transitions have the same property.

Therefore, per the project's engineering principles:

> Every non-trivial rule leaves one runnable check behind.

Bracket advancement gets a test before it gets a UI. Not a framework, not fixtures — one
runnable file that asserts a full 8-team and 16-team double-elim tournament resolves to the
correct winner given a fixed set of results, including the bracket-reset case where the
loser-bracket finalist beats the upper-bracket winner once.

The same rule applies to:

- rating/ELO calculation (deterministic given inputs — easy to assert)
- veto state machine (illegal transitions must be rejected)

## Trade-off accepted

Slower to a first live tournament, in exchange for full ownership of the mental model and
the ability to integrate CS 1.6, the admin panel/CMS, and the CI/CD dev panel on our own
terms — none of which `matchzy-auto-tournament` covers.

## Open follow-up

Whether "from scratch" excludes third-party npm libraries for solved math
(`brackets-manager`, `openskill`) or only excludes forking a whole platform.
Tracked as decision **032**.
