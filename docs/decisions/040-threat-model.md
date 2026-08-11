# 040 — Threat model

**Status:** decided
**Date:** 2026-08-10
**Lenses applied:** security-reviewer, secure-code-guardian
**Companion to:** [012](012-security.md) (controls), [036](036-authorization.md) (authz)

Threat-by-threat. Each entry states what it is in plain terms, **how it applies to NEXTSKILL
specifically**, and the control. Severity is judged for *this* system, not in the abstract.

---

## Attack surface

| Untrusted input | Source |
| --- | --- |
| Plugin events, `map_end` results, bindings | Organisers' game servers ([007](007-server-hosting-model.md)) — third-party infrastructure |
| **Demo uploads** | Same, large zip files |
| **Server registration** — address, port, credentials | Organisers |
| User-generated content — nicknames, team names, tournament descriptions | Any registered user |
| OAuth callbacks | Steam OpenID, Discord ([011](011-accounts-and-login.md)) |
| Everything from the browser | Public internet |

---

## 1. SQL injection — **Low residual, one real vector**

> *Plain version:* an attacker puts SQL fragments into a form field, and if the app pastes
> that text straight into a query, the database executes it. `'; DROP TABLE users; --`

**Mostly solved by construction:** EF Core parameterises everything ([004](004-stack.md)).
Ordinary LINQ cannot be injected.

**The vectors that survive an ORM — these are where it actually happens:**

| Vector | NEXTSKILL relevance | Control |
| --- | --- | --- |
| **Dynamic `ORDER BY`** from user input | **Real.** Leaderboards, rankings and match lists all need user-selectable sorting, and column names *cannot* be parameterised | **Allowlist** of sortable columns mapped from a fixed enum. Never interpolate a column name |
| Raw SQL / Dapper | Permitted for complex reporting queries | Parameterise always; **plus manual tenant scoping** — global query filters don't apply ([036](036-authorization.md)) |
| `LIKE` patterns from search boxes | Player/team search | Escape `%` and `_`; cap pattern length |
| `jsonb` path expressions | Per-game payload queries ([008](008-database.md)) | Never build a path from raw user input |

**Severity: Low** if the allowlist rule holds, **High** the day someone interpolates a sort
column. That one line is the whole risk.

## 2. SSRF (Server-Side Request Forgery) — **High, and designed into the product**

> **Superseded for game-server registration by [051](051-phase-4-remainder.md) §2.** The panel
> never fetches organiser-supplied `host:port`; the first signed inbound heartbeat verifies the
> plugin. This section remains the threat analysis that applies if outbound server control is ever
> proposed again. Discord webhooks remain the product's live outbound-fetch sink.

> *Plain version:* you trick the *server* into making a network request for you. Because the
> server sits inside the network, it can reach things you can't — internal admin panels,
> cloud metadata endpoints, databases bound to localhost.

**This is the most under-appreciated risk in NEXTSKILL**, because we do it *on purpose*:
[007](007-server-hosting-model.md) has organisers register a server address, and we
health-check it ([009](009-api-contract.md)). **That is arbitrary outbound connection to a
user-supplied host, by design.**

An organiser registering `127.0.0.1:5432` or `169.254.169.254` turns our health check into
an internal port scanner.

| Control | Detail |
| --- | --- |
| **Deny private ranges** | Block RFC1918 (`10/8`, `172.16/12`, `192.168/16`), loopback, link-local (`169.254/16` — **cloud metadata**), IPv6 equivalents, `0.0.0.0` |
| **Resolve first, then validate, then connect to the resolved IP** | Prevents **DNS rebinding** — a hostname that passes validation and then resolves to `127.0.0.1` on the real connection |
| Port allowlist | Game server ports only; not `22`, `5432`, `6379` |
| No redirects followed | A redirect to an internal address bypasses the check |
| Short timeouts, no response body echoed back | Don't turn it into a data-exfiltration oracle |
| Egress firewall | Defence in depth — the app host shouldn't reach internal services at all |

**Severity: High.** This needs building correctly the first time, not hardening later.

## 3. Open redirect / URL injection — **Medium, on the auth path**

> *Plain version:* your site takes `?returnUrl=` and sends the user there after login. An
> attacker sends `nextskill.com/login?returnUrl=https://evil.com/fake-login`. The victim
> sees a genuine NEXTSKILL domain, logs in, and gets bounced to a convincing fake.

**Where it applies:** post-login redirect, and the **Steam OpenID and Discord OAuth
callbacks** ([011](011-accounts-and-login.md)) — exactly the flows that carry return URLs.

| Control | Detail |
| --- | --- |
| **Allowlist relative paths only** | Accept `/tournaments/5`. Reject anything with a scheme or host |
| Reject protocol-relative URLs | `//evil.com` is absolute despite looking relative — a classic bypass |
| OAuth `state` parameter | Signed, single-use, bound to the session — also the CSRF defence for OAuth |
| Fixed callback URIs | Registered with Steam and Discord; never dynamic |

