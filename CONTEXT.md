# NEXTSKILL — Context

The shared, general-purpose document. Anything true across the whole project lives here.
Anything specific to one decision lives in `docs/` as its own file.

**Status:** **design phase complete.** All decisions grilled and recorded. Nothing is built yet.
Next: prototype the three screens flagged as able to surprise us, then begin implementation
per [038](docs/decisions/038-build-order.md).

---

## 1. What NEXTSKILL is

A tournament platform for the **low / community Counter-Strike scene** — community
server owners and grassroots organisers who want to run real tournaments without
FACEIT/ESEA-tier infrastructure or cost.

**Target games — decided ([001](docs/decisions/001-game-scope.md)):** **CS2 and CS 1.6**,
both first-class from day one. CS:GO is explicitly out of scope.

**First partner:** Pukawka (Polish CS community), trusted — see §3b.

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

### 2.6 `setinfo` is a proven CS 1.6 idiom — and its leak vector is known

- AMXX reads client info keys server-side via
  [`get_user_info(index, const info[], output[], len)`](http://amxxmodx.ru/core/amxmodxinc/36-get_user_info-poluchenie-dannyh-iz-setinfo.html),
  pulling values the player set in their `userconfig.cfg`.
- **`setinfo _pw` is the long-established convention for CS 1.6 admin/VIP passwords.**
  Carrying a secret in a client info key is not novel — it is how this community has
  authenticated admins for two decades.
- **The known attack is rogue *servers*, not other players.** A malicious server operator
  can call `get_user_info` and harvest the value: *"when you join bad servers, they steal
  your `_pw` userinfo."* AlliedModders has a long-running
  ["setinfo getting hacked"](https://forums.alliedmods.net/archive/index.php/t-241341.html)
  thread on exactly this.
- No evidence that one **player** can read another player's info keys.

**Consequence:** this is precisely why
[011](docs/decisions/011-accounts-and-login.md) requires **two** factors. A harvested
`setinfo` token is useless without the live, single-use, slot-scoped per-match code.

**Consequence 2 — a product risk, not a technical one:** CS 1.6 players have been burned by
`setinfo` credential theft before and may resist setting a NEXTSKILL token. The onboarding
copy must state plainly that the token is **not a password**, cannot log into the website,
is useless on its own, and is revocable in one click.

_Checked: 2026-08-10._

### 2.7 Steam login is still viable

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

## 3a. How we work on this project

**NEXTSKILL is a learning project as much as a product.** The decision to build from
scratch ([003](docs/decisions/003-build-basis.md)) was made deliberately to understand the
domain, and the same applies to the web stack.

Working agreement:

- **Explain the *why*, in plain terms, as part of the work.** New concepts get one plain
  sentence the first time they appear — not a lecture, not a link dump.
- **Show the failure mode a technique prevents.** "Strict deserialisation" means little;
  "a missing score silently becoming `0` and the wrong team advancing" is what makes it
  stick.
- **Name the trade-off, not just the choice.** Every decision doc in `docs/decisions/`
  records what was given up, not only what was gained.
- **Push back with evidence, not opinion.** Research beats guessing; a test beats an
  assertion.
- **Tutor, don't just narrate.** Claude periodically asks questions back to check
  understanding — after a concept has been introduced and used, or before applying it
  somewhere new. Questions favour reasoning about *this* project ("we're adding a
  leaderboard sort — where's the injection risk, given EF Core?") over textbook recall.
  Wrong answers get the correct reasoning plainly and no dwelling; the work continues
  either way. Quizzes never gate progress.

## 3b. First partner

**Pukawka** — an established Polish CS community — is the intended first organiser and is
a **trusted** partner.

This is recorded as a fact about *partner #1*, not an assumption baked into the
architecture. Trust is modelled as a per-organiser attribute
(`organiser.trusted`), which relaxes the result-confirmation gate for them while leaving it
in place for everyone else. No reputation system is being built speculatively —
but `matches.integrity` ([035](docs/decisions/035-match-result-integrity.md)) exists from
the first migration because adding it later is a painful migration.

Pukawka's involvement also confirms **CS 1.6 is a real requirement, not nostalgia** — which
is what surfaced the identity problem in [010](docs/decisions/010-identity-binding.md).

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
| [004](docs/decisions/004-stack.md) | Language & stack | **decided** — ASP.NET Core API + TypeScript frontend (NSwag-generated types) |
| [035](docs/decisions/035-match-result-integrity.md) | Match result integrity | **decided** — strict deserialisation, event sequencing, end-of-map reconciliation, integrity-gated bracket advancement |
| [005](docs/decisions/005-cs16-plugin.md) | CS 1.6 plugin | **decided** — no veto, no side selection, knife optional. Hard part is round restore: score/side/round only, economy lost |
| [006](docs/decisions/006-architecture.md) | Architecture & folder structure | **decided** — vertical slice (feature folders) inside one deployable. Microservices rejected: they'd break 035's atomicity and make debugging harder |
| [007](docs/decisions/007-server-hosting-model.md) | Server hosting model | **decided** — organisers bring their own servers; NEXTSKILL hosts none |

### Backend

| # | Decision | Status |
| --- | --- | --- |
| [008](docs/decisions/008-database.md) | Database | **decided** — one PostgreSQL; no Redis yet; demo files on disk, metadata rows only |
| [009](docs/decisions/009-api-contract.md) | API contract | **decided** — REST, HMAC-signed, versioned, **plugin-initiated in every direction**; plugin retries until acknowledged |
| [010](docs/decisions/010-identity-binding.md) | Player identity & in-game binding | **decided** — identity is the web account; 1.6 sessions bound by single-use link code, never by SteamID |
| [011](docs/decisions/011-accounts-and-login.md) | Accounts, login & two-factor binding | **decided** — native account primary, Steam/Discord optional links; in-game binding requires `setinfo` token **and** per-match code |
| [040](docs/decisions/040-threat-model.md) | **Threat model** | **decided** — threat-by-threat; top risks are **SSRF (designed into server registration)**, IDOR/tenant leakage, and demo decompression bombs |
| [012](docs/decisions/012-security.md) | Security measures | **decided** — HMAC + replay window, demo-upload hardening, secrets never logged, rate limits, GDPR retention/anonymisation |
| [013](docs/decisions/013-server-connect.md) | Connecting players to the server | **decided** — **always show both** `steam://` button and copyable console command; `steam://` silently fails for non-Steam 1.6 players |
| [014](docs/decisions/014-discord.md) | Discord integration | **decided** — outgoing **webhooks only**, no bot. Webhook URL is a credential *and* an SSRF sink |
| [015](docs/decisions/015-admin-panel.md) | Admin panel & CMS | **decided** — fixed formats + parameters + clonable templates. **No rule engine.** CMS manages news, static pages, map pools — not layouts |
| [016](docs/decisions/017-deployment-and-cicd.md#code-optimisation-016) | Code optimisation | **decided** — no optimisation without measurement; only design-time choices (indexes, N+1 avoidance, cursor pagination) done up front |
| [017](docs/decisions/017-deployment-and-cicd.md) | Deployment & Docker | **decided** — Docker Compose, single EU host, CDN in front, no Kubernetes, no Redis. **Tested restores, not just backups** |

### Frontend

| # | Decision | Status |
| --- | --- | --- |
| [018](docs/decisions/018-frontend-architecture.md) | Frontend architecture & i18n | **decided** — Razor Pages + TS islands; **4 locales** (pl primary, en/ru/de) with locale in the URL; no Node runtime |
| [043](docs/decisions/043-page-contents.md) | **Page contents** | **decided** — home leads with activity + announcements; in-app inbox + email; trigram search; state-driven tournament page; **NEXTSKILL as first-party organiser** |
| [041](docs/decisions/041-layout-system.md) | **Layout system** | **decided** — five page archetypes, three content widths, 4px spacing scale, two breakpoints, 18 components |
| [019](docs/decisions/019-visual-identity.md) | Visual identity | **decided in principle** — Utilitarian system + Tenebrism for finals + Rebus icons; CT/T side colours carry information |
| [020](docs/decisions/020-motion-and-ux.md) | Motion & UX behaviour | **decided** — motion marks state change only; progressive enhancement is a hard rule; empty states are a designed surface |
| [021](docs/decisions/021-site-structure.md) | Site structure, features & navigation | **decided** — four surfaces mirroring the role model; **+LFT board, news/CMS, seasons**; headbar public, sidebar panels |
| [022](docs/decisions/022-tournament-formats.md) | Tournament formats | **decided** — all five at launch, built as **two engines** (elimination + round-robin) composed into five formats |
| [023](docs/decisions/023-bracket.md) | Bracket data model & rendering | **decided** — whole bracket generated up front with **explicit routing links**; mobile gets round-by-round + follow-team, not a shrunken tree |
| [024](docs/decisions/024-demos.md) | Demo storage & retention | **decided** — 90 days, then deleted; finals and disputes kept indefinitely; local disk, authorised endpoint only |
| [025](docs/decisions/025-rating-system.md) | Rating, ranking & tiers | **decided** — one system, three layers. Team = **Glicko-2**, player = **OpenSkill**. Ranking/tiers are derived views, never stored |
| [028](docs/decisions/028-veto-and-lobby.md) | Map veto & lobby | **decided** — **veto runs on the web**, not in-game. Removes a state machine from both plugins and takes side selection with it |
| [029](docs/decisions/029-profile-login-footer.md) | Profile, login & footer | **decided** — rotate-never-retrieve tokens, errors that reveal nothing, footer carries obligations not marketing |

### Misc

| # | Decision | Status |
| --- | --- | --- |
| [032](docs/decisions/017-deployment-and-cicd.md#decision-no-deploy-panel--deliberately-skipped) | CI/CD | **decided** — GitHub Actions on push to `main`. **Deploy panel deliberately skipped** — it is remote code execution as a feature. Revisit when someone needs to deploy without repo access |
| [033](docs/decisions/033-maintenance-mode.md) | Maintenance mode | **decided** — **degrade, don't stop**. Matches run on organisers' servers, so events stay accepted while new matches are blocked |
| [034](docs/decisions/034-test-strategy.md) | Test strategy | **decided** — four layers; the **plugin simulator** is the centrepiece and covers flow, integrity, security and identity in one component |
| [035](docs/decisions/035-match-result-integrity.md) | Match result integrity | **decided** |
| [036](docs/decisions/036-authorization.md) | Authorization & multi-tenancy | **decided** — EF Core global query filters make tenant scoping the default; resource-based authz, never ad-hoc role checks |
| [042](docs/decisions/042-hosting.md) | Hosting, CDN & object storage | **decided** — Cloudflare free + **R2** (zero egress) + small EU VPS. Free tiers rejected with reasons |
| [037](docs/decisions/037-platform-hosting.md) | Platform hosting & tenancy | **decided** — single hosted instance on our own domain; organisers are tenants, not installers |
| [038](docs/decisions/038-build-order.md) | Build order | **decided** — web platform first; plugins replaced by a real HTTP **simulator**, not a stub |
| [039](docs/decisions/039-public-api.md) | Public API + API style | **decided** — REST (gRPC impossible for CS 1.6, GraphQL solves a problem we don't have); **SSE** for live updates, not WebSockets |

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
| **We store third-party RCON passwords** | A breach hands attackers control of hundreds of community servers. Far worse than a normal credential leak. | Encrypted at rest with an out-of-DB key; never returned by any API; never logged; redacted from staff view. **Best mitigation: find out whether RCON is needed at all** ([007](docs/decisions/007-server-hosting-model.md)) |
| **Malicious organiser fabricates a result** | Match-fixing. Inherent to BYO servers; not solvable at the protocol level | Corroboration: both-team confirmation, demo evidence (CS2 only), dispute flow, challenge window before ratings settle |
| **We don't control plugin versions on BYO servers** | Old plugins send stale contracts; we cannot force upgrades | Plugin reports version; panel refuses match allocation on incompatible versions; contract carries a version field; deprecation windows |
| Repo inside OneDrive | Git corruption, `index.lock` contention | **None yet — move the working copy out of OneDrive** |
| 34 decisions before any code ships | Never ships | Grilling to force a launch-scope cut |

---

## 7. Verification backlog

Claims that decisions currently rest on but which have **not** been tested. Each must be
verified before code depends on it — per project instruction, test before acknowledging.

| # | Assumption | Blocks | How to verify |
| --- | --- | --- | --- |
| ~~V1~~ | ~~Other players cannot read another player's `setinfo` value~~ | — | **RESOLVED 2026-08-10.** Documented threat is rogue *servers* harvesting via `get_user_info`, not players. Two-factor already covers it. See §2.6. |
| ~~V2~~ | ~~`setinfo` readable server-side via AMXX~~ | — | **CONFIRMED 2026-08-10.** `get_user_info()` reads client info keys. Connect-time timing still worth confirming on a live server. |
| V2b | Values are readable **early enough at connect** to gate team joining | [011](docs/decisions/011-accounts-and-login.md) | Log the value in a connect hook on a real server |
| V3 | `setinfo` behaves consistently on **non-Steam clients** (Reunion/dproto), not just legit Steam | [011](docs/decisions/011-accounts-and-login.md) | Test with both client types |
| V4 | Info-key length limits allow a high-entropy token | [011](docs/decisions/011-accounts-and-login.md) | Set progressively longer values, find the ceiling |
| V5 | RCON is **not** required for the normal CS2 match lifecycle | [007](docs/decisions/007-server-hosting-model.md) — if true, we avoid storing RCON passwords entirely | **Expected to resolve favourably** — [009](docs/decisions/009-api-contract.md) made the contract outbound-only, so the panel never dials the game server. Confirm with a full CS2 match |
| V6 | CS2 can expose authoritative final match state for reconciliation | [035](docs/decisions/035-match-result-integrity.md) | Inspect what a map-end payload can contain |
| V7 | Steam OpenID 2.0 remains available with no announced sunset | [011](docs/decisions/011-accounts-and-login.md) | Re-check Steamworks docs periodically |

**V1 is the highest priority.** If other players can read a `setinfo` token, it is not a
secret, the second factor collapses to a public identifier, and
[011](docs/decisions/011-accounts-and-login.md) needs redesigning.
