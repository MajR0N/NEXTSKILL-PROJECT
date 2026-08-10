# 036 — Authorization & multi-tenancy

**Status:** decided
**Date:** 2026-08-10
**Lens applied:** secure-code-guardian
**Decision:** **Tenant scoping enforced in one place** (EF Core global query filters), plus
**resource-based authorization** for actions. Roles are never checked ad-hoc in endpoints.

---

## Why this is high-stakes

[037](037-platform-hosting.md) made NEXTSKILL multi-tenant: many organisers share one
instance and one database, separated by a **column**, not by walls.

> A missing `WHERE organiser_id = ?` is not a bug. It is a **data breach** — one organiser
> seeing another's tournaments, rosters, or server credentials.

The naive approach — remember to filter in every query — fails eventually and silently.
It only takes one endpoint written on a tired evening.

## The mechanism: make scoping the default, not a thing to remember

**EF Core global query filters.** Declared once per entity; applied automatically to every
query against that entity, forever.

```csharp
// Declared ONCE, in the DbContext
modelBuilder.Entity<Tournament>()
    .HasQueryFilter(t => t.OrganiserId == _tenant.CurrentOrganiserId);
```

From then on, `db.Tournaments.Where(t => t.Name == "Spring Cup")` **cannot** return another
organiser's tournament. Not because the developer remembered, but because forgetting is no
longer possible.

> *Plain version:* this is the difference between a rule written in a document ("always
> filter by organiser") and a rule enforced by the machine. Documented rules are followed
> until someone is in a hurry. Machine-enforced rules hold at 2am.

### The escape hatches — and how they're controlled

Global filters are not airtight. Three ways around them, each needing a deliberate control:

| Escape hatch | Control |
| --- | --- |
| `.IgnoreQueryFilters()` | **Banned by default.** Permitted only in explicitly reviewed admin queries, each with a comment explaining why |
| **Raw SQL / Dapper** | Filters do not apply. Raw SQL must scope manually and is a code-review trigger |
| Missing tenant context | The tenant resolver must **fail closed** — no tenant means no data, never "all data" |

That last one is the dangerous default. If `CurrentOrganiserId` is ever null and the filter
becomes a no-op, every query silently returns everything. **The resolver throws instead.**

---

## The role model

Three independent scopes. A person can hold roles in all three simultaneously.

```
PLATFORM              ORGANISER (per tenant)        TEAM (per team)
─────────             ──────────────────────        ───────────────
owner                 organiser_owner               captain
staff                 organiser_admin               member
support               referee
```

| Role | Can do | Explicitly cannot |
| --- | --- | --- |
| **owner** | Everything, incl. platform config, maintenance mode | — |
| **staff** | Moderate users, resolve cross-organiser disputes, view audit log | **Read RCON credentials or `setinfo` tokens** ([007](007-server-hosting-model.md), [011](011-accounts-and-login.md)) |
| **support** | Read-only diagnostics, server health | See personal data beyond what a ticket needs |
| **organiser_owner** | Full control of *their* organiser: tournaments, servers, staff | Touch another organiser's anything |
| **organiser_admin** | Run tournaments, register servers, manage brackets | Delete the organiser, manage billing |
| **referee** | Start/stop matches, resolve `disputed` results, force-advance a bracket | Change tournament structure |
| **captain** | Manage roster, accept invites, confirm results, dispute a result | Anything outside their team |
| **member** | Play, view, leave the team | Roster changes |

### Actions requiring a recorded human decision

Per [035](035-match-result-integrity.md), some actions must never be silent. These write an
**audit record with actor identity, timestamp, and reason**:

- advancing a bracket from a `disputed` or `suspect` result
- overriding a match score
- rotating or revoking another user's `setinfo` token
- registering or removing a server
- enabling maintenance mode
- any use of `.IgnoreQueryFilters()` in an admin path

## Actions, not roles, at the call site

Endpoints must never ask *"is this user an organiser_admin?"* — that scatters policy across
the codebase and makes changing it a search-and-replace.

They ask **"may this user perform this action on this resource?"**:

```csharp
// NOT: if (user.Role == "organiser_admin")
await _authz.AuthorizeAsync(user, tournament, Operations.ManageBracket);
```

The policy lives in one handler per operation. Changing who can advance a bracket is one
edit, not twelve.

> *Plain version:* **resource-based authorization** asks about *this specific object*, not
> just the user's job title. "Can Anna manage this bracket?" depends on which tournament it
> belongs to — a role alone can't answer that. Role checks scattered through endpoints are
> how someone eventually edits a tournament belonging to a different organiser.

---

## Cross-tenant by design: ratings and profiles

**Deliberate exception.** Player profiles, ratings and rankings
([025](025-rating-system.md)) are **global**, spanning organisers — that is the entire point
of a platform rather than isolated installs ([037](037-platform-hosting.md)).

So the tenancy rule is not "everything is scoped." It is:

| Data | Scope |
| --- | --- |
| Tournaments, brackets, servers, organiser staff, credentials | **Tenant-scoped** |
| Accounts, player/team ratings, rankings, match history | **Global** |

Global entities get no query filter — which means **their access rules must be explicit**,
since the safety net doesn't apply. Public data is public; personal data is not.

### Refinement (2026-08-10): matches are filtered by default, read publicly by exception

Building the ranking page exposed a tension in the table above. **Match history is listed as
global**, but matches are also the thing an organiser panel most needs protected — a leak
there exposes another organiser's fixtures, scores and server assignments.

**Implemented resolution: `Match` keeps its query filter.** Public cross-organiser reads go
through explicitly named methods that call `IgnoreQueryFilters()` with a comment, which is
exactly the escape hatch this document already permits.

Rationale: **stricter-by-default with visible exceptions beats looser-by-default with
invisible ones.** If matches were unfiltered, every future organiser-panel query would need
to remember to scope — and the whole point of the filter is that remembering is not required.
This way the risky direction (leaking) is closed by default, and the safe direction (public
reads) is opened deliberately, one call site at a time.

## Consequences

- The tenant resolver runs in middleware, before any handler, and **fails closed**.
- The role model must exist in the **first migration** — retrofitting authorization is
  notoriously painful ([008](008-database.md)).
- `Shared/Tenancy/` and `Shared/Auth/` are the only places this logic lives
  ([006](006-architecture.md)).
- Session cookies, not JWT ([011](011-accounts-and-login.md)), so a role change or ban takes
  effect immediately rather than at token expiry.

## The runnable check

The single most valuable test in the project:

1. Organiser A creates a tournament. Organiser B queries tournaments → **A's is absent**.
2. Organiser B requests A's tournament **by direct ID** → **404, not 403** (a 403 confirms
   it exists, which is itself a leak).
3. A request with **no tenant context** → **throws**, returns nothing. Never returns all.
4. `staff` requesting a server's RCON credential → **denied**, and the attempt is audited.
5. A `captain` attempting a roster change on another team → **denied**.
