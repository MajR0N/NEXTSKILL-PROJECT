# 011 — Accounts, login, and two-factor in-game binding

**Status:** decided (with assumptions flagged for verification)
**Date:** 2026-08-10
**Supersedes:** the single-factor code flow in [010](010-identity-binding.md) — codes remain,
but they are now **one of two required factors**, not the whole mechanism.


> **Built (2026-08-11):** registration, login, logout, **password reset**, **two-factor**
> (TOTP + SMS), recovery codes, account-wide session revocation, GDPR account erasure,
> rotate-never-retrieve `setinfo` tokens, and verified Steam/Discord linking — see
> [047](047-two-factor.md). Argon2id, server-side
> sessions, token-bucket rate limiting, answers that reveal nothing.
>
> **Not built:** per-match code binding described below — binding needs the plugins
> ([005](005-cs16-plugin.md)) and has **no runnable check yet**. The public
> and private profile surfaces now exist. Data export remains a separate slice.

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

**Re-verified 2026-08-11:** [Valve still documents browser OpenID 2.0](https://partner.steamgames.com/doc/features/auth?l=english)
for linking a verified 64-bit SteamID to an existing third-party account. Discord linking uses
its [authorization-code flow](https://docs.discord.com/developers/topics/oauth2) with only
[`identify`](https://docs.discord.com/developers/resources/user); the temporary access token
reads `/users/@me` and is never stored.

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

## Verification status

**Researched 2026-08-10. The design-breaker is cleared and the approach is validated by
long-standing community practice.**

| # | Claim | Status |
| --- | --- | --- |
| 1 | Client info keys are readable server-side by AMXX | ✅ **Confirmed** — `get_user_info(index, const info[], output[], len)` reads values from the player's `userconfig.cfg` |
| 2 | Carrying a secret in `setinfo` is a viable pattern | ✅ **Confirmed** — `setinfo _pw` has been the CS 1.6 admin/VIP password convention for two decades |
| 3 | Other **players** can read another player's info keys | ✅ **No evidence found.** The documented threat is *servers*, not players |
| 4 | Malicious **servers** can harvest info keys | ⚠️ **Confirmed as a real, actively-exploited attack** — *"when you join bad servers, they steal your `_pw` userinfo"*. See the AlliedModders ["setinfo getting hacked"](https://forums.alliedmods.net/archive/index.php/t-241341.html) thread |
| 5 | Readable **at connect time**, early enough to gate team joining | ❓ **Untested** — verify on a live server |
| 6 | Consistent on non-Steam clients (Reunion/dproto) | ❓ **Untested** |
| 7 | Length limit permits a high-entropy token | ❓ **Untested** |

**The critical finding:** the leak vector is real and actively exploited — but it is rogue
*server operators*, not opponents in the match. This is exactly the threat that two-factor
binding neutralises. A harvested token is worthless without the live, single-use,
slot-scoped per-match code.

Items 5–7 remain untested. Verification method: stand up a CS 1.6 server with AMXX, set a
value client-side, read it from a test plugin, and repeat with a non-Steam client.
One evening.

### Product risk uncovered by this research

**CS 1.6 players have been burned by `setinfo` credential theft before.** Asking them to
paste a NEXTSKILL token into their config will meet justified suspicion.

Onboarding copy must state plainly, and truthfully:

- it is **not a password** and cannot log into the website
- it is **useless on its own** — a match code is always required as well
- it is **revocable in one click** from the profile
- rogue servers can read it, and that is *fine by design*

This is a trust and communication problem, not a technical one — but getting the copy
wrong will cost adoption in exactly the community this project targets.

---

## Privacy note — GDPR applies

The first partner and primary audience are in **Poland (EU)**. Player IP addresses and
in-game identifiers are **personal data** under GDPR.

Cheap to get right now, expensive to retrofit:

- Collect only what is needed, and say so plainly in a privacy notice.
- Do not persist IPs when an in-memory abuse control is enough. The authentication limiter uses
  a per-process HMAC partition key, so the raw IP is not retained and the key becomes unlinkable
  on restart; never expose either in any UI.
- Define a retention period for demos, IPs and logs — and actually enforce it.
- Support account deletion, including what happens to match history (anonymise the player,
  keep the match — deleting results would corrupt tournaments).

> **Built (2026-08-11):** PostgreSQL has no application IP column. Account erasure destroys
> contact details, credentials, provider links, live memberships and transient community data;
> the stable account id remains only behind an anonymous tombstone so competition history is
> not rewritten.

Tracked further under decisions 012 and 024.

## The runnable check

Extending [010](010-identity-binding.md)'s check. One test asserting a bind is **rejected**
when:

1. code is valid but `setinfo` token belongs to a different account,
2. `setinfo` token is valid but the code is expired, already used, or for another slot,
3. both are valid but the account is **not on the locked roster**,
4. both are valid but that account is **already bound** to a live session.

A bind succeeds **only** when both factors match the same rostered slot.