## 4. Host header / cache poisoning — **Medium if a CDN is used**

> *Plain version:* if the app builds absolute URLs (password-reset links, canonical tags)
> from the incoming `Host` header, an attacker sends a forged header and your email contains
> *their* link. If a cache stores that response, other users get poisoned content.

| Control | Detail |
| --- | --- |
| Base URL from **configuration**, never from the request | Password reset, verification, canonical links |
| ASP.NET Core **host filtering** | Reject unexpected `Host` values |
| Cache keys include everything that varies the response | Or don't cache authenticated responses at all |

Directly relevant because reset emails ([011](011-accounts-and-login.md)) contain
security-critical links.

## 5. XSS — **Medium, concentrated in UGC**

> *Plain version:* an attacker puts `<script>` in a field. If it's rendered as HTML rather
> than text, it runs in every visitor's browser — with their session.

**NEXTSKILL's stored-XSS surface:** nicknames, team names, tournament descriptions, team
bios. All rendered on public pages, to many viewers.

| Control | Detail |
| --- | --- |
| Framework auto-escaping | TypeScript frontend escapes by default ([004](004-stack.md)) |
| **`dangerouslySetInnerHTML` / `v-html` banned** | The only realistic way to reintroduce XSS. Requires explicit review |
| **Markdown in tournament descriptions is the trap** | If rich text is offered, render with a **sanitiser allowlist** — never raw HTML passthrough |
| **CSP without `unsafe-inline`** | Turns a successful injection into a blocked script ([012](012-security.md)) |
| Nicknames used in **demo filenames / server commands** | Escape at *every* sink, not just HTML — see §11 |
| SVG avatar uploads | **Don't accept SVG.** It is executable markup |

## 6. CSRF — **Medium, unavoidable given cookie auth**

> *Plain version:* cookies are attached automatically. If you're logged into NEXTSKILL and
> visit a malicious page, it can make your browser POST to us *with your cookie*. The
> attacker can't read the response — but "delete my team" doesn't need a response.

Session cookies were chosen deliberately over JWT ([011](011-accounts-and-login.md)) for
instant revocation, and CSRF is the cost.

| Control | Detail |
| --- | --- |
| Synchroniser token on all state-changing requests | The attacker's page cannot read it |
| `SameSite=Lax` cookies | Strong second layer; not sufficient alone |
| **Never use `GET` for state changes** | `GET /teams/5/delete` is CSRF-able from an `<img>` tag |
| OAuth `state` parameter | CSRF defence for the login flow specifically |

## 7. Decompression bombs — **High, on demo upload**

> *Plain version:* a 1 MB zip that expands to 100 GB. The server fills its disk trying to
> unpack it.

Already specified in [012](012-security.md). Restated because it is the sharpest surface:
**decompression ratio limit, absolute size cap, abort mid-extraction, never trust the
declared size, generated filenames only.**

## 8. DDoS — **Cannot be solved in application code**

> *Plain version:* enough traffic to exhaust bandwidth or connections. No amount of clever
> C# helps if the pipe is full.

| Layer | Control |
| --- | --- |
| Volumetric | **A proxy/CDN in front (Cloudflare or equivalent).** This is the only real answer. Origin IP must not be publicly known |
| Application-layer floods | Rate limiting per account and per IP ([012](012-security.md)) |
| **SSE connection exhaustion** | **NEXTSKILL-specific.** Live streams are long-lived ([039](039-public-api.md)) — an attacker opening thousands starves the server. Cap concurrent streams **per account and per IP**, with idle timeouts |
| Expensive endpoints | Rating recalculation and bracket generation must never be triggerable by anonymous requests |

The SSE row is the one that wouldn't appear on a generic checklist and is specific to the
architecture chosen.

## 9. SEO poisoning — **Medium, reputational**

> *Plain version:* attackers use *your* trusted domain to host or link spam, so search
> engines index it. Your rankings suffer, and you may end up flagged.

**Applies because public profiles, team pages and tournament pages are indexable UGC.**
A team named `Cheap Watches — buy at spam.example` sits on our domain, in Google.

| Control | Detail |
| --- | --- |
| **No user-controlled hyperlinks** at launch. If added later: `rel="nofollow ugc noopener"` | Removes the SEO value that motivates the abuse |
| `noindex` on thin/empty pages — new profiles, empty teams | Also prevents mass-created profile spam being indexed |
| Rate-limit profile/team creation; require a verified email | Raises the cost of mass creation |
| Moderation + reporting, with staff takedown ([036](036-authorization.md)) | Human backstop |
| Length caps and character restrictions on names | Stops keyword-stuffed names |

## 10. IDOR / broken object-level authorization — **High**

> *Plain version:* changing `/matches/123` to `/matches/124` and seeing someone else's data.

**The highest-likelihood serious bug in any multi-tenant app**, and NEXTSKILL is multi-tenant
([037](037-platform-hosting.md)).

