# 037 — Platform hosting & tenancy

**Status:** decided
**Date:** 2026-08-10
**Decision:** **NEXTSKILL is a single hosted platform** on our own domain
(`nextskill.com` or similar). It is **not** software that organisers install.

---

## Context

This was never stated explicitly, but it was implicitly assumed by several earlier
decisions — and it surfaced while resolving [008](008-database.md), where the question
"could hosting force MySQL on us?" only has an answer once you know **who runs the servers
the platform itself lives on.**

## Decision

| | |
| --- | --- |
| Deployment | **One instance, run by us**, on our own domain |
| Organisers | **Tenants** on that instance — they sign up, they don't install anything |
| Game servers | **Theirs** ([007](007-server-hosting-model.md)) — they connect *to* us over HTTP |
| Infrastructure control | **Ours** — we choose the OS, runtime, database, everything |

Note the split that makes NEXTSKILL work: **we host the platform, they host the game
servers.** Neither side installs the other's software; they meet over an HTTP contract.

```
        nextskill.com  (ours)                organisers' infrastructure
   ┌──────────────────────────┐            ┌───────────────────────────┐
   │  ASP.NET Core + Postgres │◄──HTTP────►│  CS2 server + our plugin  │
   │  one instance, many      │            │  CS 1.6 server + our      │
   │  organiser tenants       │            │  AMXX plugin              │
   └──────────────────────────┘            └───────────────────────────┘
```

## Consequences

### Settles the database question

No external hosting constraint exists. We are not limited to what shared hosting offers,
which is what made [008](008-database.md)'s PostgreSQL choice straightforward. ASP.NET Core
([004](004-stack.md)) already required a VPS or container host rather than shared PHP
hosting, so the infrastructure is ours to specify regardless.

### Makes this a multi-tenant system

Multiple organisers share one instance and one database. **Every query that touches
organiser-owned data must be scoped to that organiser.** A missing `WHERE organiser_id = ?`
is not a bug — it is a data breach, exposing one organiser's tournaments, rosters, or
server credentials to another.

This raises the importance of decision **036 (authorization)** considerably, and it must be
designed so that scoping is the *default*, not something each query remembers to do.

> *Plain version:* **multi-tenant** means many customers share one running system and one
> database, separated by a column rather than by walls. It's far cheaper to run than one
> deployment per customer, but the isolation is only as good as your query discipline —
> which is why it should be enforced in one place, not repeated in every query.

### Operational responsibilities are ours

Uptime, backups, TLS certificates, security patching, GDPR obligations
([011](011-accounts-and-login.md)), and the response when Valve breaks CounterStrikeSharp
([002](002-cs2-plugin.md)) all sit with us. Nobody else can fix a NEXTSKILL outage.

### A domain is a real dependency

`nextskill.com` (or whichever name) must be registered and controlled before launch.
It appears in Steam OpenID return URLs, OAuth redirect URIs, plugin callback URLs
configured on organisers' servers, and TLS certificates. **Changing it later means every
organiser must reconfigure their game servers** — so choose once.

## Rejected alternative

**Self-hosted / installable NEXTSKILL** (each organiser runs their own copy). Rejected:

- cross-organiser rankings, ratings and player profiles would be impossible — and those
  are core to the product
- support burden multiplies across every organiser's environment
- we could no longer choose the database, runtime, or upgrade schedule
- shipping updates would depend on organisers installing them, exactly the problem BYO
  servers already creates for plugins ([007](007-server-hosting-model.md))

## Open follow-ups

- Domain registration — not yet done.
- Hosting provider and region. EU region is strongly preferred given a Polish primary
  audience and GDPR ([011](011-accounts-and-login.md)).
- Tenancy model detail: are organisers hard-isolated, or do players/ratings deliberately
  span organisers? **Ratings spanning organisers is likely the whole point** — folds into
  decisions 025–027.
