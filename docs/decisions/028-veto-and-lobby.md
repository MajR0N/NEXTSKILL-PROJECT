# 028 — Map veto & the match lobby

**Status:** decided
**Date:** 2026-08-10
**Decision:** **Veto happens on the web, before the server is needed.** Neither plugin
implements veto.

> **Implementation update, 2026-08-12:** the Phase 4 server-rendered floor is live at `/m/{id}`.
> Captain readiness and every action are durable; a 30-second overdue turn becomes one random legal
> action on read; and completion atomically freezes ordered maps and starting sides for the plugin.
> BO1, BO3 and BO5 output rules have executable checks. SSE remains the Phase 5 enhancement.

---

## Why this is the largest simplification in the project

The default assumption was in-game veto, because that is how MatchZy works. Nothing requires
it, and moving it to the web removes a state machine from **both** plugins:

| | In-game veto | **Web veto (chosen)** |
| --- | --- | --- |
| CS2 plugin | veto state machine, menu, timers | **none** |
| **CS 1.6 plugin** | **veto state machine in Pawn** + menu | **none** |
| Side selection | knife round logic | **decided by the veto** |
| Server running during veto | required | not required |
| Spectators | see nothing | **watch live over SSE** ([039](039-public-api.md)) |
| The UI | in-game menu (see below) | **the veto strip** ([019](019-visual-identity.md)) |

### On the in-game UI — stated accurately

In-game veto is **not** chat text. Both games have real menu APIs:

| Game | Menu facility |
| --- | --- |
| CS2 / CounterStrikeSharp | `ChatMenu` and `CenterHtmlMenu` — the HTML variant is a genuinely styled picker |
| CS 1.6 / AMXX | `menu_create` / `show_menu` — a numbered overlay selected with keys 1–9, paginated |

So the honest comparison is **a decent in-game picker versus a web picker** — not "chat versus
UI". The web still wins here, but on narrower grounds than a caricature would suggest:

- an AMXX menu is capped at ~9 visible entries and carries no imagery — fine for a 7-map pool,
  cramped beyond it
- neither menu is visible to spectators, casters, or the opposing team's substitutes
- neither survives a player reconnecting mid-veto
- both are still a **state machine we would have to write twice**

That last point is the real argument, and it stands regardless of how good the menus are.

The Pawn row is the one that matters most: a veto state machine in Pawn/Small is a
substantial share of the hardest work in [005](005-cs16-plugin.md).

### Side selection comes with it

In real tournament practice, sides are decided **by the veto** — the team that did not pick
the map picks side. Knife rounds are a pug/mix convention, not a tournament one.

So the web veto produces both outputs: **map order and starting sides**. The plugin receives
them as configuration and never decides either.

**Knife round becomes an optional per-tournament setting** ([015](015-admin-panel.md)),
defaulting to off. If a community wants it, the plugin still needs it — but it is no longer
on the critical path.

## The veto flow

```
lobby opens ──► both captains present ──► veto runs ──► map + sides decided
                                              │
                                              ▼
                                    match config ready
                                    server pulls it (009)
```

| Rule | Detail |
| --- | --- |
| Who acts | Captains only ([036](036-authorization.md)); everyone else watches live |
| Turn timer | Per step. Expiry → **random legal choice**, never a stall |
| Sequence | From the tournament's veto preset ([015](015-admin-panel.md)) |
| Illegal action | Rejected server-side. The client is never trusted to enforce turn order |
| Live updates | SSE ([039](039-public-api.md)) — but state is **re-fetchable**; the stream is a view, never the record ([035](035-match-result-integrity.md)) |
| Stalled veto | Referee may complete it manually — **audited** ([036](036-authorization.md)) |
| Result | Immutable once complete. Written to the match before the server is contacted |

The turn timer is what makes web veto safe: a captain who disappears cannot hold a
tournament hostage, because expiry auto-picks rather than waiting.

## The lobby page

One screen with everything needed before a match ([013](013-server-connect.md)):

```
┌─ LOBBY · Ćwierćfinał ────────────────────────────────────┐
│                                                          │
│  VETO STRIP  (live, fills in as bans land)               │
│  ─────────────────────────────────────────────           │
│                                                          │
│  SKŁAD                          DOŁĄCZ                   │
│  ✓ kubaa      związany          [ Dołącz ]               │
│  ✓ mateo      związany          lub w konsoli:           │
│  ⚠ zibi       brak setinfo      connect 51.83…  [kopiuj] │
│  ○ dawid      nie połączony                              │
│  ○ olek       nie połączony     TWÓJ KOD: 7K2Q           │
│                                 (widoczny tylko dla Ciebie)
└──────────────────────────────────────────────────────────┘
```

| Element | Source |
| --- | --- |
| Veto strip, live | This decision |
| Roster with per-player state | Plugin bind events ([009](009-api-contract.md)) |
| **Your link code, visible only to you** | [011](011-accounts-and-login.md) — never shown to team-mates or the organiser |
| **`setinfo` token warning** | [011](011-accounts-and-login.md) — surfaced **early**, not when they fail to join |
| Connect button **and** console command | [013](013-server-connect.md) |

**The `setinfo` warning is the most important thing on this page.** A CS 1.6 player without a
token cannot play. Discovering that at match start means a delay; discovering it in the lobby
means a fix.

## Consequence for both plugins

The plugin receives a **decided** match: maps, sides, rosters. Its job narrows to running the
match and reporting it. That reshapes [005](005-cs16-plugin.md) considerably and trims
[002](002-cs2-plugin.md) too.

## The runnable check

1. A captain attempting to act **out of turn** → rejected.
2. A captain attempting to ban an **already-banned** map → rejected.
3. Turn timer expiry → a **legal** random choice is made and the veto continues.
4. A completed veto produces the **correct map order and sides** for BO1, BO3 and BO5.
5. Reloading mid-veto → full correct state from a plain fetch, no SSE required.

Check 5 is the one that proves the live stream is a view rather than the record.
