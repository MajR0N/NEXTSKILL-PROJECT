# 033 — Maintenance mode

**Status:** decided
**Date:** 2026-08-10
**Decision:** **Degrade, don't stop.** Maintenance blocks *new* activity while in-flight
matches continue and their results are accepted.

---

## The thing that makes this non-obvious

**Matches don't run on our servers.** They run on organisers' infrastructure
([007](007-server-hosting-model.md)).

So when the panel goes down for maintenance, **matches keep playing.** Ten people are
mid-map on someone's server in Poland, entirely unaffected by our deploy. A naive
"maintenance mode = return 503 to everything" would:

- reject their `map_end` result,
- lose the demo upload,
- and strand a tournament mid-bracket.

### The saving grace, already designed

[009](009-api-contract.md) made the plugin responsible for delivery: **it retries with
backoff until the panel returns `200`.** So a short outage is survivable by construction —
events queue on the game server and drain afterwards.

That was designed for NAT and flaky connections. It doubles as the maintenance story.

## Decision: three levels, not a switch

| Level | Public site | New matches | In-flight events | Use |
| --- | --- | --- | --- | --- |
| **Normal** | ✅ | ✅ | ✅ | — |
| **Draining** | ✅ read-only | ❌ blocked | ✅ **accepted** | Before a deploy; wait for live matches to finish |
| **Full** | 503 + status page | ❌ | ⚠️ **rejected — plugins retry** | Emergency only, kept short |

**Draining is the one used for routine deploys.** It stops the situation getting worse
without breaking anything already running.

### Rules

- **Event, demo and binding endpoints stay open in `draining`.** They are the endpoints that
  cannot be retried indefinitely without cost.
- **New match starts are blocked**, so nothing new enters the danger window.
- The admin panel remains reachable — locking yourself out during an incident is a classic
  own goal.
- `Retry-After` is returned on every 503, so plugins back off sensibly rather than hammering.
- A **live-match count** must be visible in the admin panel, so "is it safe to deploy?" is a
  glance, not a guess.

> *Plain version:* **draining** is the pattern of refusing new work while letting existing
> work finish — the same idea as a shop locking the door at closing time but serving the
> customers already inside.

## Ordinary deploys don't need this

[017](017-deployment-and-cicd.md) deploys by starting a new container and switching over. A
few seconds of unavailability is invisible: browsers retry, and plugins retry by design.

**Maintenance mode is for schema migrations and incidents**, not for routine deploys. If it
gets used for every deploy, deploys are too disruptive and the deployment process is what
needs fixing.

## Consequences

- Maintenance state is **configuration**, readable without a database (a migration may be
  the reason it's on).
- The status page is **static and separately hosted**, so it survives the thing it reports on.
- Enabling maintenance is an audited action, restricted to `owner`
  ([036](036-authorization.md)).
- Organisers with a live match should be notified when `draining` begins — a Discord webhook
  is the obvious channel (decision 014).

## The runnable check

1. Enable `draining` → a `map_end` event is **accepted**; a new match start is **rejected**.
2. Enable `full` → the event is rejected with `Retry-After`, and the simulator
   ([038](038-build-order.md)) **retries and succeeds** once maintenance clears, with no
   duplicate result recorded.

Check 2 exercises idempotency ([035](035-match-result-integrity.md)) and the maintenance path
in one go.
