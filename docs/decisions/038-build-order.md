# 038 — Build order

**Status:** decided
**Date:** 2026-08-10
**Decision:** **Web platform first**, with the game plugins stubbed by placeholders.
Real plugin integration follows.

---

## Decision

Build the NEXTSKILL web platform — accounts, teams, profiles, tournaments, brackets, admin
panel — before either game plugin exists. Plugins are represented by placeholders during
this phase.

**Why this fits:** it is almost entirely ordinary web development, which directly serves
the stated learning goal (CONTEXT §3a). It also produces visible, demoable progress early,
and it does not depend on standing up CS servers.

## The risk this carries

> Schema and API decisions get made **before** we know what the plugin actually sends.

This is a real risk and it was flagged when the choice was made. Web-first projects
routinely design a beautiful data model, then discover the integration produces data that
doesn't fit it — and by then the model has a dozen features built on top.

## The mitigation: the placeholder is a real simulator, not a stub

**Do not stub with hardcoded fake data.** Build a small **plugin simulator** — a script
that speaks the real HTTP contract against the real API:

- pulls a match config from `GET /api/match/:id`, exactly as a plugin would
- POSTs a realistic event sequence: connect, ready, veto, knife, round-by-round, map end
- signs its payloads with HMAC, like the real thing ([012](../decisions/012))
- carries `seq` numbers and a contract version ([035](035-match-result-integrity.md))
- can be told to misbehave: drop an event, duplicate one, report a mismatched final score

This costs little more than a fake, and it pays for itself three times over:

| It also serves as | Replacing |
| --- | --- |
| **The test harness** for match flow | decision **034** — "how do we exercise a full match without ten humans" |
| **The check required by [035](035-match-result-integrity.md)** — drop/duplicate/mismatch cases | A test that would have to be written anyway |
| **The contract specification**, executable rather than prose | Documentation that drifts |

> *Plain version:* a **stub** returns canned answers and teaches you nothing. A
> **simulator** plays the real role over the real protocol, so the day you swap in the
> actual plugin, the API has already been exercised by something that behaves like one.
> The gap between "works with the fake" and "works for real" shrinks to almost nothing.

## Sequencing consequence

The wire contract must be designed **early** — it is the first thing the simulator needs.
That is a good forcing function: it means the riskiest interface gets thought about first,
even though its implementation comes last.

Order:

1. Wire contract design (decision 009) — on paper
2. Simulator that speaks it
3. Web platform, developed against the simulator
4. Real CS2 plugin ([002](002-cs2-plugin.md)) — swap it in, contract already proven
5. Real CS 1.6 plugin — concretely, separately ([001](001-game-scope.md), rule of two)

## What must NOT be deferred despite web-first

Decisions already made that must land in the **first** schema migration regardless of
plugins existing yet — they are painful migrations later:

- `matches.game` ([001](001-game-scope.md))
- `matches.integrity` ([035](035-match-result-integrity.md))
- `organisers.trusted` (CONTEXT §3b)
- organiser scoping on every tenant-owned table ([037](037-platform-hosting.md))
- `timestamptz` everywhere, never naive timestamps ([008](008-database.md))

## Consequence for the verification backlog

CONTEXT §7 items **V2b, V3, V4** (setinfo connect timing, non-Steam client behaviour, token
length) and **V5, V6** (RCON necessity, CS2 authoritative final state) are **not blocked**
by this ordering — but they must be resolved **before** the corresponding plugin is
written, not after. Web-first is not a reason to leave them unverified indefinitely.
