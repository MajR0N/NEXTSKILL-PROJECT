# 039 — Public API (panel ↔ frontend)

**Status:** decided
**Date:** 2026-08-10
**Lens applied:** api-designer
**Companion to:** [009](009-api-contract.md), which covers the *plugin* contract. Different
consumers, different auth, different rules.

---

## Two APIs, deliberately separate

| | Plugin API ([009](009-api-contract.md)) | Public API (this doc) |
| --- | --- | --- |
| Consumer | Game servers | Browser frontend |
| Auth | HMAC per server | Session cookie + CSRF ([011](011-accounts-and-login.md)) |
| Shape | ~6 endpoints, event-driven | Many resources, CRUD-ish |
| Versioning | Strict — organisers run old plugins ([007](007-server-hosting-model.md)) | Looser — frontend deploys with the API |
| Breaking changes | Need a deprecation window | Ship together |

Keeping them separate matters because **the plugin API cannot be changed freely** and the
public API can. Merging them would drag the frontend's churn into a contract that must stay
stable for months.

## Why REST — and not GraphQL or gRPC

This applies to **both** APIs ([009](009-api-contract.md) and this one). Two of the three
options are eliminated by constraints already decided, not by preference.

### gRPC — eliminated by CS 1.6

gRPC is fast, strongly typed via protobuf, and excellent for service-to-service traffic.
It is **not usable here**, for two independent reasons:

