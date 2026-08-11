# 054 — Becoming a rated player

**Status:** decided
**Date:** 2026-08-11
**Relates to:** [010](010-identity-binding.md) (identity binding), [011](011-accounts-and-login.md)
(accounts, Steam link, `setinfo` token), [012](012-security.md) (security),
[025](025-rating-system.md) (ratings), [029](029-profile-login-footer.md) (no fabricated history),
[040](040-threat-model.md) (abuse), [050](050-match-results-and-ratings.md) (participants and
provenance), [053](053-rewards-and-prize-pools.md) (what "counting" earns)

---

## The problem this solves

Valve removed progression from community servers: Premier is the ranked mode, private servers
grant no XP and produce no record. NEXTSKILL's answer is to **make time on a community server
count again** — and the entire emotional payload of that promise is one instant:

> a match ends, and something says *this counted — you are ranked now.*

Everything else is plumbing for that moment. **If reaching it requires leaving the game, opening a
browser and filling in a registration form first, most players never reach it at all** — and the
population we most want to convert is precisely the casual regulars already sitting on a partner's
server.

[010](010-identity-binding.md) and [011](011-accounts-and-login.md) settled *what an identity is*.
They did not settle *when a player has to acquire one*, and the honest answer differs by game.

## Decision: the two games get different onboarding, on purpose

| | **CS2** | **CS 1.6** |
| --- | --- | --- |
| Order | **Play first, claim after** | **Account first, then bind** |
| Identity anchor | SteamID, verified by Valve | Web account + `setinfo` token + per-match code |
| Friction at first play | **None** | Real, and unavoidable |

**This is not an inconsistency.** It follows directly from a fact
[010](010-identity-binding.md) already established: a CS2 SteamID is verifiable, and a CS 1.6
SteamID is spoofable on non-Steam servers. The lazy path exists in one game and is a security hole
in the other.

### CS2 — play first, claim after

A match on a partner's server is recorded against the SteamIDs that played it. The player is shown
that their match was recorded and offered a one-click claim. **Steam OpenID is already built and
verified server-to-server** ([011](011-accounts-and-login.md)), so claiming is a single login that
attaches the accumulated history to a real account.

Why this is worth the extra state:

- It converts the **existing casual population**, not only competitive teams. A network owner
  hears *"your regulars start getting ranked without doing anything"*, which is a far better
  answer to *what do I get in return* than anything about brackets.
- The payoff is felt **before** the friction is asked for, which is the only order that works when
  the friction is optional.

### CS 1.6 — account first, unchanged

[010](010-identity-binding.md) requires two factors precisely because the in-game identity proves
nothing. There is no lazy version here that is not a security hole, and this decision does not
attempt one. CS 1.6 players register, set the token, and enter the per-match code — as decided.

## How it fits the participant model

No new mechanism. [050](050-match-results-and-ratings.md) already has one row per
`(match, account)` with a `Provenance` that gets **promoted** when better evidence arrives
(`Declared` → `Bound`). Claiming is the same shape:

- an **unclaimed** participant carries a verified SteamID and **no `AccountId`**;
- claiming sets the `AccountId` across that identity's history in one transaction;
- ratings for unclaimed identities are computed exactly as for claimed ones — the maths never sees
  the difference, which is what makes the claim feel like *recovering* a history rather than
  starting one.

This also keeps [029](029-profile-login-footer.md) honest: nothing is fabricated. An unclaimed
record is a verified statement that *this SteamID played this match*, which is true.

## Constraints that are not negotiable

- **One provider identity, one account.** The unique index that already enforces this for Steam
  links ([011](011-accounts-and-login.md)) governs claiming too, including under concurrent
  callbacks. A claimed identity cannot be re-claimed.
- **Claiming is atomic**, under the same row-locking discipline as the rest of the platform. A
  partially claimed history is worse than an unclaimed one.
- **An unclaimed profile exposes nothing an account would not.** SteamID and match results only —
  no inferred names beyond what the game reported, no contact data, nothing an allow-list
  projection would refuse ([029](029-profile-login-footer.md), [012](012-security.md)).
- **Unclaimed identities are visible in rankings but flagged as unclaimed.** Hiding them would
  defeat the point; presenting them as members would be a lie.
- **A player may refuse.** An unclaimed identity can be suppressed on request without deleting the
  match record — the same tombstone shape GDPR erasure already uses.

## What was given up

- **Extra state in the schema** — a participant row that may have no account. Accepted: it is one
  nullable foreign key, and it is the state that already had to exist for `Declared` participants
  anyway.
- **Two onboarding paths to document, support and translate.** Accepted, because collapsing them
  means either adding friction to CS2 that costs conversions, or removing a factor from CS 1.6
  that [010](010-identity-binding.md) proved necessary.
- **Rejected: account-first everywhere.** Cleanest model, and it loses every casual player at the
  registration form — the exact population the partner is being promised.
- **Rejected: team-gated rating only.** Simplest, and it never touches a network's casual
  population, which is the one worth converting.

## The runnable check

1. A match played by an unknown SteamID **produces a rated, unclaimed participant row** and moves
   a rating.
2. A Steam login **claims the entire prior history in one transaction**, and the resulting rating
   is byte-identical to the unclaimed one — claiming reveals a history, it does not create one.
3. A second account **cannot claim an already-claimed identity**, even under concurrent callbacks.
4. An unclaimed public profile **exposes no field** the `/players/{id}` allow-list would refuse.
5. **CS 1.6 never produces an unclaimed rated participant** — the two-factor path is the only way
   in, and a match without it is unrated.
