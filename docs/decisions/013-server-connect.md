# 013 — Connecting players to the server

**Status:** decided
**Date:** 2026-08-10

---

## The asymmetry that shapes this

CS2 and CS 1.6 do **not** connect the same way, and the difference lands exactly on the
audience NEXTSKILL exists for.

| | CS2 | CS 1.6 (Steam) | CS 1.6 (non-Steam) |
| --- | --- | --- | --- |
| `steam://connect/ip:port` | ✅ works | ✅ works | ❌ **no Steam client to handle it** |
| Console `connect ip:port` | ✅ | ✅ | ✅ **the only reliable path** |

Most active CS 1.6 servers admit non-Steam clients ([010](010-identity-binding.md)), so a
"Join" button that only emits `steam://` **silently fails for a large share of the 1.6
audience** — the button does nothing, and the player has no idea why.

## Decision: always show both

The match lobby shows a connect button **and** a copyable console command, always. Not a
fallback, not hidden behind "having trouble?" — both, side by side.

```
┌─ DOŁĄCZ DO SERWERA ─────────────────────────────┐
│                                                 │
│   [ Dołącz ]        ← steam://connect/…         │
│                                                 │
│   lub wklej w konsolę (~):                      │
│   ┌───────────────────────────────────┐  [kopiuj]
│   │ connect 51.83.12.9:27015;password …│         │
│   └───────────────────────────────────┘         │
└─────────────────────────────────────────────────┘
```

**Rationale:** for non-Steam 1.6 players the console command is not a workaround, it is *the*
method. Presenting it as a fallback frames a large part of the audience as an edge case.

## Server password per match

Every match server is password-protected, with a **password unique to that match**.

| Why | |
| --- | --- |
| Keeps randoms out of a live tournament match | Public servers are public |
| Limits the window | Password valid only while the match is live |
| Complements, does not replace, the roster allowlist | The plugin still enforces who may take a playing slot ([011](011-accounts-and-login.md)) |

**The password is not a security control** — it appears on the lobby page for ten rostered
players and is trivially shareable. It is a *convenience* barrier. Actual enforcement is the
two-factor binding and the allowlist in the plugin. Documented explicitly so nobody later
mistakes it for authentication.

## What the lobby page shows

Everything a player needs, on one screen, before the match:

| Element | Source |
| --- | --- |
| Connect button + console command | This decision |
| **Your link code** — visible only to you | [011](011-accounts-and-login.md) — never shown to team-mates or the organiser |
| `setinfo` token status: set / not set | [011](011-accounts-and-login.md) — with a one-line explanation of why it isn't a password |
| Roster with per-player ready/bound state | Plugin events ([009](009-api-contract.md)) |
| Map, format, server, veto progress | [028](028) |

**A CS 1.6 player who has not set their `setinfo` token cannot play**, so the lobby must
detect and explain that *before* match start — not at the moment they try to join a team.

## Rejected

| Option | Why not |
| --- | --- |
| `steam://` only | Silently breaks for non-Steam 1.6 players — a large share of the target audience |
| Console command only | Worse for CS2 players, where one click genuinely works |
| Auto-detect and show one | Detection is unreliable, and guessing wrong strands a player with no visible alternative |
| A downloadable launcher | An executable to solve a copy-paste problem |
