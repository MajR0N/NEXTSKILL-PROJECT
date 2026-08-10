# 007 — Server hosting model

**Status:** decided
**Date:** 2026-08-10
**Decision:** **Organisers bring their own servers (BYO).** NEXTSKILL hosts no game servers.

---

## Decision

Community server owners register a server with NEXTSKILL — address, port, game, RCON
credentials — and NEXTSKILL allocates matches onto it. NEXTSKILL owns zero game servers.

This fits the stated audience: the low / community CS scene, where server owners already
have infrastructure and the thing they lack is tournament tooling.

| | |
| --- | --- |
| Hosting cost to NEXTSKILL | **Zero** — capacity scales with adoption, for free |
| Works for both games | Yes — CS 1.6 and CS2 register identically |
| Plugin version control | **None** — organisers install and update it themselves |
| Support burden | **Ours anyway** — "my server won't start a match" becomes our problem |

---

## The consequence that matters most: the trust boundary flips

**The game server is now untrusted infrastructure controlled by a third party.**

This is not a minor operational detail. It changes what
[035 — match result integrity](035-match-result-integrity.md) can and cannot promise:

| Threat | Reconciliation (035) defends? |
| --- | --- |
| Dropped / duplicated / reordered events | **Yes** |
| Contract drift, malformed payloads, silent-zero scores | **Yes** |
| Plugin bug producing a wrong score | **Yes** |
| **Organiser patches the plugin to report a fake score** | **No.** The "authoritative" source is the attacker. |

Reconciliation compares the panel's accumulated view against the server's authoritative
view. If the organiser controls the server, they control both halves of that comparison.
**A determined organiser can fabricate any result they like.** That is inherent to BYO and
cannot be engineered away at the protocol level.

### What actually mitigates match-fixing

Not cryptography — **corroboration**. Ranked by cost:

| Mitigation | Cost | Effectiveness |
| --- | --- | --- |
| **Both teams confirm the result** before the bracket advances | Low | High — an organiser fixing a result now needs the losing team's silence |
| **Demo is evidence** (CS2 only) — uploaded, retained, admin-reviewable | Low (already built) | High for disputes, useless for 1.6 |
| **Dispute flow** — either captain can flag a result; bracket blocks pending admin | Low | High |
| Server reputation / verified-organiser badge | Medium | Moderate, social |
| Cross-check server-reported scores against demo parse | Medium | High for CS2, and fully automatic |
| Rating changes held until a challenge window closes | Low | Limits blast radius |

**Design rule adopted:** for elimination matches, a result becomes `confirmed` only when
the server report reconciles **and** neither team has disputed within the challenge window.
The server is a *witness*, not a *judge*.

> `ponytail:` team confirmation is the cheap 90% of anti-fixing. Ceiling: it fails when
> both captains collude. Upgrade path: automatic demo-parse cross-check (CS2), which
> catches collusion but costs a parsing pipeline.

---

## Security consequence: we hold other people's RCON passwords

This is the single most sensitive data NEXTSKILL stores. An RCON password is **full remote
control of someone else's server.** A breach here is not "user emails leaked" — it is
"we handed attackers root on hundreds of community servers."

Non-negotiable requirements (detailed under decision 012):

| Rule | Why |
| --- | --- |
| **Encrypted at rest** with a key not stored in the database | DB dump alone must be useless |
| **Never returned by any API**, ever — not even to the owner | No "reveal password" button. They can only replace it. |
| **Never logged**, never in error messages, never in audit trails | Logs leak far more often than databases |
| **Redacted in the admin panel** — staff cannot read them | Insider risk, and it removes the temptation |
| Rotatable by the owner at any time | Breach response |
| Scoped as narrowly as the game allows | Blast radius |

**Preferred alternative where possible:** avoid holding RCON at all. If the plugin can
pull its config from us and push results to us, the panel may not need RCON for the normal
match lifecycle at all — only for exceptional control (force-restart, kick). If that proves
true, **not storing the credential is strictly better than storing it well.**
This must be validated during CS2 integration before the storage design is finalised.

---

## Operational consequences

### We do not control plugin versions

Organisers install and update the NEXTSKILL plugin themselves. Therefore:

- The plugin **must report its version** on every registration and match load.
- The panel **must refuse to allocate a match** to a server running an incompatible plugin
  version, with a clear message telling the organiser to update.
- The wire contract needs a version field from day one
  ([035](035-match-result-integrity.md) layer 1 already requires this).
- Breaking contract changes need a deprecation window, because we cannot force upgrades.

### Server verification is mandatory at registration

Without it, anyone can register a server they do not own — including someone else's.

**Challenge-response:** the panel issues a one-time token; the organiser proves control by
having the server echo it back through the plugin. Registration completes only on success.

### Health and readiness

Before a match is allocated: server reachable, correct game, correct map pool available,
compatible plugin version, not already hosting a live match. A failed check must produce a
diagnosis the organiser can act on — this is the support burden made cheap.

---

## Rejected alternatives

| Option | Why not |
| --- | --- |
| NEXTSKILL hosts a server pool | Real monthly cost and an ops burden from day one; turns the project into a game-server hosting business. Would have given us plugin-version control and a staging buffer against Valve updates — a genuine loss, accepted. |
| Both (BYO + managed pool) | Two code paths to build and secure before either is proven. Revisit as monetisation once BYO works. |

## Open follow-ups

- Whether RCON is needed at all for the normal match lifecycle — **validate during CS2
  integration.** Folds into decisions 012 and 013.
- Challenge window length before a result is final — needs a real organiser's opinion.
- Whether 1.6 results need stronger corroboration given no demo evidence exists
  ([001](001-game-scope.md)).
