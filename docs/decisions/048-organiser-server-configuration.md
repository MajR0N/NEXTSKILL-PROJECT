# 048 — Organiser-configured servers, plugins and match config

**Status:** 🔴 **OPEN — captured, not designed**
**Date opened:** 2026-08-11
**Extends:** [007](007-server-hosting-model.md) (BYO servers), [009](009-api-contract.md) (wire
contract), [002](002-cs2-plugin.md) / [005](005-cs16-plugin.md) (the plugins),
[046](046-tournament-lifecycle.md) (tournaments)

> **Implementation update, 2026-08-12:** the registration half is closed by
> [051 §2](051-phase-4-remainder.md#2-we-never-dial-a-game-server): authorised organisers can
> register `host:port`, receive the signing secret once, and the first valid plugin heartbeat
> verifies the server. This ADR remains open for the plugin-list, event configuration and server
> allocation questions below.

---

## The requirement, as stated

> NEXTSKILL allows organisers to configure the tournament with **their own servers** — which
> includes connection, **plugin list** (we will be building more than one), server config, and
> so on.

Two parts of that are new, and one of them changes the shape of things already built.

**Already assumed:** organisers bring their own servers ([007](007-server-hosting-model.md)).
A `servers` table exists, tenant-scoped, with host, port, game, verified flag, HMAC secret and
plugin version.

**New:** there will be **more than one plugin**, a tournament carries **its own server and
config**, and the organiser configures that per event rather than per server.

---

## Why this needs grilling before it is built

### 1. `plugin_version` assumes exactly one plugin

`servers.plugin_version` is a single string, and [007](007-server-hosting-model.md) uses it to
refuse allocating a match to an incompatible server. With a plugin *list*, the question becomes
"does this server run all the plugins this tournament needs, at versions it accepts?" — which
is a compatibility matrix, not a string comparison.

### 2. Config lives at three levels, and nobody has said which wins

```
server   →  what this machine can do        (game, plugins installed, capacity)
tournament → what this event requires       (plugins, cvars, map pool, overtime rules)
match    →  what this fixture needs         (map, teams, sides)
```

[009](009-api-contract.md) already serves a per-match config document. Where do tournament-level
settings merge into it, and what happens when a tournament demands something the server does
not have?

### 3. Server allocation is currently manual and implicit

A match points at a `server_id`. Nothing chooses it. With per-tournament servers, an organiser
expects to say "this event runs on these three servers" and have fixtures allocated
automatically — with the one-live-match-per-server rule ([007](007-server-hosting-model.md))
already enforced by a partial unique index.

### 4. "Server config" is a security surface, not a settings form

[040](040-threat-model.md) already names server registration as the **top SSRF risk**. Letting
organisers supply arbitrary cvars or config fragments adds a second one: config injection into
whatever the plugin executes. An allowlist of settings is very different work from a free-text
box, and the difference has to be decided before either is built.

### 5. Secrets stay unreadable

[012](012-security.md) and `Operation.ReadSecret` are unconditional: the panel shows **status**,
never secrets. Whatever shape server configuration takes, RCON passwords and HMAC secrets are
write-only and rotatable, never displayed — including to the organiser who typed them.

> **Updated by [051](051-phase-4-remainder.md) §2:** the normal product never stores an RCON
> password or dials the game server. The write-only rule above remains a boundary for any future
> secret type; it is not permission to add RCON quietly.

---

## Questions to answer

1. What is a **plugin** here — a name and version range, or something we distribute and version
   ourselves?
2. Does a server **declare** its plugins on heartbeat, or does the organiser assert them and we
   verify on connect? (Declaring is the only one that cannot be wrong.)
3. What happens when a tournament requires a plugin a server does not report? Refuse allocation,
   warn, or block the tournament from starting?
4. Which settings are organiser-controllable, as an **allowlist**? Map pool, overtime, rounds,
   knife round, pause rules — where does it stop?
5. Are servers attached to a **tournament**, or pooled per organiser and allocated per match?
6. Can two tournaments share a server? The one-live-match-per-server index says yes over time,
   never at once — is that the intended answer?
7. What does an organiser see when a server is misconfigured, and **before** the event rather
   than at match time? ([007](007-server-hosting-model.md) wants failures found early.)
8. How does any of this degrade for **CS 1.6**, where the plugin is AMXX and capabilities differ
   sharply from CS2 ([001](001-game-scope.md))?

---

## What this does not change

- **Plugin-initiated in every direction.** NAT makes panel→server calls unreliable, so the
  plugin still pulls config and pushes events ([009](009-api-contract.md)).
- **Servers stay tenant-scoped.** One organiser's servers are invisible to another
  ([036](036-authorization.md), [037](037-platform-hosting.md)).
- **Verification before use.** A server proves itself by challenge-response before it can carry
  a match ([007](007-server-hosting-model.md)).

## This is a sales argument, not only a feature

Cross-links to [MARKETING-AND-SALES-STRATEGY](../MARKETING-AND-SALES-STRATEGY.md), because
"bring your own servers" is one of the few things NEXTSKILL can say that the incumbents cannot.

| What an organiser hears | Why it lands |
| --- | --- |
| **Your servers, your community, your rules** | They already own the machines and the players. A platform that rents them capacity is asking them to pay for something they have |
| **No per-slot or per-server fee** | Our hosting cost does not scale with their events, so the pricing question stays open rather than being forced ([MARKETING](../MARKETING-AND-SALES-STRATEGY.md)) |
| **Configure the event, not the box** | The pitch is that we replace the spreadsheet and the manual server juggling, not the server |
| **Plugins are ours and versioned** | More than one plugin is a *product surface*: it is the thing an organiser cannot assemble themselves in an evening |

**The risk to name in the same breath:** BYO servers is also what makes support hard. When a
match fails on someone else's machine, the organiser blames the platform. That is a
*marketing* cost as much as an engineering one, and it argues for the "find failures before the
event" position [007](007-server-hosting-model.md) already takes.

---

## Sequencing

This belongs with the **organiser panel** slice, and it should be grilled immediately before
that slice rather than now — the surrounding pieces (matches, veto, allocation) are still
unbuilt, and answering these questions in a vacuum is how [019](019-visual-identity.md)
happened.
