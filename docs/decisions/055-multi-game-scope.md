# 055 — Multi-game scope, and the integration ladder

**Status:** decided — *deferred deliberately, with the ladder fixed*
**Date:** 2026-08-11
**Relates to:** [001](001-game-scope.md) (game scope), [002](002-cs2-plugin.md) / [005](005-cs16-plugin.md)
(plugins), [006](006-architecture.md) (rule of two), [008](008-database.md) (`jsonb` payload),
[009](009-api-contract.md) (receive, don't orchestrate), [022](022-tournament-formats.md) /
[025](025-rating-system.md) (game-agnostic engines),
[MARKETING-AND-SALES-STRATEGY](../MARKETING-AND-SALES-STRATEGY.md) §2

---

## The question

[001](001-game-scope.md) settled CS2 and CS 1.6, and ruled CS:GO out. It never considered games
outside Counter-Strike. League of Legends, Call of Duty, Clash Royale and Rocket League were
raised as future targets.

## Decision

**Recorded now, built later. No work in Phase 4–6, and nothing narrowed in the meantime.**

The architecture is already multi-game-ready and this decision exists mainly to keep it that way
on purpose rather than by luck:

- `Match.Game` is a discriminator present since the first migration ([001](001-game-scope.md));
- per-game variable data is `jsonb` with a GIN index, so a new game needs no schema migration
  ([008](008-database.md));
- the bracket and rating engines are **pure functions that never see a game**
  ([022](022-tournament-formats.md), [025](025-rating-system.md)).

**[006](006-architecture.md)'s rule of two is the binding constraint.** Build CS2 concretely, then
CS 1.6 concretely, *then* extract. Not one game runs end to end yet. Generalising across five
before finishing one is precisely the premature abstraction that rule exists to prevent.

## The integration ladder

Not all games are the same distance away. The distance is **whether results can be received rather
than typed**, which is the whole product.

| Tier | Games | How a result arrives | What NEXTSKILL is there |
| --- | --- | --- | --- |
| **A — plugin games** | CS 1.6, CS2, (CS:GO), other GoldSrc/Source | Our plugin, on the organiser's server | **The structural gap. The moat** |
| **B — official tournament API** | **League of Legends** | Riot Tournament API v5: register as provider, set a **results callback**, receive notifications | Viable — the same inversion of control [009](009-api-contract.md) is built on |
| **C — no integration path** | Call of Duty, Rocket League, Clash Royale | Typed in by a human | **Challonge with fewer features** |

### Why tier C is refused, not merely deferred

Three things break, and none is fixable by effort:

1. **There are no community servers.** You cannot host your own CoD, Rocket League or Clash Royale
   server. *"Your servers, your community, your rules"* — the entire positioning — is meaningless.
2. **The rating gap does not exist.** Riot has ranked, Rocket League has ranked, Clash Royale has
   trophies. Nobody there is unranked and unacknowledged the way a Polish CS 1.6 team is.
3. **The customer changes.** The go-to-market is named Polish CS server networks. A Rocket League
   organiser is not one, and nothing we know about the CS scene reaches them.

Meanwhile Toornament, Battlefy, start.gg and Challonge have served tier C for a decade. Entering
it means competing on breadth, without game integration, against incumbents — the one fight the
strategy already identifies as unwinnable.

**Every game added without an integration path dilutes the single sentence that makes the product
different.**

### If a non-CS game is ever added, it is League of Legends first

Riot's Tournament API is the only path on the table that preserves the model: register as a
provider, supply a callback URL, generate tournament codes, and **receive game-result
notifications**. That is the same shape as the plugin contract, so it plugs into the existing
ingest and integrity pipeline instead of bolting a manual-score path beside it.

Constraints to plan for, not discover:

- **A production application must be approved.** Personal keys cannot access the Tournament API.
  This is a lead time, not a sprint.
- One product per production key; a free tier for players is required.
- It is a **different customer** — a LoL organiser is not a server-network owner — so it is a
  go-to-market decision as much as an engineering one.

## The question that decides whether this is ever revisited

**Is multi-game a TAM play or a retention play?**

- **TAM** ("more games, more users") → the answer stays no, permanently. Four markets with no
  relationships, no integration and no gap.
- **Retention for the same customer** → if the Polish networks being courted are *gaming
  communities* rather than CS-only clubs, and already run LoL or Rocket League events, then
  serving them across games keeps them here instead of split between us and Toornament.

**This is answered by asking a partner, not by analysis.** The question for Pukawka: *do you run
events for anything other than Counter-Strike?* If yes, tier B becomes a roadmap item. If no, this
document stays closed.

## CS:GO stays out, pending a cheap check

[001](001-game-scope.md)'s objection was users, not cost — *"a third integration path for near-zero
users"* — and the marginal cost really is low, since Get5 runs on CS:GO and our wire contract
already mirrors the Get5 config format. The legacy branch also has effectively no anti-cheat
support, which makes rated CS:GO strictly worse than rated CS2 on the axis
[052](052-cheating-and-rating-integrity.md) cares about.

**Discharge the burden of proof cheaply before reopening:** count CS:GO-legacy servers with real
players in Poland on a monitoring site. Meaningful number → 001 gets reopened on evidence.
Otherwise it stays closed and this costs nothing.

## What was given up

- **Breadth, deliberately.** A general tournament platform is a larger market and a commodity one.
- **The option to be first in a non-CS niche** — accepted, because there is no non-CS niche where
  the product's actual advantage applies.
- **Rejected: adding games to grow the market.** Rejected: a manual-score path for any game, ever,
  since it would make "results are reported, not entered" false in our own product.
