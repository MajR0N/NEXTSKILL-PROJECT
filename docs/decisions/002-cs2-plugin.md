# 002 — CS2 server plugin

**Status:** decided (revised 2026-08-10, supersedes an earlier "fork MatchZy" decision)
**Date:** 2026-08-10
**Decision:** **Write an original NEXTSKILL CS2 plugin from scratch** on CounterStrikeSharp.
Existing plugins are used as **inspiration and reference only** — no forking, no code reuse.

---

## Decision

| | |
| --- | --- |
| Plugin | **Original NEXTSKILL plugin.** Our code, our name, our architecture. |
| Framework | CounterStrikeSharp (C# / .NET 8 over Metamod) |
| MatchZy / MatchZy-Enhanced / FSHOST fork | **Reference material.** Read to understand the problem space. Not forked, not copied. |
| Get5 JSON schema | **Reference only.** We design our own config contract. |

This decision was made after the fork option was presented and explicitly reversed:
the project's goal includes owning and understanding every part of the system, and having
NEXTSKILL be original work end to end.

## The "inspiration, not derivation" boundary

This distinction is legal as well as stylistic, and it must be respected concretely.
MatchZy is MIT — copying with attribution *would* be permitted. We are choosing not to.

**Allowed:**
- Reading the source to understand *why* a problem is hard
- Learning that a problem exists at all (e.g. GOTV framebuffer flush timing)
- Observing the shape of a solution and then designing our own
- Comparing our behaviour against theirs to check we haven't missed a case

**Not allowed:**
- Copy-pasting source files or functions
- Reproducing their code structure verbatim and renaming it
- Vendoring their code and calling it ours

Practical rule: **read it, close it, then write ours.** If a file is open in one window
while ours is being typed in another, that is derivation, not inspiration.

## What we are taking on — the full cost, stated plainly

This is the section that matters. Writing this plugin ourselves means we own all of it.

### Tier 1 — straightforward

| Feature | Notes |
| --- | --- |
| Ready-up system | Per-player ready state, all-ready transition |
| Knife round + side swap | Winner chooses side |
| Score reporting to panel | HTTP POST out |
| Team/player assignment from config | Map SteamIDs to teams |

### Tier 2 — fiddly, many edge cases

| Feature | Why it bites |
| --- | --- |
| Map veto state machine | Legal-transition enforcement, timeouts, disconnect mid-veto, BO1 vs BO3 vs BO5 orderings |
| Pauses: tactical, technical, admin | Limits per team per half, who may unpause, resume countdown |
| Overtime | MR3/MR6 configurable, money reset, repeated OT periods |
| Coach slots | Spectator that isn't a spectator; movement restrictions |
| Forfeit / full-team disconnect | When does a match auto-end vs. wait |
| Disconnect + reconnect grace | Player drops at 8-7 — match pauses, waits, resumes, doesn't lose their stats |
| Series handling (BO3/BO5) | Map-to-map state carry, series score, next-map load |

### Tier 3 — genuinely hard, and the ones that ruin live events

| Feature | Why it is hard |
| --- | --- |
| **Round backup & restore** | Server crashes at 12–3. Without `mp_backup_round_file` capture *and* a working restore path, the match is unrecoverable and the tournament stalls. This is where homebrew match plugins most commonly die. It must be built and it must be *tested by actually crashing a server mid-match.* |
| **GOTV demo flush timing** | Stopping GOTV recording flushes the framebuffer to disk. Do it at the wrong moment and every player on the server lag-spikes. Correct behaviour is to wait until the full broadcast has finished before stopping. This is non-obvious and is only learned by hitting it. |
| **Per-round stat collection** | Correct kill/assist/death/utility attribution across round restarts, knife rounds, warmup, and OT without double-counting. Tedious and easy to get subtly wrong. |

### Tier 4 — permanent operational burden

CounterStrikeSharp is a .NET 8 scripting layer over a Metamod plugin that reads **raw
signatures and offsets out of the CS2 binary** via `gamedata.json`. Valve ships updates
that break these:

- [Server segfaults on map change after a CS2 update](https://github.com/roflmuffin/CounterStrikeSharp/issues/1139)
- [Plugins fail to load — undefined symbols in the game library](https://github.com/roflmuffin/CounterStrikeSharp/issues/1342)

**This risk is inherited by any CS2 plugin, ours included.** Writing our own does not
create the fragility — but it does mean there is no upstream maintainer absorbing it.
Every Valve update that breaks CSSharp is a NEXTSKILL outage until we fix it.

**Required:** an operational answer for "Valve shipped an update and every match is down."
At minimum — pinned CSSharp version, a staging server that takes updates first, and a
documented rollback. See decisions 015 and 031.

## Consequences

- **C# / .NET 8 is permanently in the stack.** Combined with Pawn/Small for CS 1.6
  ([001](001-game-scope.md)), this constrains the web-platform language choice — see 004.
- Timeline is materially longer than the fork path. Accepted deliberately.
- **Test strategy is now a hard requirement, not a nicety.** We do not get MatchZy-Enhanced's
  simulation mode for free, so we need our own way to exercise a full match without
  assembling ten humans. Bot-driven matches plus a replayable event fixture are the
  realistic minimum.
- The plugin is a real subproject with its own lifecycle, versioning, and release process —
  not an afterthought attached to the web app.

## Mandatory checks before this plugin is trusted with a real tournament

Per the project's engineering principles, non-trivial logic leaves a runnable check behind.
For this plugin specifically:

1. **Veto state machine** — illegal transitions rejected; BO1/BO3/BO5 orderings produce the
   correct final map set.
2. **Round backup/restore** — verified by killing the server process mid-match and restoring
   to the correct round with correct scores. Not by reading the code and believing it.
3. **Score reporting** — panel receives the same result the server displays, including OT.

## Open follow-ups

- Config contract design: our own schema (Get5 as reference only) — folds into decision 007.
- Plugin ↔ panel auth: shared secret / HMAC over the event payloads — decision 010.
- Whether the CS 1.6 plugin and the CS2 plugin should speak the *same* NEXTSKILL wire
  protocol. Tempting, but per [001](001-game-scope.md) do **not** design it until both
  exist concretely.
