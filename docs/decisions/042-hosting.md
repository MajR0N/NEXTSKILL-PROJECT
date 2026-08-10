# 042 — Hosting, CDN & object storage

**Status:** **decided**
**Decision:** Cloudflare (free) for DNS/CDN/DDoS · **Cloudflare R2** for demos · **small EU VPS**
(~€4–5/month) running the app and PostgreSQL together
**Date:** 2026-08-10
**Lens applied:** cloud-architect, devops-engineer
**Extends:** [017](017-deployment-and-cicd.md), [037](037-platform-hosting.md)

---

## What has to be hosted

| Piece | Needs |
| --- | --- |
| ASP.NET Core app | A host that runs .NET — **not shared PHP hosting** ([004](004-stack.md)) |
| PostgreSQL | Persistent disk, backups ([008](008-database.md)) |
| **Demo files** | Bulk storage, and **egress when people download them** ([024](024-demos.md)) |
| CDN / proxy | **The only real DDoS answer** ([040](040-threat-model.md) §8), and it must hide the origin IP |
| Region | **EU** — Polish audience, GDPR ([011](011-accounts-and-login.md)) |

---

## Object storage: Cloudflare R2, not Google Cloud Storage

The deciding factor is **egress**, because demos are downloaded, not just stored.

| | Cloudflare R2 | Google Cloud Storage |
| --- | --- | --- |
| Free tier | **10 GB storage**, 1M Class A + 10M Class B ops / month | 5 GB regional (US only) |
| Storage beyond free | $0.015 / GB / month | $0.020 / GB / month (Standard) |
| **Egress** | **$0 — always, no allowance to track** | **$0.085–0.12 / GB** |

> *Plain version:* **egress** is what you pay for data leaving the provider. Storing a demo
> is cheap everywhere; letting people *download* it is where the bill appears. R2 charges
> nothing for that, permanently — which is unusual and is the entire reason to pick it.

A worked case: 90-day retention ([024](024-demos.md)) keeps roughly 15–30 GB live. On GCS
that's a small storage bill plus a download bill that grows with popularity — **the more
successful a tournament is, the more it costs.** On R2 the download bill is zero, so cost
scales with *retention*, which we control, rather than with *interest*, which we want.

R2 also sits behind the same Cloudflare account as the CDN, so it's one fewer vendor.

**Decision: Cloudflare R2.** [024](024-demos.md) chose local disk first; R2 is the upgrade
path it named, and the serving endpoint is unchanged either way.

---

## CDN / proxy: Cloudflare free plan

Not optional — [040](040-threat-model.md) §8 states plainly that volumetric DDoS cannot be
solved in application code.

| Free plan gives | Why it matters here |
| --- | --- |
| Unmetered DDoS protection | The only real defence |
| **Origin IP hidden** | [040](040-threat-model.md) requires this; an exposed origin can be attacked directly, bypassing the proxy |
| Global CDN | Rankings and brackets are read-heavy and cacheable ([039](039-public-api.md)) |
| Free TLS, DNS | One less thing to run |

**Caveat to design around:** Cloudflare's proxy has request/response limits and idle
timeouts. **SSE streams** ([039](039-public-api.md)) must send periodic heartbeat comments
or the proxy will close them.

---

## App + database hosting: the honest analysis

Free tiers were investigated properly, because "free" is doing a lot of work in the question.

| Option | Reality in 2026 |
| --- | --- |
| **Azure App Service F1** | **60 CPU-minutes per day**, 1 GB RAM shared, no custom domain. Fine for a demo, **cannot serve a site people use** |
| **Oracle Cloud Always Free** | Headline 4 OCPU / 24 GB ARM, but **quietly cut to 2 OCPU / 12 GB**, provisioning is frequently unavailable, and new accounts are often flagged and put in indefinite review. Still the most generous free offer that exists |
| **Fly.io** | **Free tier retired.** Usage-based from the start, ~$5/month minimum |
| **Hetzner / Contabo VPS** | Not free. ~€4–5/month for a small EU VPS with predictable performance |

### The trap worth naming

**A free tier that can vanish is a poor foundation for something people rely on during a
live tournament.** Oracle can decline the account, reclaim capacity, or halve the allocation
again — all three have happened. If the platform is down on finals night, "it was free"
is not a satisfying explanation to an organiser.

ARM is *not* an objection: .NET builds for ARM64 fine, and the plugin runs on the
organiser's server, not ours ([007](007-server-hosting-model.md)).

### Recommendation

**Cloudflare (free) + R2 (free tier) + a small EU VPS (~€4–5/month) running the app and
Postgres together.**

That is roughly the price of one coffee per month, and it buys:

- predictable performance during events
- root access, so [008](008-database.md)'s PostgreSQL choice stays available
- one machine, one `docker compose`, matching [017](017-deployment-and-cicd.md)
- no account-review roulette before a tournament

