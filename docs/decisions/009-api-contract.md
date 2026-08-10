# 009 — API contract (panel ↔ game server)

**Status:** decided
**Date:** 2026-08-10
**Lens applied:** api-designer
**Decision:** REST over HTTPS, HMAC-signed, versioned, **plugin-initiated in every
direction.** The plugin owns delivery and retries until acknowledged.

---

## ⚠ A problem this design uncovered in [035](035-match-result-integrity.md)

[035](035-match-result-integrity.md) layer 3 says:

> On map end, the panel **pulls** authoritative final state from the plugin.

**That cannot work.** Game servers sit on organisers' infrastructure
([007](007-server-hosting-model.md)) — often behind NAT, firewalls, or a hosting panel with
no inbound HTTP. **We cannot rely on connecting *to* them at all.**

### The fix: invert it, and make delivery the plugin's job

| Old (broken) | New |
| --- | --- |
| Panel pulls final state | **Plugin pushes full authoritative final state** as part of the `map_end` event |
| Panel retries the pull | **Plugin retries the push until the panel returns `200`** |
| Requires inbound connectivity | Requires **outbound only** — works behind any NAT |

Reconciliation still happens exactly as designed — the panel compares its accumulated
event history against the authoritative block — but both halves now arrive in one
plugin-initiated message.

**Delivery responsibility sits with the plugin.** It holds the final state on disk and keeps
retrying with backoff until acknowledged. A panel outage or deploy delays reconciliation;
it never loses a result.

> *Plain version:* **NAT** means most machines can dial out but can't be dialled. Designing
> a system that needs to call *in* to someone else's server is how integrations die in
> practice — the code works, and then the first real organiser's firewall blocks it.
> Outbound-only is the design that survives contact with reality.

**Bonus:** this makes [007](007-server-hosting-model.md)'s open question moot in the
common case. If the plugin drives everything outbound, **RCON is not needed for the normal
match lifecycle** — and not storing a credential beats storing it well. Verification
item **V5** is now expected to resolve favourably.

---

## Design rules

| Rule | Choice |
| --- | --- |
| Style | **REST over HTTPS.** gRPC and GraphQL evaluated and rejected — see [039](039-public-api.md#why-rest--and-not-graphql-or-grpc). Decisive point: **CS 1.6's AMXX plugin can only make plain HTTP requests**, so gRPC would make CS 1.6 impossible |
| Naming | `snake_case` in JSON, everywhere, no exceptions |
| Versioning | URL path: `/api/v1/...` **and** an `X-NextSkill-Contract` header ([035](035-match-result-integrity.md)) |
| Errors | RFC 7807 `application/problem+json` with stable `type` URIs |
| Auth | HMAC-SHA256 over the raw body + timestamp; per-server secret |
| Idempotency | `(match_id, seq)` — replays are safe no-ops |
| Timestamps | RFC 3339, UTC, always ([008](008-database.md)) |
| Unknown fields | **Rejected**, never ignored ([035](035-match-result-integrity.md)) |

### Why HMAC and not a bearer token

> *Plain version:* a **bearer token** is like a ticket — whoever holds it gets in, so a
> leak is total compromise and the token travels in every request. An **HMAC signature**
> proves the sender knows a shared secret *without sending it*, and because the signature
> covers the body, it also proves the message wasn't altered in transit. Including a
> timestamp stops someone replaying a captured request later.

---

## Endpoints

All plugin-facing endpoints live under `/api/v1/servers/{server_id}/`.

| Method | Path | Direction | Purpose |
| --- | --- | --- | --- |
| `GET` | `/matches/{match_id}/config` | plugin pulls | Match setup: teams, rosters, map pool, veto rules, series format |
| `POST` | `/matches/{match_id}/events` | plugin pushes | All lifecycle events; `map_end` carries authoritative final state |
| `POST` | `/matches/{match_id}/demo` | plugin pushes | Zipped demo upload (CS2 only — [001](001-game-scope.md)) |
| `POST` | `/matches/{match_id}/bind` | plugin pushes | Two-factor player binding ([011](011-accounts-and-login.md)) |
| `POST` | `/heartbeat` | plugin pushes | Liveness, plugin version, current match state |

Note there is **no endpoint the panel calls on the plugin.** That is the point.

### Event envelope

Every event shares one envelope, which is what makes sequencing and idempotency uniform:

```json
{
  "contract": 1,
  "match_id": "01J8X…",
  "seq": 42,
  "sent_at": "2026-08-10T18:22:31Z",
  "type": "round_end",
  "payload": { }
}
```

| Field | Rule |
| --- | --- |
| `seq` | Monotonic per match. Gap → match marked `suspect`. Repeat → no-op ([035](035-match-result-integrity.md)) |
| `contract` | Unknown value → **reject loudly**, never best-effort parse |
| `payload` | Shape determined by `type`; **`required` members, unknown fields rejected** |

### `map_end` — the integrity-critical one

Carries the **complete authoritative final state**, not a delta:

```json
{
  "type": "map_end",
  "payload": {
    "map": "de_dust2",
    "map_number": 0,
    "final_score": { "team_a": 13, "team_b": 11 },
    "rounds_played": 24,
    "players": [ { "slot": "a1", "account_id": "…", "kills": 22, "deaths": 17 } ],
    "authoritative": true
  }
}
```

The panel compares this against its accumulated events. Match → `confirmed`. Mismatch →
`disputed`, bracket blocked, admin alerted ([035](035-match-result-integrity.md)).

---

## Player-facing API (panel ↔ frontend)

Separate concern, different rules — session cookie auth ([011](011-accounts-and-login.md)),
not HMAC. Standard REST under `/api/v1/`, organised by the same feature slices as
[006](006-architecture.md). Cursor pagination on every collection. TypeScript types
generated by NSwag ([004](004-stack.md)).

**Never exposed by any endpoint, at any level:** RCON credentials
([007](007-server-hosting-model.md)), `setinfo` tokens, another player's link code, raw IPs
([011](011-accounts-and-login.md)).

---

## Consequences

- The **simulator** ([038](038-build-order.md)) implements exactly this contract — including
  the misbehaviours: dropped `seq`, duplicate `seq`, and a `map_end` whose authoritative
  score disagrees with the events.
- Contract changes need a deprecation window; organisers' plugin versions are outside our
  control ([007](007-server-hosting-model.md)).
- **[035](035-match-result-integrity.md) must be amended** — its layer-3 "panel pulls"
  wording is superseded by plugin-push-with-retry.
- An OpenAPI 3.1 spec is the deliverable when implementation starts; it generates both the
  TS client and the simulator's expectations.
