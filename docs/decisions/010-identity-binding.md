# 010 — Player identity & in-game binding

**Status:** decided
**Date:** 2026-08-10
**Decision:** **Identity lives in the NEXTSKILL web account.** In-game sessions are bound to
an account by a **single-use, short-lived link code**, never by trusting a reported SteamID.

---

## The problem

[001](001-game-scope.md) makes CS 1.6 first-class. CS 1.6 identity is not trustworthy:

| Fact | Source |
| --- | --- |
| Valve reports 10–15k concurrent CS 1.6 players in 2026, with a far larger non-Steam shadow population | Server-tracker research, 2026 |
| The **majority of active CS 1.6 servers run Reunion**, admitting non-Steam clients | Community server surveys |
| Non-Steam clients report `STEAM_ID_LAN` or a fake/variable ID | ReHLDS / Reunion / dproto behaviour |
| **SteamID is spoofable** on any server configured to accept non-Steam clients | Same |
| Nickname + password is how leaked CS 1.6 admin lists get abused | Long-standing AMXX community experience |

**If in-game identity is spoofable, then ELO, ranking, smurf detection, and bans are all
meaningless for CS 1.6.** And requiring Steam-legit clients would exclude most of the scene
NEXTSKILL exists to serve.

## Decision

**The SteamID is never the identity.** The NEXTSKILL account is.

```
panel                                        game server
-----                                        -----------
roster locked
issues per-player codes
  player A -> 7K2Q  (single use, TTL)
  player B -> M4XZ
                                             player joins
                                             types code in CONSOLE
                          <-- plugin reports --
                              { slot: A, code: "7K2Q",
                                ingame_id: "<whatever>",
                                seq: n }
binds session -> account
marks slot A as present
```

Spoofing a SteamID gains an attacker nothing: the code is the proof of identity, and the
reported in-game ID is treated as an opaque session label with no inherent trust.

### Per-game behaviour

| Game | Binding |
| --- | --- |
| **CS 1.6** | **Code required.** Reported ID is untrusted and unusable as identity. |
| **CS2** | **Automatic.** Valve authenticates the SteamID, and the account already holds a Steam-verified SteamID from login. No code step — do not impose friction where the platform already solved it. |

> `ponytail:` code flow exists only where identity is actually broken. Ceiling: if CS2
> ever admits spoofed IDs, extend the code flow to CS2 — the mechanism is already built.

---

## Security requirements for link codes

These are trust-boundary rules and are not negotiable.

| Rule | Reason |
| --- | --- |
| **Single use** | A reused code is a stolen identity |
| **Short TTL** — valid only for the match window | Limits the theft window |
| **Bound to one roster slot**, not to a player generally | A stolen code can only impersonate the slot it was issued for |
| **Rate-limited attempts per server and per match** | Prevents brute-forcing a short code |
| Sufficient entropy for the TTL and rate limit | A 4-char code is fine at 5 attempts/min; it is not fine unlimited |
| Reissuing invalidates the previous code | No lingering valid codes |
| **Never logged** in plaintext | Logs leak |

### The detail that would quietly break this: chat is public

**The code must not be entered through public chat.** `say` broadcasts to everyone;
`say_team` broadcasts to the team. Either lets an opponent — or a teammate — read a code
before its owner uses it.

**Required implementation:**

- Primary: a **client console command** (`register_clientcmd` in AMXX) that is never
  broadcast. The player types it in console, not in chat.
- Fallback: if a `say /link ...` form is offered for convenience, the plugin **must**
  suppress the message (`PLUGIN_HANDLED`) so it never reaches other players.
- The panel must never display another player's code to anyone, including team-mates and
  the organiser.

Single-use + slot-binding + short TTL means even a leaked code has a narrow blast radius —
but the leak should not happen in the first place.

---

## Consequences

- **In-game IDs are stored as opaque session labels**, linked to a match and an account.
  They are never a primary key for a player and never carry trust across matches.
- **Bans are account bans**, not ID bans, for CS 1.6. ID bans are unenforceable there.
- **Smurf detection** cannot rely on in-game IDs for 1.6. It must work from account
  signals. This is a genuine limitation, honestly recorded rather than wished away.
- One extra step per player per 1.6 match. Acceptable friction; it is the price of a
  defensible ranking.
- The plugin needs a code-submission path in **both** plugins ([002](002-cs2-plugin.md),
  005) — though CS2's is only a latent capability, unused at launch.

## The runnable check

One test asserting that a link code:

1. binds successfully once,
2. is **rejected on second use**,
3. is **rejected after TTL expiry**,
4. is **rejected for a different roster slot** than the one it was issued for.

If any of those four bind successfully, the check fails.

## Open follow-ups

- Web login providers — Steam OpenID alone excludes non-Steam 1.6 players from even having
  an account. Needs Discord and/or email. Tracked as decision **011**.
- Ban evasion by simply making a new NEXTSKILL account — mitigated by whatever login
  providers are chosen (Discord accounts are more costly to recreate than emails).
  Folds into 011.
