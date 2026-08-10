# 035 — Match result integrity (resolving the silent failure)

**Status:** decided
**Date:** 2026-08-10
**Condition on:** [004 — stack](004-stack.md)

---

## The problem

The plugin↔panel seam is the highest-consequence interface in NEXTSKILL, and its failure
mode is **silent**:

> A wrong score is recorded during a live final. Nothing throws. Nothing 500s. The bracket
> advances the wrong team. Nobody notices until the trophy is handed to the wrong roster.

A shared compiled DTO library ([004](004-stack.md)) catches **shape** drift at compile time.
It does **not** catch semantic error. A perfectly-shaped payload carrying `score: 0`
because a field was renamed upstream compiles, deserialises, and validates fine.

Type safety is necessary. It is not sufficient. Integrity has to be engineered.

## Principle

**The panel never trusts a single event stream as the source of truth for a result.**

Events are *notifications*. The authoritative result is *reconciled* before it is allowed
to move a bracket.

## Scope limit — what this document does NOT solve

[007](007-server-hosting-model.md) established that game servers are **BYO: third-party
infrastructure running a plugin the organiser controls.**

Everything below defends against **accident** — dropped packets, contract drift, plugin
bugs, silent-zero scores. **None of it defends against a malicious organiser**, because
reconciliation compares the panel's view against the server's view, and under BYO the
organiser owns the server. They control both halves of the comparison.

Match-fixing is a **corroboration** problem, not a protocol problem, and is addressed in
[007](007-server-hosting-model.md#what-actually-mitigates-match-fixing): both-team result
confirmation, demo evidence (CS2), dispute flow, and a challenge window before ratings
settle.

**The server is a witness, not a judge.** Read this document with that limit in mind.

---

## The design — four layers, cheapest first

### Layer 1 — malformed becomes loud (a few lines)

| Rule | Mechanism |
| --- | --- |
| Missing fields are rejected, never defaulted | C# `required` members on all DTOs |
| Unknown fields are rejected, never ignored | `JsonUnmappedMemberHandling.Disallow` |
| **Scores are never nullable-with-default** | A missing score is a `400`, not a `0`. This single rule kills the most likely silent-zero bug. |
| Contract version is explicit | `X-NEXTSKILL-Contract: 1` header; unknown version → reject and log loudly |
| Payload authenticity | HMAC signature over the raw body — see decision 012 |

> `ponytail:` strict deserialisation is the whole of layer 1. Ceiling: it only catches
> *structural* error. Semantic error needs layer 3.

### Layer 2 — lost / duplicated / reordered becomes loud (a few lines)

Every event carries `matchId` and a **monotonically increasing `seq`**.

| Condition | Panel behaviour |
| --- | --- |
| `seq` gap detected | Mark match `integrity = suspect`, request replay from the plugin |
| `seq` repeated | Idempotent no-op — dedupe on `(matchId, seq)` |
| `seq` out of order | Buffer briefly, then treat as gap |

This also makes the system safe against the known SignalR/Redis property recorded in
[004](004-stack.md): **live push may silently drop messages, so it is never the record.**

### Layer 3 — reconciliation (the layer that actually fixes it)

**On map end and series end, the panel does not sum up what it saw.** It fetches the
authoritative final state from the plugin — a pull endpoint, or a complete final-state
block on the map-end payload — and compares it against the accumulated event history.

```
accumulated (from events)   vs   authoritative (pulled at map end)
        13 : 11                          13 : 11        -> clean,    advance
        13 : 11                          11 : 13        -> DISPUTED, do not advance
        13 : 11                          13 : 12        -> DISPUTED, do not advance
```

| Outcome | Match state | Bracket |
| --- | --- | --- |
| Match | `confirmed` | advances automatically |
| Mismatch | `disputed` | **blocked**, admin alerted |
| No authoritative response | `suspect` | **blocked**, admin alerted |

This is the layer that converts a silent wrong result into a loud, visible, blocking one.
It is also the layer that is genuinely worth the effort — layers 1 and 2 are cheap hygiene,
layer 3 is the actual safety net.

### Layer 4 — human gate where stakes are highest

- Group-stage / league matches: auto-advance on `confirmed`.
- **Elimination matches**: auto-advance on `confirmed`; require explicit admin confirmation
  on `suspect` or `disputed`.
- A bracket **never** advances from a `disputed` result without a recorded human decision,
  with the admin's identity and timestamp stored on the match.

## Explicitly rejected as insufficient

| Approach | Why not |
| --- | --- |
| "The shared DTO library solves it" | Catches shape only. The failure mode is semantic. |
| "Trust the last event" | Events can be lost, duplicated, or reordered. |
| "Trust the live WebSocket feed" | SignalR/Redis drops messages during a backplane outage without buffering. Live push is a *view*, never a record. |
| "Admin can fix it afterwards" | They cannot, if nobody knows it was wrong. The point is detection, not repair. |

## The runnable check (mandatory)

Per the project's engineering principles, this logic leaves one runnable check behind.
One test file, no framework, that replays a recorded event stream three times:

1. **with an event dropped** → asserts match lands `suspect` and the bracket does **not** advance
2. **with an event duplicated** → asserts the result is unchanged (idempotency holds)
3. **with a final-state mismatch injected** → asserts match lands `disputed` and the bracket
   does **not** advance

If bracket advancement can be made to happen in any of those three cases, the check fails.

## Consequences

- The plugin must expose an authoritative final-state pull, or include full final state on
  map-end. **This is a hard requirement on the plugin design in [002](002-cs2-plugin.md)** —
  and it applies equally to the CS 1.6 plugin.
- Matches carry an explicit `integrity` state (`confirmed` / `suspect` / `disputed`)
  alongside their result. This must exist in the very first schema migration.
- Bracket advancement is gated on integrity state, not merely on a result existing.
- A small amount of extra latency at map end. Irrelevant next to handing a trophy to the
  wrong team.
