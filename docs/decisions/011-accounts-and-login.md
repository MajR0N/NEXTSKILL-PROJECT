# 011 — Accounts, login, and two-factor in-game binding

**Status:** decided (with assumptions flagged for verification)
**Date:** 2026-08-10
**Supersedes:** the single-factor code flow in [010](010-identity-binding.md) — codes remain,
but they are now **one of two required factors**, not the whole mechanism.

---

## Decision

### Web account

**A native NEXTSKILL account is the primary identity.** Registration happens on the website.
Steam, Discord and other socials are **optional links**, not login requirements.

| | |
| --- | --- |
| Primary | NEXTSKILL account (email + password) |
| Optional links | Steam, Discord, other socials |
| Steam link | **Must be verified as genuinely theirs** — see below |
| Non-Steam 1.6 players | Fully supported; they simply never link Steam |

**Why this shape:** it excludes nobody. A non-Steam CS 1.6 player — a large share of the
target audience ([010](010-identity-binding.md)) — can hold a full NEXTSKILL identity,
profile, rating and history without owning the game on Steam.

**Steam link verification** is solved for free: Steam OpenID *is* the proof. The player is
redirected to Steam, authenticates there, and Steam returns their SteamID to us. A user can
only complete that flow for an account they can actually log into. We never ask them to
*type* a SteamID — a typed SteamID proves nothing.

### Cost accepted: we now store passwords

Registering natively means **NEXTSKILL owns password security**. This was avoidable by using
only social login and is being taken on deliberately. Non-negotiable requirements:

| Rule | Detail |
| --- | --- |
| Hashing | **Argon2id** (or bcrypt), never a plain/fast hash. Never reversible. |
| Reset flow | Single-use, short-TTL, high-entropy tokens. Requesting a reset must not reveal whether an address exists. |
| Rate limiting | On login, registration, and reset request — per account **and** per IP |
| Breach posture | Assume the DB leaks. Correct hashing is what makes that survivable. |
| Session | Server-side session cookie: `HttpOnly`, `Secure`, `SameSite`, rotated on login |

> *Plain version:* hashing turns a password into a one-way fingerprint. Argon2id is
> deliberately **slow and memory-hungry**, so an attacker with a stolen database can only
> test a few guesses per second instead of billions. That speed difference is the entire
> defence.

---

## In-game binding: two factors, both required

A player is bound to an in-game session only when **both** succeed:

| # | Factor | Nature | Set when |
| --- | --- | --- | --- |
| 1 | **`setinfo` token** | Persistent — lives in the client config | Once, from the profile page |
| 2 | **Per-match link code** | Ephemeral — single use, slot-scoped, short TTL | Per match, from the lobby |

```
profile page:  setinfo _ns "<persistent token>"   <- set once, kept in config
match lobby:   your code this match: 7K2Q          <- new every match

on connect:  plugin reads setinfo token  -> which account is this?
in game:     player submits code         -> is this the person we rostered?
             BOTH must match the same roster slot, or no bind.
```

### Why both, and not either alone

| Attack | Code only | `setinfo` only | Both required |
| --- | --- | --- | --- |
| Random impersonation | blocked | blocked | blocked |
| **Ringer/smurf: "here, play on my code"** | **works** | — | **blocked** — ringer lacks the victim's config token |
| **Token harvested by a rogue server** | — | **works** | **blocked** — attacker lacks the live per-match code |
| Replay of an old code | blocked | — | blocked |

### The `setinfo` leak problem — stated explicitly

A `setinfo` value is stored in the player's client config and is **transmitted to every
server they connect to**, including servers with no relation to NEXTSKILL. A malicious
server operator can therefore harvest NEXTSKILL tokens at scale.

This is precisely why `setinfo` **cannot be the only factor**. Mitigations on top of
two-factor:

- The token is **rotatable and revocable** from the profile at any time.
- A token is only meaningful on a **NEXTSKILL-registered server**
  ([007](007-server-hosting-model.md) requires verified registration).