**If free is a hard requirement**, the honest answer is **Oracle Cloud Always Free** — accept
the provisioning risk, take a region in the EU, and treat the VPS migration as a known
future step. It is genuinely the only free option that can serve real traffic.

> `ponytail:` one VPS running app + Postgres + a Cloudflare account. Ceiling: single host, so
> a host failure is an outage. Upgrade path: the app is stateless, so scaling out means
> adding instances behind the proxy — but back up first, because
> [017](017-deployment-and-cicd.md) is right that the database *is* the product.

---

## Free Postgres services — considered and rejected for now

Neon and Supabase have free tiers, but they sleep on inactivity, cap connections, and add a
network hop. Since the VPS already has a disk, running Postgres alongside the app is fewer
moving parts and keeps a result write, a bracket advance and a rating update inside **one
transaction** ([035](035-match-result-integrity.md)) with no cross-service latency.

Revisit only if managed backups become more valuable than simplicity.

## Decision

**Cloudflare free plan + R2 + a small EU VPS.** Roughly €4–5/month total.

| Layer | Choice |
| --- | --- |
| DNS, CDN, DDoS, TLS | Cloudflare free — **origin IP must stay private** ([040](040-threat-model.md) §8) |
| Demo storage | **Cloudflare R2** — zero egress is the whole argument ([024](024-demos.md)) |
| App + PostgreSQL | One small EU VPS, one `docker compose` ([017](017-deployment-and-cicd.md)) |
| Region | EU — Polish audience, GDPR ([011](011-accounts-and-login.md)) |

Free tiers were rejected deliberately, not overlooked. Azure F1 cannot serve a real site at
60 CPU-minutes/day; Fly.io's free tier is gone; Oracle Always Free was halved without
announcement and routinely fails to provision. **Paying about the price of one coffee a
month buys the platform being up on finals night**, which is the only uptime that matters
for a tournament product.

### VPS: Hetzner CAX11 (ARM), Falkenstein

| Option | Specs | Price | Verdict |
| --- | --- | --- | --- |
| **Hetzner CAX11 (ARM)** | 2 vCPU Ampere, 4 GB, 40 GB NVMe | **~$3.79/mo** | **Chosen.** Reported as the best compute value in the cloud market |
| Hetzner CX22 (x86) | 2 vCPU, 4 GB, 40 GB NVMe | ~$4.59/mo | Fallback if ARM bites. Outperforms Contabo's 4-core plan on every benchmark |
| Contabo VPS S | 4 cores, more RAM | $6.99/mo | More specs on paper, worse in practice — high disk latency, inconsistent CPU |
| OVH Warsaw | varies | ~€5+ | Only advantage is being physically in Poland |

**Latency is not a deciding factor, counter-intuitively.** OVH has a Warsaw datacentre;
Hetzner's nearest is Falkenstein (~20–30 ms to Poland vs ~5–10 ms). But Cloudflare caches
the read-heavy pages at the edge, matches run on the **organiser's** servers
([007](007-server-hosting-model.md)), and 20 ms is imperceptible on a page load. Paying more
for that difference buys nothing anyone can notice.

**ARM is both cheaper and faster here.** .NET builds ARM64 natively, PostgreSQL runs on ARM,
and the official images have ARM64 variants.

> ⚠️ **Hard requirement if ARM is chosen:** every image in the compose file must have an
> `arm64` tag. Verify before committing to it — discovering a missing tag after setup is
> avoidable annoyance.

40 GB of disk is ample because **demos live in R2**, not on the host — the disk carries the
OS, the app and PostgreSQL only.

### Domain: `.com`, registered at Porkbun

| | Choice | Why |
| --- | --- | --- |
| TLD | **`.com`** | Universal across pl/en/ru/de. `.pl` would signal Polish-only, contradicting four-locale support and the stated goal of reaching many communities |
| Registrar | **Porkbun** (~$11/yr, flat) | **Cloudflare Registrar cannot register new domains** — transfers only. Its ~$0.62/yr saving does not justify a transfer. Namecheap discounts year one and recovers it at $18.48 renewal |
| Defensive | `.pl` if cheap | So nobody else runs `nextskill.pl` in the home market |

**Non-negotiable at registration:** auto-renew ON, WHOIS privacy ON, registrar lock ON, and
**2FA on the registrar account**.

The registrar account is the **highest-value credential in the project — above the server.**
Whoever controls the domain can repoint it and obtain valid certificates for it. Losing the
server costs a rebuild; losing the domain costs the platform.

Procedure: [runbook §0b](../runbooks/hetzner-setup.md).

### Follow-ups this creates

- **SSE heartbeats are now mandatory** — Cloudflare's proxy closes idle connections, and
  [039](039-public-api.md) relies on long-lived streams
- Origin must reject traffic that did not come through Cloudflare, or the proxy is bypassable
- R2 credentials are secrets: encrypted at rest, never logged ([012](012-security.md))
- Backups go **off the host** ([017](017-deployment-and-cicd.md)) — a single VPS means a host
  failure is an outage, and an untested restore is a hope
