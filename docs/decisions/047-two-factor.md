# 047 — Password reset and two-factor authentication

**Status:** **decided**
**Date:** 2026-08-11
**Implements:** [011](011-accounts-and-login.md) · **Relates to:** [012](012-security.md), [040](040-threat-model.md)

---

## Password reset

[011](011-accounts-and-login.md) required a reset flow and it did not exist — the single gap
that made the product unusable for a real person, because forgetting a password meant losing
the account permanently.

| Rule | Detail |
| --- | --- |
| **Single use** | Enforced by **deleting the row**, not by a flag. A flag is a check somebody eventually forgets, and forgetting once means a link that works twice |
| **5 minutes** | Deliberately short: the link is a live credential sitting in a mailbox |
| **Stored hashed** | SHA-256. A database leak must not hand over a working reset link for every account with one outstanding |
| **Non-disclosure** | An unknown address gets the same message, and no email is sent |
| **Mailbomb throttle** | One email per account per minute. The per-IP limiter does not cover this: the attacker's cost is one request, the victim's is one email |
| **Revokes everything** | Redeeming kills **every session**. Whoever forced the reset is signed out by the act of recovering the account |
| **Never signs anybody in** | Redirects to login — see below |

**Why SHA-256 here and Argon2id for passwords.** A password is human-chosen and therefore
guessable, so it needs a slow, memory-hard hash. A reset token is 256 bits of CSPRNG output:
no dictionary, no reuse, no structure. Slow hashing would buy nothing and cost 60ms per lookup.

**The 5-minute cost, accepted.** Mail delivery can itself take a minute or two, so some
legitimate users will follow an expired link. Expiry is therefore **never a dead end** — the
page says what happened and offers a new link in one click.

---

## Two-factor

### "Support Authy" means "implement TOTP"

Authy, Google Authenticator, 1Password and Aegis all implement RFC 6238. There is nothing to
integrate with and no vendor to depend on: thirty lines of standard library supports all of
them, offline, for free.

It is verified **against the published RFC vectors**, not against our own expectations — the
same discipline as checking Glicko-2 against Glickman's worked example. That is what proves an
authenticator app will accept our codes, which reasoning cannot establish.

### SMS is offered and named as weaker

| | TOTP | SMS |
| --- | --- | --- |
| Cost | free | per message, needs a gateway |
| Works offline | yes | no |
| Defeated by a SIM swap | no | **yes** |
| Stores personal data | no | **a phone number, GDPR** |

SMS exists because some people will not install an app — **not** because it is as strong, and
the settings page says so in plain language rather than letting somebody assume otherwise.

### Recovery codes are issued with the factor, never later

Ten codes, hashed, single use, shown exactly once. Without them, turning on two-factor is a way
to *lose* an account — the same lockout password reset exists to prevent, reintroduced one
layer up.

### The half-signed-in state is not a session

A password is one factor. When an account has a second, the login is not finished, so **no
session row is created**. The pending state travels in its own short-lived, data-protected
cookie carrying an account id and an expiry, and granting nothing else. A check asserts that a
correct password alone leaves the browser signed out.

### Reset and 2FA do not undermine each other

A reset **redirects to login** rather than signing anybody in. If it auto-signed people in,
anyone who took over an email account would walk straight past the second factor and it would
be worth nothing.

---

## Outbound messaging fails closed

There is no email or SMS provider yet, and there cannot be one without an account and a bill.
So in development messages are written to files — followable, and readable by the checks — and
in production **the application refuses to start**.

That is deliberate. A provider that silently drops mail is indistinguishable from one that
works: the reset form would keep saying "if that account exists, we sent a link" forever.

The end-to-end checks read the **real message files** rather than pulling tokens from the
database, because a token that exists but never reaches anyone is exactly the failure that
looks like success.

---

## Rate limiting is now a token bucket

Fixed windows have a seam at every boundary: twenty attempts at 4:59 and twenty more at 5:01 is
forty in two seconds, which is precisely the burst the limit exists to stop.

**Ten to spend at once, refilling one per ten seconds.** Somebody who mistypes a password twice
and then fumbles a code never notices; a sustained attack gets six attempts a minute. Requests
are refused immediately rather than queued — a queued login is a spinner and a held connection.

> `ponytail:` brute force against a six-digit code is held off by this limiter plus TOTP's
> ~90-second validity. Ceiling: a distributed attacker with many addresses. Upgrade path: a
> per-account failure counter with a lockout, which needs a column and a policy for who clears
> it.

### What changing it revealed

Switching to a bucket broke **every end-to-end suite at once**. The cause was in the tests, not
the limiter: a test host gives every request no source address, so the whole suite shared one
bucket and tripped the brute-force defence meant for a single attacker.

The fix was to give each client its own address — not a workaround, but what several people
using the site actually looks like — and the four copies of the same three helpers became one
`TestHost`. A copied helper is usually only a delay.

## The runnable checks

- TOTP reproduces the **RFC 6238 vectors** exactly, accepts ±1 step, rejects two minutes out
- A reset token is **not stored in plaintext**, and the email really is produced
- An unknown address sends **nothing** and answers identically
- A second request inside the throttle sends nothing
- Redeeming burns the token, **kills every session**, and does not sign anybody in
- Showing a TOTP secret enables nothing; only a correct code does
- A correct password alone **creates no session** when 2FA is on
- A recovery code works **once**, and the used one never again