- **Anomaly detection:** the same token presenting from two different in-game sessions
  concurrently → flag and refuse both.
- The token is **not a password** — it can never be used to log into the website.

---

## Plugin-side enforcement (anti-smurf)

The panel tells the plugin exactly who is allowed. **The plugin enforces it on the server**,
rather than reporting afterwards.

| Rule | Enforcement |
| --- | --- |
| **Roster allowlist** | Only accounts on the locked roster may take a playing slot. Everyone else is spectator or kicked. |
| **Both factors before play** | An unbound player cannot join a team — held in spectator until bound |
| **One session per account** | The same account binding twice → second attempt rejected |
| **Session continuity** | If the in-game ID behind a bound slot changes mid-match, flag the match `suspect` ([035](035-match-result-integrity.md)) |
| **Name lock** | In-game name forced to the rostered name, so scoreboards and demos are readable and cannot mislead |
| **Late join** | A player joining after match start must satisfy both factors identically |

> *Plain version:* "allowlist" means the server starts from *nobody is allowed* and lets in
> only the named ten. That's safer than letting everyone in and trying to kick impostors —
> a rule that fails closed beats one that fails open.

### What this does and does not stop

**Stops:** casual ringers, code sharing, opportunistic smurfing, wrong-person-joined mistakes.

**Does not stop:** a determined player who creates a fresh NEXTSKILL account and plays on it
from the start. That is not an *identity binding* problem — it is an *account provenance*
problem, and no in-game mechanism solves it. Mitigations there are social and statistical
(linked Steam/Discord raising the cost of a new account, rating-trajectory anomaly
detection, organiser vetting) and are deliberately **not** being built yet.

> `ponytail:` two-factor binding is the whole anti-smurf story at launch. Ceiling: it does
> nothing against a brand-new account played honestly from game one. Upgrade path:
> require a linked Steam/Discord account for ranked play, which raises the cost of a
> throwaway identity without building a detection system.

---

## ⚠ Assumptions requiring verification before implementation

Web research was unavailable at decision time (session limit). **These are assumptions, not
verified facts, and must be tested on a real server before any code depends on them:**

1. `setinfo`-style client info keys are readable server-side by an AMXX plugin
   (expected: `get_user_info` / `pfnGetInfoKeyBuffer`).
2. Client info values persist across sessions in the player's config as expected.
3. Values are readable **at connect time**, early enough to gate team joining.
4. Behaviour is consistent across common non-Steam clients (Reunion/dproto), not just
   legitimate Steam clients.
5. Length limits on info keys are sufficient for a high-entropy token.
6. Whether the value is visible to *other players* by any means (it must not be).

**Verification method:** stand up a CS 1.6 server with AMXX, set a value client-side,
and read it from a test plugin. One evening. Per project instruction — *test before
acknowledging.*

Point **6** is a potential design-breaker: if other players can read another player's info
keys, the token is not a secret and the whole factor collapses to a public identifier.
**Verify this one first.**

---

## Privacy note — GDPR applies

The first partner and primary audience are in **Poland (EU)**. Player IP addresses and
in-game identifiers are **personal data** under GDPR.

Cheap to get right now, expensive to retrofit:

- Collect only what is needed, and say so plainly in a privacy notice.
- Store IPs **hashed with a salt** where they are used for abuse detection rather than
  displayed; never expose them in any UI.
- Define a retention period for demos, IPs and logs — and actually enforce it.
- Support account deletion, including what happens to match history (anonymise the player,
  keep the match — deleting results would corrupt tournaments).

Tracked further under decisions 012 and 024.

## The runnable check

Extending [010](010-identity-binding.md)'s check. One test asserting a bind is **rejected**
when:

1. code is valid but `setinfo` token belongs to a different account,
2. `setinfo` token is valid but the code is expired, already used, or for another slot,
3. both are valid but the account is **not on the locked roster**,
4. both are valid but that account is **already bound** to a live session.

A bind succeeds **only** when both factors match the same rostered slot.
