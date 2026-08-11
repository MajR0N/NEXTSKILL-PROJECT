# 034 — Test strategy

**Status:** decided
**Date:** 2026-08-10
**Lens applied:** test-master
**Decision:** **Four layers, weighted heavily toward pure-function tests.** The plugin
simulator ([038](038-build-order.md)) is the centrepiece, not an afterthought.


> **Status (2026-08-11): 169 checks passing**, no test framework, run with
> `dotnet run --project tests/NextSkill.Checks`. Pure checks for the engines, contract and
> policy; live-PostgreSQL checks for tenancy; in-process end-to-end runs for the plugin
> pipeline, accounts, teams, tournaments, brackets and two-factor.
>
> **Not covered yet:** anything needing a real game server, demo upload, or an email/SMS
> provider — and **"no secret appears in any log"**, which is testable today and is not tested.

---

## The problem this has to solve

A tournament match needs **ten players, two teams, a game server, and forty minutes.**
That cannot be a test. Without a way to exercise a full match cheaply, the riskiest paths in
NEXTSKILL would only ever be tested in production, during a real event, in front of people.

This is also why [002](002-cs2-plugin.md) noted the loss of MatchZy-Enhanced's simulation
mode — writing our own plugin means building our own way to test it.

## Layer 1 — pure functions (the bulk of the value)

The two engines are pure: given state and a result, return the next state. **No database,
no HTTP, no time, no I/O.**

| Engine | Tests |
| --- | --- |
| **Brackets** ([022](022-tournament-formats.md)) | 8 tests, incl. **the bracket reset** — the most commonly missing rule in tournament software |
| **Rating** ([025](025-rating-system.md)) | 5 tests: byes unrated, disputed unrated, anti-farm, RD grows while inactive, **determinism** |

Determinism matters beyond correctness: it's what makes recomputing ratings after a
reversed result trustworthy.

These are exhaustively testable because they're pure — no fixtures, no mocks, no setup.
**Write these first.** They're the cheapest tests in the project and they guard the two
things that silently ruin a tournament.

## Layer 2 — the plugin simulator (the centrepiece)

A real HTTP client speaking the real contract ([009](009-api-contract.md)) — **not a stub**
([038](038-build-order.md)).

It must be able to **misbehave on demand**:

| Misbehaviour | Asserts |
| --- | --- |
| Drop an event (`seq` gap) | Match → `suspect`, bracket **does not advance** ([035](035-match-result-integrity.md)) |
| Duplicate an event | Idempotent — result unchanged |
| Reorder events | Handled or flagged, never silently wrong |
| `map_end` score ≠ accumulated events | Match → `disputed`, bracket **blocked** |
| Wrong/absent HMAC | Rejected ([012](012-security.md)) |
| Replay an old signed request | Rejected on the timestamp window |
| Retry after a 503 | Succeeds later, **no duplicate result** ([033](033-maintenance-mode.md)) |
| Bind with a valid code but wrong `setinfo` token | Rejected ([011](011-accounts-and-login.md)) |

This single component covers match flow, integrity, security, identity binding and
maintenance behaviour. It is the highest-leverage thing to build in the whole test estate.

## Layer 3 — integration tests against a real Postgres

Not mocks. A throwaway containerised Postgres, so `jsonb`, partial indexes and global query
filters are exercised for real ([008](008-database.md)).

**The most important tests in the project live here** — tenancy
([036](036-authorization.md)):

1. Organiser B cannot see A's tournament in a list.
2. Organiser B requesting A's tournament by ID gets **404, not 403**.
3. **No tenant context → throws**, returns nothing.
4. `staff` cannot read RCON credentials or `setinfo` tokens.

Plus the security checks from [040](040-threat-model.md): SSRF rejection for private ranges,
open-redirect rejection, `ORDER BY` allowlist, decompression-bomb abort, path-traversal
filename handling.

## Layer 4 — browser E2E (Playwright), kept deliberately thin

Only for flows where **the UI itself is the risk**, not the logic beneath it:

- registration → login → `setinfo` token displayed once
- map veto: two clients, alternating picks/bans, correct final map set
- live bracket updates over SSE ([039](039-public-api.md))

E2E tests are slow and flaky in proportion to how many you write. These three earn it because
they're stateful, real-time, and multi-client — genuinely hard to verify any other way.

## What is deliberately not tested

Per CONTEXT §3 — laziness applies to tests too.

| Not tested | Why |
| --- | --- |
| Framework behaviour | EF Core and ASP.NET Core have their own tests |
| Simple CRUD with no branching | The integration layer already covers the risky ones |
| Every endpoint | Coverage percentage is not a goal; **risk coverage** is |
| Getters, DTO mapping, trivial one-liners | Nothing to break |

## What still requires a real server

No test replaces these — they belong to the verification backlog (CONTEXT §7):

- `setinfo` readable at connect time; behaviour on non-Steam clients; token length limits
  (**V2b, V3, V4**)
- **Round backup/restore, verified by actually killing a server mid-match**
  ([002](002-cs2-plugin.md)) — reading the code and believing it is not verification
- GOTV demo flush timing
- Behaviour after a Valve update breaks CounterStrikeSharp
  ([040](040-threat-model.md), CONTEXT §2.2)

## Order of construction

1. Bracket + rating tests — pure, cheap, highest silent-failure risk
2. Simulator — unlocks everything else
3. Tenancy tests — the breach class
4. Security checks from [040](040-threat-model.md)
5. Three E2E flows

No framework beyond the language default. No fixtures, no mocks, no per-function suites.
