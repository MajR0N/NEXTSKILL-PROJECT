# 012 — Security measures

**Status:** decided
**Date:** 2026-08-10
**Lens applied:** secure-code-guardian

This document covers what is **specific to NEXTSKILL**. Generic web hygiene (parameterised
queries, output encoding, HTTPS everywhere, dependency updates) is assumed, not argued.

---

## Trust boundaries

Every one of these is a place where laziness is banned (CONTEXT §3, principle 3).

| Boundary | Trusted? | Control |
| --- | --- | --- |
| Browser → API | ❌ | Session cookie + CSRF, validation, authorization ([036](036-authorization.md)) |
| **Game server → API** | ❌ **Third-party infrastructure** ([007](007-server-hosting-model.md)) | HMAC signature + replay window + `seq` ([009](009-api-contract.md)) |
| **Demo upload** | ❌ **Attacker-controlled file** | See below — the sharpest surface |
| Steam / Discord OAuth callback | ⚠️ | Verify with the provider; never trust returned IDs directly |
| Admin panel → deploy pipeline | ❌ | See decision 032 — this is remote code execution by design |

---

## 1. Plugin → panel authentication

**HMAC-SHA256** over the raw request body, with a per-server secret
([009](009-api-contract.md)).

| Control | Detail |
| --- | --- |
| Signature covers | Raw body **and** a timestamp header |
| Replay window | Reject requests older than a few minutes |
| Idempotency | `(match_id, seq)` — replays inside the window are safe no-ops |
| Comparison | **Constant-time**. A naive `==` leaks the signature one byte at a time via timing |
| Secret rotation | Supported per server, without downtime |
| Body reading | Verify against the **raw bytes**, before deserialisation — not the re-serialised object |

> *Plain version:* verifying a signature with `==` seems fine, but string comparison stops
> at the first differing byte. An attacker measuring response times can therefore guess the
> signature one byte at a time. Constant-time comparison always checks every byte.

## 2. Demo upload — the sharpest attack surface

Demos are **large zip files from an untrusted server** ([001](001-game-scope.md), CS2 only).
This is the most dangerous endpoint in the system.

| Threat | Control |
| --- | --- |
| **Zip bomb** — a small file expanding to fill the disk | Enforce a **decompression ratio limit and absolute size cap**; abort mid-extraction, never trust the declared size |
| **Path traversal** — entries named `../../etc/passwd` | **Never use the filename from the archive.** Generate our own name from the match ID |
| Disk exhaustion | Per-server upload quota, absolute size limit, disk-space check before accepting |
| Unauthenticated upload | HMAC verified **before** a single byte is written to disk |
| Serving demos back | Stored **outside the web root**, served through an authorised endpoint, never as static files |
| Content type confusion | Serve with an explicit content type and `Content-Disposition: attachment` — never let a browser render an uploaded file inline |

> `ponytail:` size cap + ratio limit + generated filenames. Ceiling: doesn't inspect demo
> contents for validity. Upgrade path: parse the header with DemoFile.Net on ingest to
> confirm it is a real demo — only if junk uploads become a problem.

## 3. Secrets at rest

| Secret | Handling |
| --- | --- |
| **RCON passwords** | **Ideally not stored at all** — [009](009-api-contract.md)'s outbound-only design likely removes the need (V5). If stored: encrypted with a key held **outside** the database, never returned by any API, never logged, redacted from staff view ([007](007-server-hosting-model.md)) |
| **`setinfo` tokens** | Stored **hashed**, like a password. We only ever need to *verify* one, never display it. Rotatable and revocable ([011](011-accounts-and-login.md)) |
| **Per-match link codes** | Short TTL, single use, stored hashed, never logged ([011](011-accounts-and-login.md)) |
| **User passwords** | **Argon2id** ([011](011-accounts-and-login.md)) |
| Plugin HMAC secrets | Encrypted at rest; shown **once** at server registration, never again |

**The rule that catches most leaks:** secrets leak through **logs** far more often than
through databases. No secret is ever logged, included in an error message, put in a URL, or
returned in an API response — even to its owner.

## 4. Rate limiting

ASP.NET Core has built-in rate limiting; no dependency needed.

| Endpoint | Why |
| --- | --- |
| Login, registration, password reset | Credential stuffing ([011](011-accounts-and-login.md)) |
| **Link code submission** | The code is short — brute force is the obvious attack ([011](011-accounts-and-login.md)) |
| Demo upload | Disk exhaustion |
| Event ingestion | A misbehaving or malicious plugin |
| Public API generally | Scraping, denial of service |

