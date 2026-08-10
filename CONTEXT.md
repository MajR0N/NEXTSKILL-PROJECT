# NEXTSKILL — Context

The shared, general-purpose document. Anything true across the whole project lives here.
Anything specific to one decision lives in `docs/` as its own file.

**Status:** design phase. Nothing is built. Decisions are being made via structured grilling.

---

## 1. What NEXTSKILL is

A tournament platform for the **low / community Counter-Strike scene** — community
server owners and grassroots organisers who want to run real tournaments without
FACEIT/ESEA-tier infrastructure or cost.

Target games (under active decision, see `docs/decisions/001-game-scope.md`):
CS 1.6, CS:GO, CS2.

---

## 2. Verified external facts

Everything in this section was confirmed by research, not assumed. Each fact carries
its source and the date it was checked. **Re-verify before relying on any of these
after the check date** — this ecosystem moves.

### 2.1 CS2 match orchestration is a solved problem

[MatchZy](https://shobhit-pathak.github.io/MatchZy/) is a **MIT-licensed**
CounterStrikeSharp plugin that already provides:

| Capability | Detail |
| --- | --- |
| Remote match config | `matchzy_loadmatch_url https://panel/api/match/123` + optional auth header. Config format mirrors **Get5**. |
| Map veto | In-game veto flow |
| Knife round & side swap | Built in |
| Ready-up / pause / tac-pause / forfeit | Built in |
| Series support | Bo1 / Bo3 / Bo5 |
| Round backup & restore | `mp_backup_round_file` — recovers a match after a server crash |
| GOTV demo recording | Starts on ready-up, ends on map result |
| Demo upload | `matchzy_demo_upload_url` — POSTs the **zipped demo** with `MatchZy-MatchId` and `MatchZy-MapNumber` HTTP headers |
| Event webhooks | connect / disconnect / ready / round start / round end / map end / series end, with debounced retry + backoff |
| Storage | SQLite local, MySQL supported |

**Consequence:** the panel never talks *to* the game server for match flow. It serves
one JSON document and receives callbacks. Direction of control is inverted from what
most people assume.

_Checked: 2026-08-10._

### 2.2 The CS2 plugin layer is inherently fragile

CounterStrikeSharp is a .NET 8 scripting layer over a Metamod plugin that reads raw
signatures and offsets from the CS2 binary via `gamedata.json`. Valve updates break
these regularly:

- [Server segfaults on map change after a CS2 update](https://github.com/roflmuffin/CounterStrikeSharp/issues/1139)
- [Plugins failing to load — undefined symbols in the game library](https://github.com/roflmuffin/CounterStrikeSharp/issues/1342)

**Consequence:** this risk is inherited by *any* CS2 plugin, custom or not. Writing our
own does not avoid it — it only removes the upstream maintainers who currently absorb it.
Any plugin decision must include an answer for "Valve shipped an update and servers are
down."

_Checked: 2026-08-10._

### 2.3 Prior art worth reading before writing anything

| Project | Why it matters |
| --- | --- |
| [matchzy-auto-tournament](https://github.com/sivert-io/matchzy-auto-tournament) | Full CS2 tournament automation: brackets, real-time match tracking, auto server allocation. Possibly overlaps a large share of the intended backend. |
| [MatchZy-Enhanced](https://github.com/sivert-io/MatchZy-Enhanced) | Fork adding richer real-time events specifically for tournament automation. |
| [FSHOST MatchZy fork](https://help.fshost.me/games/cs2/plugins/matchzy) | Fork adding coach system, remote log API, in-game match-setup menus, G5API compatibility. |
| [Get5 / G5API / G5V](https://github.com/shobhit-pathak/MatchZy) | The config schema MatchZy speaks. A worked example of what a match config must contain. |
| [brackets-manager.js](https://github.com/Drarig29/brackets-manager.js) / [brackets-viewer.js](https://github.com/Drarig29/brackets-viewer.js) | v1.6.0 / v1.4.6, actively maintained, storage-agnostic. Reference for bracket data modelling. |

> **These are read, not used.** Per [003](docs/decisions/003-build-basis.md), NEXTSKILL is
> original work. Prior art exists to teach us *which problems are hard* — not to be forked,
> vendored, or copied. The boundary is defined in
> [002](docs/decisions/002-cs2-plugin.md#the-inspiration-not-derivation-boundary):
> **read it, close it, then write ours.**

### 2.4 CS 1.6 integration is possible but entirely hand-rolled

- HTTP out of an AMXX plugin is available and non-blocking via
  [AmxxCurl](https://github.com/Next21Team/AmxxCurl) or the more actively maintained
  AmxxEasyHttp.
- There is **no Get5/MatchZy equivalent.** Veto, knife round, ready-up, score reporting,
  and stat collection would all be written from scratch in Pawn/Small.
- **No demo pipeline.** Demos are client-side `.dem` or ancient HLTV. There is no
  server-side "record and POST the file" path equivalent to GOTV.

The 1.6 competitive scene remains active — notably Turkey, Brazil, Russia/CIS and Poland.

_Checked: 2026-08-10._

### 2.5 CS:GO is effectively dead as a target

Valve replaced CS:GO with CS2 in September 2023. The `-csgo_legacy` branch exists, but
community server population is negligible. Supporting it means a third integration path
for near-zero users.

_Checked: 2026-08-10._

### 2.6 Steam login is still viable

Steam supports [OpenID 2.0 for web authentication](https://partner.steamgames.com/doc/features/auth)
and returns a SteamID. OpenID 2.0 is a deprecated *protocol* in general, but **no Steam
sunset date has been announced.** Treat the login provider as a swappable seam rather
than assuming it is permanent.

_Checked: 2026-08-10._

---

## 3. Engineering principles

These are binding, not aspirational.

1. **Receive, don't orchestrate.** The game server plugin owns match state. The panel
   serves config and consumes callbacks. It does not drive the match tick by tick.
2. **Original code, informed by prior art.** NEXTSKILL is written by us. Existing projects
   are studied to learn which problems are hard, then closed before ours is written.
   No forking, no vendoring, no copy-paste. This is a deliberate, accepted trade of
   speed for ownership and understanding.
3. **Laziness stops at trust boundaries.** Input validation, authn/authz, webhook
   signature verification, secret storage, and accessibility are never simplified away.
4. **Every non-trivial rule leaves one runnable check behind.** Bracket advancement,
   ELO math, and veto state transitions each get a test. No frameworks required.
5. **One database until a profiler disagrees.** Same for caches, queues, and services.
6. **Deliberate shortcuts are commented** with `ponytail:` and name their ceiling and
   upgrade path.

---

## 4. Environment

| Item | Value |
| --- | --- |
| Local repo | `C:\Users\miron\OneDrive\Pulpit\NEXTSKILL PROJECT` (git initialised, branch `main`) |
| GitHub remote | **Not yet created.** `gh` CLI is not installed on this machine. Intended name `NEXT SKILL PROJECT` → GitHub will slugify to `NEXT-SKILL-PROJECT` (repo names cannot contain spaces). Visibility: **private**. |
| Git | `C:\Program Files\Git\cmd\git.exe` |
| OS | Windows 11 Pro |

> ⚠️ The repo lives inside a **OneDrive-synced folder**. OneDrive and `.git` are a known
> bad combination — sync can corrupt object files and lock `index.lock` mid-operation.
> Moving the working copy outside OneDrive is strongly advised before real work starts.

---

## 5. Decision agenda

Each item becomes a file in `docs/decisions/` once resolved. Status values:
`open` → `grilling` → `decided`.

### Cross-cutting (blocks everything downstream)

| # | Decision | Status |
| --- | --- | --- |
| [001](docs/decisions/001-game-scope.md) | Game scope | **decided** — CS2 + CS 1.6 both day one; CS:GO out |
| [002](docs/decisions/002-cs2-plugin.md) | CS2 server plugin | **decided** — original plugin on CounterStrikeSharp, prior art as reference only |
| [003](docs/decisions/003-build-basis.md) | Build basis | **decided** — platform written from scratch, no forking |
| 004 | Language & stack for the web platform | **grilling** |
| 005 | CS 1.6 plugin design (Pawn/AMXX) | open |
| 006 | Architecture shape — monolith vs. services | open |
| 007 | Who hosts the game servers (us, organisers, or both) | open |

### Backend

| # | Decision | Status |
| --- | --- | --- |
| 008 | Database(s): users, matches, server data, stats | open |
| 009 | API design — our own match config + event contract | open |
| 010 | Authentication — Steam OpenID, sessions vs. JWT | open |
| 011 | Authorization — roles, org scoping | open |
| 012 | Security measures — webhook HMAC, RCON secrets, CSRF, headers | open |
| 013 | CS server connection & player redirect (`steam://connect`) | open |
| 014 | Discord integration — OAuth, bot, notifications | open |
| 015 | Admin panel / CMS — scope, roles, what's editable | open |
| 016 | Code optimization posture | open |
| 017 | Docker & deployment topology | open |

### Frontend

| # | Decision | Status |
| --- | --- | --- |
| 018 | Fundamentals — structure, layout, navigation | open |
| 019 | Visual identity — colours, fonts | open |
| 020 | Behaviour — animation & interaction budget | open |
| 021 | UX enhancements | open |
| 022 | Tournament formats — single/double elim, round robin, groups, league | open |
| 023 | Bracket rendering & data model | open |
| 024 | Demo storage, retention, and match detail pages (CS2 only) | open |
| 025 | ELO system | open |
| 026 | Rating calculation | open |
| 027 | Ranking & leaderboards | open |
| 028 | Map veto & lobby UI | open |
| 029 | Profile creation & management | open |
| 030 | Login page | open |
| 031 | Footer design | open |

### Misc

| # | Decision | Status |
| --- | --- | --- |
| 032 | CI/CD — dev → production, GitHub branch/commit verification in admin panel | open |
| 033 | Maintenance mode | open |
| 034 | Test strategy — how a full match is exercised without ten humans | open |

---

## 6. Open risks

| Risk | Impact | Current mitigation |
| --- | --- | --- |
| **Valve CS2 update breaks CounterStrikeSharp** | All CS2 matches stop, and no upstream maintainer is fixing it for us | **None yet.** Needs: pinned CSSharp version, staging server that takes updates first, documented rollback. Owed by 017/033. |
| **Two original game plugins + original platform** | Long path to first live tournament | Accepted deliberately ([002](docs/decisions/002-cs2-plugin.md), [003](docs/decisions/003-build-basis.md)). Mitigate by sequencing, not by cutting quality. |
| **Round backup/restore is ours to build, in both games** | A crashed server kills a live match unrecoverably | Must be verified by actually crashing a server mid-match, not by code review |
| **Premature abstraction over two game lifecycles** | Wrong seam, expensive to undo | Rule of two — build CS2 concretely, then CS 1.6 concretely, then extract ([001](docs/decisions/001-game-scope.md)) |
| CS 1.6 has no demo pipeline | Feature parity impossible across games | Accepted. UI must treat demos as nullable; product must not promise them for 1.6. |
| No simulation mode (we don't inherit MatchZy-Enhanced's) | Can't test a full match cheaply | Owed by decision 034 |
| Repo inside OneDrive | Git corruption, `index.lock` contention | **None yet — move the working copy out of OneDrive** |
| 34 decisions before any code ships | Never ships | Grilling to force a launch-scope cut |
