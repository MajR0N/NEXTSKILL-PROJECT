# 006 — Architecture & folder structure

**Status:** decided (revised 2026-08-10 — supersedes the earlier one-line "modular monolith"
default, which was under-explained)
**Date:** 2026-08-10
**Lenses applied:** architecture-designer, api-designer
**Decision:** **Vertical slice architecture — feature-driven folders — inside a single
deployable.** Not microservices.

---

## The confusion worth clearing up first

The pushback was: *"folder structure cannot be monolith — better to split into smaller,
feature-driven design, something like microservices. Easier to debug."*

**The goal is right. The mechanism doesn't have to be microservices**, because these are
two completely independent choices:

| Axis | Question | Options |
| --- | --- | --- |
| **Code organisation** | How are folders and modules arranged? | layered *(Controllers/, Services/, Repositories/)* ↔ **feature-driven** *(Matches/, Teams/, Brackets/)* |
| **Deployment topology** | How many separately-running programs? | **one deployable** ↔ many services over a network |

"Monolith" refers only to the **second** axis. A monolith organised by feature is not a big
ball of mud — it is a **modular monolith**, and its folder structure looks exactly like what
you asked for.

```
LAYERED MONOLITH              FEATURE MONOLITH             MICROSERVICES
(what you're rejecting)       (what we're building)        (what you named)

Controllers/                  Features/                    match-service/    (deploy 1)
  MatchController             Matches/                       + its own DB
  TeamController                CreateMatch/               team-service/     (deploy 2)
Services/                       GetMatchConfig/              + its own DB
  MatchService                  ReceiveEvent/              rating-service/   (deploy 3)
  TeamService                 Teams/                         + its own DB
Repositories/                   CreateTeam/                       |
  MatchRepository               InviteMember/              network between all
  TeamRepository              Brackets/

one change touches            one change touches           one change touches
3 folders                     1 folder                     1 service + 2 contracts
                                                           + a deploy dance
```

**You want the middle column.** So do I. We already agreed on it — I just called it by a
name that sounded like the left column.

---

## Why microservices would make debugging *harder*, not easier

Debuggability was the stated reason, so it deserves a direct answer.

| Situation | One deployable | Microservices |
| --- | --- | --- |
| Follow a request end to end | One stack trace. One breakpoint. | Distributed tracing needed to *see* the request at all |
| Step through a bug | Debugger walks straight through | Debugger stops at the network hop |
| Run it locally | `dotnet run` | Every service running, plus their databases |
| Reproduce a failure | Deterministic | Timing-dependent; may not reproduce |
| A wrong result appears | One database to inspect | Which service was wrong? Was a message lost? Retried twice? |

Microservices are a **scaling and team-organisation** tool: they let separate teams deploy
independently and scale parts differently. They buy that by **adding** network failure,
partial failure, eventual consistency, and distributed debugging. For one developer on one
product, that's paying a heavy tax for a benefit that doesn't apply.

### The decisive argument: [035](035-match-result-integrity.md) forbids it

Result integrity requires this to be atomic:

> record the result **AND** advance the bracket **AND** update ratings — or do none of it.

In one database that's **one transaction**. Across services with separate databases it is a
**distributed transaction**, which in practice means sagas with compensating actions:
"ratings failed, so now un-advance the bracket." That machinery is difficult to get right,
and getting it wrong produces exactly the corrupt-tournament failure that
[035](035-match-result-integrity.md) exists to prevent.

**Splitting matches, brackets and ratings into separate services would actively undermine a
decision already made.**

> `ponytail:` one deployable, one database, feature folders. Ceiling: if one feature ever
> genuinely needs independent scaling, the module boundary is already a clean seam — extract
> that one, keep the rest. Extraction from a well-modularised monolith is a normal
> refactor; premature distribution is not reversible.

---

## Decided structure

```
NEXTSKILL/
├─ src/
│  ├─ NextSkill.Api/                 ← the single deployable
│  │  ├─ Features/                   ← THE feature-driven part
│  │  │  ├─ Accounts/
│  │  │  │  ├─ Register/             ← endpoint + request + handler +
│  │  │  │  ├─ Login/                  validator, all in ONE folder
│  │  │  │  ├─ LinkSteam/
│  │  │  │  └─ RotateSetinfoToken/
│  │  │  ├─ Teams/
│  │  │  ├─ Tournaments/
│  │  │  ├─ Brackets/
│  │  │  ├─ Matches/
│  │  │  │  ├─ GetMatchConfig/       ← plugin pulls this
│  │  │  │  ├─ ReceiveEvent/         ← plugin pushes here
│  │  │  │  ├─ ReceiveDemo/
│  │  │  │  ├─ BindPlayer/           ← two-factor binding (011)
│  │  │  │  └─ ReconcileResult/      ← integrity gate (035)
│  │  │  ├─ Servers/
│  │  │  ├─ Ratings/
│  │  │  └─ Admin/
│  │  └─ Shared/                     ← genuinely cross-cutting ONLY
│  │     ├─ Persistence/
│  │     ├─ Auth/
│  │     └─ Tenancy/                 ← organiser scoping (037)
│  ├─ NextSkill.Contracts/           ← wire DTOs shared with the plugin (004)
│  ├─ NextSkill.Plugin/              ← CS2 plugin, C#/CounterStrikeSharp (002)
│  └─ NextSkill.Simulator/           ← speaks the wire contract (038)
├─ web/                              ← TypeScript frontend
├─ plugin16/                         ← CS 1.6 AMXX plugin, Pawn
└─ docs/decisions/
```

### The rules that keep it modular

1. **A feature folder holds everything for that use case** — endpoint, request/response
   types, handler, validator. Fixing "registration is broken" means opening one folder.
   This is the debuggability win, and it is real.
2. **Features do not call each other's internals.** Cross-feature work goes through a
   published interface or a domain event. This is what keeps extraction possible later.
3. **`Shared/` is for genuinely cross-cutting concerns only** — persistence, auth, tenancy.
   It is not a dumping ground; a `SharedService` that everything depends on recreates the
   layered mess by another name.
4. **`Contracts` holds wire DTOs only** — no business logic, no database entities
   ([004](004-stack.md)).
5. **Tenancy is enforced in one place**, not per query ([037](037-platform-hosting.md)) —
   a missing organiser scope is a data breach, so it must not rely on remembering.

> *Plain version:* **vertical slice** means organising by *what the code does for a user*
> rather than by *what kind of code it is*. A layered app scatters one feature across three
> folders; a sliced app keeps it in one. When something breaks, you open the folder named
> after the broken thing.

---

## Consequences

- **Positive:** one stack trace, one debugger, one `dotnet run`, one database, one
  transaction. Feature folders give the navigability that was actually being asked for.
- **Positive:** module boundaries are real, so extracting a service later is a normal
  refactor if it's ever genuinely needed.
- **Negative:** no independent scaling per feature. Irrelevant at present scale, and the
  seam exists if that changes.
- **Risk:** feature folders can still rot into a mess if rule 2 is ignored and features
  reach into each other. Discipline is required — but it is *less* discipline than
  distributed transactions would demand.

## Rejected

**Microservices.** Rejected because they would make debugging harder (the stated goal),
require distributed transactions that [035](035-match-result-integrity.md) cannot tolerate,
and solve a team-scaling problem that does not exist for one developer.