Limit **per account and per IP**, since either alone is trivially bypassed.

## 5. Browser-facing controls

| Control | Setting |
| --- | --- |
| Session cookie | `HttpOnly`, `Secure`, `SameSite=Lax`, rotated on login and privilege change |
| **CSRF** | Required — cookie auth means the browser attaches credentials automatically. Synchroniser token on all state-changing requests |
| CSP | Strict; no `unsafe-inline`. The frontend is a build artefact, so nonces/hashes are practical |
| HSTS | Enabled with a long max-age |
| Clickjacking | `frame-ancestors 'none'` |

> *Plain version:* **CSRF** works because cookies are sent automatically. If you're logged
> into NEXTSKILL and visit a malicious page, it can make your browser POST to us *with your
> cookie attached*. A CSRF token defeats this because the attacker's page cannot read it —
> it can only make your browser send requests blindly.

## 6. GDPR — the audience is in the EU

Poland is the primary market ([007](007-server-hosting-model.md), CONTEXT §3b). IPs and
in-game identifiers are personal data.

| Obligation | Implementation |
| --- | --- |
| Minimise collection | Store what is needed; document why |
| IP handling | **No persistent application IP column.** The auth limiter uses a random per-process HMAC key; it retains neither the raw address nor a cross-restart identifier |
| Retention | Application IP retention is zero. Demos and future infrastructure logs need their own explicit periods — **and jobs that actually enforce them** |
| Deletion | **Anonymise the player, keep the match.** Contact data, credentials, provider links, sessions and transient community data are destroyed; deleting results would corrupt tournament history and other players' ratings |
| Export | Users can obtain their data |
| Hosting region | **EU region** ([037](037-platform-hosting.md)) |

> ⚠ **Reversed in one place, 2026-08-22, by [058](058-what-we-sell.md) §6c.** The zero-persistent-
> identifier posture above holds for **IP addresses** and stays the default. It does **not** hold
> for the anti-cheat: a **salted HWID hash is persisted**, because a ban keyed only to an account
> is defeated by registering another one.
>
> **A hashed HWID is still personal data** — pseudonymisation, not anonymisation. Basis is
> **Art. 6(1)(f)** with **Recital 47** (fraud prevention named explicitly); retention is a defined
> period under **Art. 5(1)(e)**; the ban tombstone survives an erasure request under
> **Art. 17(3)(e)** and **Art. 21(1)**, keeping the hash and the decision and nothing else — the
> same shape as *anonymise the player, keep the match*, one step further.
>
> **Owed before it ships:** an **LIA**, a **DPIA** (very likely mandatory — WP248 scoring plus
> systematic monitoring), and **Art. 13 notice wording**. Also **Art. 22**: an automated ban needs
> a human who can genuinely overrule it. Detail and sources in [CONTEXT §2.11](../../CONTEXT.md).

The deletion rule matters: a naive cascade delete would destroy match history and silently
corrupt every opponent's rating ([025](025-rating-system.md)).

The legal basis was rechecked on 2026-08-11 against the official [GDPR text](https://eur-lex.europa.eu/eli/reg/2016/679/2016-05-04): Recital 30 names IP addresses as online identifiers,
Article 5 requires storage limitation, and Article 17 defines erasure. The European Commission
also states that data may be retained after an [appropriate anonymisation process](https://commission.europa.eu/law/law-topic/data-protection/information-business-and-organisations/dealing-requests-individuals/do-we-always-have-delete-personal-data-if-person-asks_en).

## 7. Audit log

Append-only, covering every action listed in
[036](036-authorization.md#actions-requiring-a-recorded-human-decision), plus
authentication events, credential rotations, and permission changes.

Records **actor, timestamp, resource, action, reason** — and never the secret involved.
Staff can read it; nobody can edit it.

## The runnable checks

1. A request with a **valid body but wrong HMAC** → rejected.
2. A **replayed** request outside the timestamp window → rejected.
3. A **zip bomb** upload → aborted before filling the disk.
4. An archive entry named `../../evil` → written safely under a generated name, nowhere else.
5. **No secret appears in any log** — grep the test-run logs for known test secrets; the
   check fails if any is found.

Check 5 is cheap, unglamorous, and catches the most common real-world leak.