1. **CS 1.6 cannot speak it.** The AMXX plugin's only outbound HTTP mechanism is
   [AmxxCurl / AmxxEasyHttp](https://github.com/Next21Team/AmxxCurl) — plain HTTP requests
   from Pawn ([001](001-game-scope.md)). There is no gRPC client for AMXX, and writing one
   is not a realistic project. **The plugin API must be plain HTTP or CS 1.6 is impossible.**
2. **There is no internal service boundary.** gRPC's primary use case is
   service-to-service calls. [006](006-architecture.md) chose one deployable — the calls
   gRPC would optimise are ordinary in-process method calls.

Browser support would also require gRPC-Web plus a proxy, losing HTTP caching and making
traffic unreadable in devtools.

### GraphQL — solves a problem we don't have

GraphQL's real benefit is **many different clients with different data needs**: a mobile app
wants three fields, a web app wants thirty, and you'd otherwise maintain endpoints for both.

NEXTSKILL has **one client**, which we build and deploy alongside the API. When a page needs
different data, we change the endpoint — no negotiation required.

What it would cost:

| Cost | Detail |
| --- | --- |
| **HTTP caching is lost** | Everything is a `POST` to `/graphql`. Rankings and brackets are highly cacheable — REST gets that from the protocol for free |
| **N+1 queries by default** | Resolvers hide database access; a nested query fans out into hundreds of queries unless DataLoader batching is added everywhere |
| **Authorization gets harder** | Must be enforced **per field**, not per endpoint — which fights the single-place enforcement in [036](036-authorization.md) and multiplies the chance of a tenancy leak |
| **Query cost analysis needed** | A public GraphQL endpoint lets anyone request a deeply nested query; you must add depth and complexity limits or you've built a DoS endpoint |
| Error handling | Returns `200 OK` with errors in the body, so RFC 7807 and normal HTTP semantics don't apply |

**The honest argument *for* GraphQL:** this data genuinely is graph-shaped. A profile page
wants account + ratings + recent matches + teams, which in REST is either several requests
or a purpose-built endpoint.

**Why that loses:** with one frontend, a purpose-built endpoint *is* the simpler answer. If
a page needs five things, we return those five things from one endpoint. That's a few lines
of C#, versus a resolver layer, batching infrastructure, per-field authorization, and query
cost limits.

> `ponytail:` REST plus purpose-built endpoints where a page needs aggregated data. Ceiling:
> if the frontend ever makes many round-trips per page, add aggregate endpoints — not
> GraphQL. Upgrade path: GraphQL becomes worth it only if a **second, independent client**
> appears (a public developer API, a mobile app someone else builds).

### REST — chosen

| Reason | |
| --- | --- |
| **Works for CS 1.6** | Plain HTTP from Pawn — the non-negotiable constraint |
| Free HTTP caching | Brackets, rankings and profiles are read-heavy and cacheable |
| Debuggable | Readable in browser devtools, `curl`, and server logs — matters when diagnosing an organiser's server at midnight |
| Large binary uploads | Demo upload is a plain body ([012](012-security.md)); natural in HTTP, awkward in GraphQL |
| Endpoint-level authorization | Matches [036](036-authorization.md)'s resource-based model directly |
| Tooling | OpenAPI 3.1 → NSwag → TypeScript types ([004](004-stack.md)) |

**One protocol for both APIs** also means the plugin simulator ([038](038-build-order.md))
and the frontend exercise the same stack.

## Conventions

| Rule | Choice |
| --- | --- |
| Naming | `snake_case` in JSON, consistently |
| Errors | RFC 7807 `application/problem+json`, stable `type` URIs, actionable `detail` |
| Collections | **Cursor pagination** — `?cursor=&limit=` |
| Versioning | `/api/v1/` |
| Timestamps | RFC 3339 UTC; the frontend localises ([008](008-database.md)) |
| IDs | Opaque public IDs, never raw sequential integers |
| Types | Generated for TypeScript by NSwag ([004](004-stack.md)) |

**Cursor, not offset, pagination.** With `?page=2&size=20`, inserting a row shifts everything
— a user paging through a leaderboard sees the same team twice, or misses one. A cursor says
"after *this* item", which stays correct while data changes underneath.

## Resource model

```
account ──< team_membership >── team ──< roster >── match ──> tournament ──> organiser
   │                              │                   │
   └──> rating (per game)         └──> rating         └──> demo, events, integrity
```

| Resource | Scope ([036](036-authorization.md)) |
| --- | --- |
| `accounts`, `ratings`, `rankings`, `matches` (history) | **Global** — cross-organiser by design |
| `tournaments`, `brackets`, `servers`, `organiser_staff` | **Tenant-scoped** |

## Endpoints by feature slice

Mirrors the folder structure in [006](006-architecture.md).

| Slice | Endpoints |
| --- | --- |
| **Accounts** | `POST /accounts`, `POST /sessions`, `DELETE /sessions`, `GET /me`, `POST /me/links/steam`, `POST /me/setinfo-token/rotate` |
| **Teams** | `GET/POST /teams`, `GET /teams/{id}`, `POST /teams/{id}/invites`, `DELETE /teams/{id}/members/{id}` |
| **Tournaments** | `GET/POST /tournaments`, `GET /tournaments/{id}`, `POST /tournaments/{id}/registrations` |
| **Brackets** | `GET /tournaments/{id}/bracket`, `POST /tournaments/{id}/bracket/advance` *(referee, audited)* |
| **Matches** | `GET /matches/{id}`, `POST /matches/{id}/confirm`, `POST /matches/{id}/dispute`, `GET /matches/{id}/lobby` |
| **Ratings** | `GET /rankings?game=&scope=`, `GET /accounts/{id}/rating` |
| **Demos** | `GET /matches/{id}/demo` *(authorised, streamed, never static)* ([012](012-security.md)) |
| **Admin** | Separate surface, see decision 015 |

### Never returned by any endpoint, at any role

RCON credentials, `setinfo` tokens, another player's link code, raw IP addresses, password
hashes ([007](007-server-hosting-model.md), [011](011-accounts-and-login.md),
[012](012-security.md)).

`GET /me/setinfo-token/rotate` returns a **new** token; there is no endpoint that reads the
existing one. If a user loses it, they rotate — they never retrieve.

---

## Real-time updates: Server-Sent Events, not WebSockets

Live bracket updates, the match lobby, and map veto all need the server to push state to the
browser.

**Decision: SSE (`text/event-stream`), consumed by the browser's native `EventSource`.**

### Why not WebSockets/SignalR

The requirement is **one-way**: server → client. Every *client* action — pick a map, ban a
map, ready up, confirm a result — is an ordinary `POST`. Nothing needs a persistent
bidirectional channel.

| | SSE | SignalR / WebSocket |
| --- | --- | --- |
| Direction needed here | ✅ one-way, exactly the requirement | Bidirectional — more than needed |
| Client library | **None** — `EventSource` is native | SignalR JS client |
| Reconnection | **Built in**, with `Last-Event-ID` replay | Manual or library-managed |
| Protocol | Plain HTTP — proxies, CDNs and corporate firewalls handle it | Upgrade handshake; occasionally blocked |
| Scale-out | Ordinary HTTP | Needs a **Redis backplane** ([004](004-stack.md)) |
| Message loss on backplane outage | N/A | **Silently drops messages** ([004](004-stack.md)) |

> *Plain version:* **SSE** is just an HTTP response that never finishes — the server keeps
> writing events down the same connection. The browser has `EventSource` built in, and it
> reconnects automatically if the connection drops, telling the server the last event it
> saw so nothing is missed. WebSockets are the right tool when the *client* also needs to
> stream continuously — chat, multiplayer input. Clicking "ban de_dust2" is a form
> submission, not a stream.

This also removes Redis from the near-term stack ([008](008-database.md)) — one fewer moving
part.

> `ponytail:` SSE, no Redis, no client library. Ceiling: SSE is one-way and browsers limit
> concurrent connections per domain (mitigated by HTTP/2). Upgrade path: SignalR if a
> genuinely bidirectional feature appears — in-browser voice coordination, live chat.

### The rule that keeps it safe

Consistent with [035](035-match-result-integrity.md): **the live stream is a view, never a
record.** The frontend must be able to recover full correct state by re-fetching over plain
REST. Nothing may exist only in a pushed event — if a client misses every event, a refresh
must still show the truth.

## Consequences

- The frontend needs no real-time library at all.
- Each SSE stream is authorised on connect and **scoped like any other request**
  ([036](036-authorization.md)) — a stream is not an authorization bypass.
- Long-lived connections need sensible server timeouts and heartbeat comments to keep
  proxies from closing them.
- An OpenAPI 3.1 spec is the deliverable at implementation time; it generates the TS client.
