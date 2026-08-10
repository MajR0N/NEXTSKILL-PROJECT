# 001 — Game scope

**Status:** decided
**Date:** 2026-08-10
**Decision:** **CS2 and CS 1.6, both first-class from day one.** CS:GO is out of scope.

---

## Decision

| Game | Status | Integration path |
| --- | --- | --- |
| **CS2** | first-class, day one | Original NEXTSKILL plugin on CounterStrikeSharp (see [002](002-cs2-plugin.md)) |
| **CS 1.6** | first-class, day one | AMXX plugin written by us, in Pawn/Small |
| CS:GO | **out of scope** | Replaced by CS2 in Sept 2023; `-csgo_legacy` community population is negligible. A third integration path for near-zero users. |

## What this costs, stated plainly

The two games have **incompatible match lifecycles and wildly different capabilities**:

| Capability | CS2 | CS 1.6 |
| --- | --- | --- |
| Match plugin | **We write it.** C#, CounterStrikeSharp | **We write it.** Pawn/Small, from zero |
| Config delivery | We design the mechanism | We design the mechanism |
| HTTP out of server | Native to CSSharp/.NET | [AmxxCurl](https://github.com/Next21Team/AmxxCurl) / AmxxEasyHttp, non-blocking |
| Map veto | We build it | We build it |
| Knife / ready / pause / OT | We build it | We build it |
| Round backup & restore | `mp_backup_round_file` **exists as an engine feature** — we drive it | **No engine equivalent.** We build it or accept crashed matches are lost |
| Demo capture | **GOTV exists** — server-side, we drive it | **No equivalent.** Client-side `.dem` or ancient HLTV |
| Demo upload to panel | We build it, on top of GOTV | **Not possible server-side** |
| Per-round stats | We build it | We build it |

Note the distinction: CS2 gives us *engine primitives* (GOTV, round backup files) that we
still have to drive correctly. CS 1.6 does not have the primitives at all.

**Feature parity across the two games is not achievable at launch.** Demos in particular
are a CS2-only feature. The UI must not assume every match has a demo, and the product
must not promise it.

## The identified risk, and the mitigation

> Designing the abstraction *before* running either game end-to-end.
> Abstractions built on zero experience are usually wrong.

This risk was raised at decision time and accepted. Mitigation is mandatory:

1. **Build CS2 integration concretely first.** No interfaces, no adapters, no
   `IMatchRunner`. Hardcode it. Get one real CS2 match running end-to-end into the panel.
2. **Then build CS 1.6 integration concretely.** Also standalone. Also hardcoded.
   Get one real 1.6 match running end-to-end.
3. **Only then extract the shared interface** — from two working implementations, not
   from imagination. The seam will be obvious by that point and probably not where we'd
   have guessed.

Rule of two: do not abstract until there are two real cases in front of you.

## Schema consequence

Every game-touching table carries an explicit game discriminator from the first migration —
`matches.game`, `servers.game`, `demos.game`, and anything else that differs. Adding the
column later is a painful migration; adding it now is free.

Nullable/absent-capability fields (demos, per-round stats) must be genuinely nullable, not
defaulted to a lie.

## Language consequence

CS 1.6 forces **Pawn/Small** into the stack permanently. CS2's plugin forces **C#/.NET 8**
(CounterStrikeSharp). Neither is negotiable. This significantly narrows the sensible choice
of web-platform language — see decision **004**.