Controlled by [036](036-authorization.md): global query filters make scoping the default,
resource-based authorization for actions, **404 not 403** for out-of-scope resources (a 403
confirms the resource exists).

## 11. Command / argument injection into game servers — **Medium, easily missed**

> *Plain version:* if any user-controlled string is ever placed into an RCON command or
> server config, an attacker can break out of it and run other commands.

**NEXTSKILL-specific and easy to overlook:** nicknames and team names flow into match
configs, in-game name locking ([011](011-accounts-and-login.md)), and demo filenames.

| Control | Detail |
| --- | --- |
| **Prefer never sending RCON at all** — [009](009-api-contract.md)'s outbound-only design (V5) | The cleanest fix is not having the sink |
| Strict allowlist charset for nicknames and team names | No quotes, semicolons, newlines, or control characters |
| Escape at the sink, per sink | HTML, RCON, filename and JSON each need different escaping |
| Demo filenames generated by us | Never derived from user input ([012](012-security.md)) |

## 12. Mass assignment / over-posting — **Medium**

> *Plain version:* the API binds the whole JSON body onto a database entity. An attacker adds
> `"role": "owner"` or `"rating": 9999` and the ORM saves it.

| Control | Detail |
| --- | --- |
| **Never bind request bodies directly to entities** | Explicit request DTOs per endpoint ([006](006-architecture.md) — each feature folder owns its own) |
| `required` members, unknown fields rejected | Already mandated by [035](035-match-result-integrity.md) |
| Sensitive fields never appear in any request DTO | `role`, `rating`, `integrity`, `trusted` |

## 13. Account enumeration & email abuse — **Low/Medium**

| Threat | Control |
| --- | --- |
| Registration reveals whether an email exists | Identical response either way; confirm via email |
| Password reset reveals existence | Same |
| **Email bombing** — reset requested repeatedly for a victim's address | Rate-limit per address *and* per IP; cooldown between sends |
| Login timing reveals valid usernames | Constant-time comparison; consistent work on both paths |

## 14. Supply chain & secrets in git — **Medium, and now live**

The repository exists on GitHub as of today, which makes this immediate.

| Threat | Control |
| --- | --- |
| **A secret committed to git** | Secret scanning enabled; `.env` git-ignored; **a leaked secret is rotated, not just deleted** — git history is forever |
| Malicious/compromised NuGet or npm package | Lockfiles committed; automated dependency updates; review new dependencies (rare anyway — CONTEXT §3) |
| Compromised CI shipping to production | **Decision 032 is remote code execution by design.** Grill separately |

## 15. Business-logic abuse — **the category tools never catch**

Automated scanners find none of these. They are all NEXTSKILL-specific.

| Abuse | Control |
| --- | --- |
| **Match fixing** by a malicious organiser | Corroboration, not cryptography ([007](007-server-hosting-model.md)): team confirmation, demo evidence, dispute flow, challenge window |
| **Smurfing** | Two-factor binding ([011](011-accounts-and-login.md)); roster allowlist |
| **Rating farming** between colluding accounts | Glicko-2 makes beating weak opponents near-worthless ([025](025-rating-system.md)) |
| **Inheriting a rating** by buying a highly-rated shell team | Roster change sharply raises RD ([025](025-rating-system.md)) |
| Bracket manipulation | Advancement gated on integrity state; overrides audited ([035](035-match-result-integrity.md), [036](036-authorization.md)) |

---

## Priority order for implementation

| # | Threat | Severity | Why first |
| --- | --- | --- | --- |
| 1 | **SSRF on server registration/health check** | **High** | Designed into the product; hardest to retrofit |
| 2 | **IDOR / tenant leakage** | **High** | One missing filter is a breach ([036](036-authorization.md)) |
| 3 | **Decompression bomb / demo upload** | **High** | Untrusted files from untrusted servers |
| 4 | Dynamic `ORDER BY` allowlist | High if missed | One line separates safe from injectable |
| 5 | Open redirect on OAuth callbacks | Medium | Auth path; phishing amplifier |
| 6 | XSS in UGC + CSP | Medium | Broad blast radius |
| 7 | CSRF tokens | Medium | Unavoidable given cookie auth |
| 8 | Rate limits incl. **SSE connection caps** | Medium | Cheap; prevents easy self-inflicted outages |
| 9 | DDoS proxy in front | Medium | Infrastructure, not code |
| 10 | SEO poisoning controls | Medium | Reputational, slow-burning |

## The runnable checks

Extending [012](012-security.md)'s list:

1. Register a server at `127.0.0.1`, `169.254.169.254`, and a hostname that resolves to a
   private IP → **all rejected**.
2. `?returnUrl=//evil.com` and `?returnUrl=https://evil.com` → **both rejected**;
   `/tournaments/5` accepted.
3. Sort a leaderboard by `id; DROP TABLE` → **rejected by allowlist**, not passed through.
4. Create a team named `<script>alert(1)</script>` → rendered as **text** on every page
   showing it.
5. Open N+1 SSE streams as one account → **capped**, not unbounded.
