# 014 — Discord integration

**Status:** decided
**Date:** 2026-08-10
**Decision:** **Outgoing webhooks only.** No bot.

---

## Decision

Organisers paste a Discord webhook URL into their server/tournament settings. NEXTSKILL
POSTs rich embeds into their channel. Nothing is hosted, nothing must stay online.

| Event | Posted |
| --- | --- |
| Registration opens / closes | Tournament name, format, team count, deadline |
| Match starting | Both teams, map, server, link to the lobby |
| Result confirmed | Score, map, link to the match page |
| **Dispute raised** | ⚠ Requires an organiser decision ([035](035-match-result-integrity.md)) |
| Bracket advanced / champion | Final standings |
| **Maintenance draining** | Warn organisers with a live match ([033](033-maintenance-mode.md)) |

## Why not a bot

> *Plain version:* a **webhook** is a URL Discord hands you — your app POSTs JSON and a
> message appears. A **bot** is a persistent WebSocket connection to Discord's gateway that
> must stay alive, which means a second always-on process alongside the app
> ([017](017-deployment-and-cicd.md)).

The failure mode is the real objection: **a bot dies quietly.** It stops notifying people
about their matches and nobody notices until someone no-shows a final. A webhook POST either
succeeds or returns an error we can log and retry.

**What we give up:** DMs to individuals (webhooks post to channels only) and interaction
inside Discord. That's accepted — and note the no-show problem is partly covered elsewhere:
the match lobby, email, and the channel post all exist.

> `ponytail:` webhook POST from the existing app. Ceiling: no DMs, no slash commands.
> Upgrade path: add a bot **only** if no-shows prove to be a real, measured problem that
> channel posts don't solve.

## Security

The webhook URL **is a credential** — anyone holding it can post into that channel as us.

| Rule | Reason |
| --- | --- |
| Stored encrypted, never returned by any API | Same class as RCON ([007](007-server-hosting-model.md), [012](012-security.md)) |
| Never logged | Logs leak more often than databases |
| Validated to `discord.com/api/webhooks/...` on save | **Otherwise it's an SSRF sink** — an organiser could point it at an internal address ([040](040-threat-model.md) §2) |
| Outbound request obeys the SSRF controls | Private ranges denied, no redirects followed |
| Failures retried with backoff, then surfaced in the panel | A silently broken webhook is worse than none |

The SSRF row matters: "let the user give us a URL we will POST to" is textbook SSRF, and it
arrives here disguised as a convenience feature.

## Presentation

The intended semantic colour treatment remains part of the visual-direction decision. While
[044](044-visual-direction.md) is reopened, embeds deliberately carry no colour rather than
spreading provisional palette choices into an external surface.

Posts always link back, and the linked page carries its own Open Graph card
([018](018-frontend-architecture.md)) — the post and the unfurl reinforce each other.

## Account linking is separate

Discord OAuth as an **optional account link** was already decided in
[011](011-accounts-and-login.md). It is unrelated to webhooks: linking is about identity,
webhooks are about announcements. Neither depends on the other.

## Built slice — 2026-08-11

The first event slice is live for **registration opening, match start, and confirmed results**.
Registration reopening has a monotonic occurrence number, while plugin event retries deduplicate
on the stable match id. A disputed or suspect result never enters the announcement outbox; an
audited scoreless forfeit does, because it is already a confirmed administrative result.

Delivery is a transactional PostgreSQL outbox: the lifecycle/result transaction commits the
event fact, not a network call. A background worker claims due rows with `FOR UPDATE SKIP LOCKED`,
retries six times with bounded backoff, and records success/failure for the tournament editor.
Removing the integration destroys the protected credential and abandons pending work.

The URL is protected with ASP.NET Data Protection, whose key ring is outside PostgreSQL. The save
boundary accepts only the exact HTTPS `discord.com/api/webhooks/{id}/{token}` shape and never
echoes a rejected value. Delivery uses a deliberately unlogged HTTP transport, follows no
redirects, and resolves the destination itself so loopback, private, link-local, reserved and
mixed public/private DNS answers are refused.

Rich embeds disable all allowed mentions and link back to NEXTSKILL. Until the lobby and match
detail surfaces exist, match start and result messages link to the tournament bracket rather than
to a route that would be dead. Registration-close, dispute, champion and maintenance messages
remain attached to their future owning slices.

Ten new runnable checks cover URL/DNS SSRF controls, protection/tamper failure, staff
authorization, non-echo and encryption at rest, transactional deduplication, safe embed output,
confirmed-versus-disputed behavior, retries and exception sanitization, forfeits, and removal.
