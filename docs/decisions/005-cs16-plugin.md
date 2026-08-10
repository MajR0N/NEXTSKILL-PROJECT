# 005 — CS 1.6 plugin (AMXX / Pawn)

**Status:** decided
**Date:** 2026-08-10
**Decision:** Original AMXX plugin in Pawn. **Veto and side selection are not its job**
([028](028-veto-and-lobby.md)) — it receives a decided match and runs it.

---

## What it is not responsible for

Thanks to [028](028-veto-and-lobby.md), the hardest parts are gone before we start:

| Not in the plugin | Where it lives |
| --- | --- |
| Map veto | Web lobby |
| Side selection | Decided by the veto |
| Knife round | **Optional**, off by default ([015](015-admin-panel.md)) |
| Bracket, seeding, ratings | Panel |
| Identity | Panel issues the factors; plugin only verifies |

A veto state machine in Pawn would have been a large share of this subproject. It no longer
exists.

## What it must do

### Tier 1 — identity and access ([011](011-accounts-and-login.md))

| Feature | Mechanism |
| --- | --- |
| Read `setinfo` token at connect | `get_user_info()` — confirmed available (CONTEXT §2.6) |
| Accept the per-match link code | **`register_clientcmd`** — console, never chat. A code typed into chat is visible to opponents |
| Report the bind | `POST /matches/{id}/bind` ([009](009-api-contract.md)) |
| **Roster allowlist** | Unbound players are held in spectator. **Fails closed** — nobody plays until both factors verify |
| One session per account | Second concurrent bind rejected |
| Name lock | In-game name forced to the rostered name |

### Tier 2 — match flow

| Feature | Notes |
| --- | --- |
| Ready-up | Per player; match starts when all rostered players are bound and ready |
| Live score / round reporting | One event per round end, with `seq` ([035](035-match-result-integrity.md)) |
| Pauses | Tactical and technical, with limits per team |
| Overtime | MR3/MR6 configurable |
| Forfeit / full-team disconnect | Auto-end after a configurable grace |
| Disconnect + reconnect grace | Match pauses, waits, resumes; the player's stats survive |
| **Authoritative final state on map end** | Complete score and per-player stats, **retried until the panel returns 200** ([009](009-api-contract.md)) |

### Tier 3 — the hard part: round backup and restore

**CS 1.6 has no `mp_backup_round_file`.** The engine offers nothing here — unlike CS2, where
the primitive exists and only needs driving ([001](001-game-scope.md)).

What is realistically achievable in AMXX:

| Restorable | Not realistically restorable |
| --- | --- |
| Team scores | Exact economy per player |
| Round number and half | Weapons and equipment in hand |
| Which side each team is on | Grenade/armour state |
| Player→team assignment | Bomb/objective micro-state |

> `ponytail:` restore **score, side and round number**, then start a clean round with default
> buy. Ceiling: economy is lost, so a restored match resumes with a fresh buy round rather
> than the exact prior state. Upgrade path: per-player money snapshotting if a real
> tournament ever demands it — but a match that continues with a rebuy is vastly better than
> a match that dies.

**This must be verified by actually killing a server mid-match** ([034](034-test-strategy.md))
— not by reading the code and believing it.

### Tier 4 — transport

| Concern | Approach |
| --- | --- |
| HTTP out | [AmxxEasyHttp](https://github.com/Next21Team/AmxxCurl) (or AmxxCurl) — **non-blocking**, confirmed available (CONTEXT §2.4) |
| Delivery | **Retry until acknowledged**, queued on disk. Outbound only; never assume we can be dialled ([009](009-api-contract.md)) |
| Auth | HMAC over the raw body ([012](012-security.md)) |
| Contract version | Sent on every request; panel refuses incompatible versions ([007](007-server-hosting-model.md)) |

Blocking HTTP would stall the game server's main loop and lag every player — non-blocking is
not optional.

## No demos

CS 1.6 has no server-side demo pipeline ([001](001-game-scope.md), [024](024-demos.md)).
**The product must not promise them for 1.6**, and the UI must treat demos as genuinely
absent rather than missing.

This also weakens dispute resolution for 1.6, where there is no video evidence to appeal to
— corroboration falls back to team confirmation ([007](007-server-hosting-model.md)).

## Verification still outstanding

From CONTEXT §7, all must be answered on a real server before this is written:

| # | Question |
| --- | --- |
| V2b | Is `setinfo` readable **at connect time**, early enough to gate team joining? |
| V3 | Does it behave identically on **non-Steam clients** (Reunion/dproto)? |
| V4 | Does the info-key length limit permit a high-entropy token? |

**V3 is the one that could hurt.** Most active 1.6 servers admit non-Steam clients
([010](010-identity-binding.md)); if `setinfo` behaves differently there, the two-factor
binding needs rethinking for the majority of the audience.

## Build order

Per [001](001-game-scope.md) — **CS2 concretely first, then this concretely, then extract any
shared abstraction.** Not before. Two working implementations reveal the seam; imagination
does not.
