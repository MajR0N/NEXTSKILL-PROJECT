# NEXTSKILL — Context

The shared, general-purpose document. Anything true across the whole project lives here.
Anything specific to one decision lives in `docs/` as its own file.

**Status:** **building.** Phases 0–3 complete; **Phase 4 (web platform) past its keystone**.
**358 checks passing** against a real PostgreSQL, including in-process end-to-end runs of the
plugin pipeline, the tournament and bracket flow, and the whole authentication surface.

> **The definition-of-done sentence is now true in code (2026-08-17):** a bracket advances
> itself from a result the server reported, and that result settles into a rating. Phases 0–3
> built the rating engines, the wire contract and the integrity rules while none of them was
> connected to anything; [050](docs/decisions/050-match-results-and-ratings.md) is the join,
> and it is built.

### What actually runs today

| Working | Where |
| --- | --- |
| Accounts — register, login, logout, **account-wide session revocation**, **password reset**, **two-factor (TOTP + SMS)**, recovery codes, private `/me`, rotate-only `setinfo` tokens, verified Steam/Discord links, GDPR erasure with anonymous history tombstones and zero persisted application IPs | [011](docs/decisions/011-accounts-and-login.md), [012](docs/decisions/012-security.md), [029](docs/decisions/029-profile-login-footer.md), [047](docs/decisions/047-two-factor.md) |
| Teams — create, invite, accept, roster slots (5 + 3 + 2), promote, edit, disband and captain-authorised restoration | [045](docs/decisions/045-teams-and-rosters.md) |
| Tournaments — create, private drafts, reusable organiser templates, lifecycle-safe editing, season assignment, registration, seeding, captain check-in, audited event forfeits, and durable Discord announcements for registration/start/confirmed results | [014](docs/decisions/014-discord.md), [015](docs/decisions/015-admin-panel.md), [021](docs/decisions/021-site-structure.md), [046](docs/decisions/046-tournament-lifecycle.md) |
| Brackets — generated into the database on start, routing as real foreign keys, byes and forfeit vacancies resolved, **the grand-final reset armed live**, and a phone gets one round at a time rather than a shrunken tree | [023](docs/decisions/023-bracket.md) |
| Ranking — team + player views, provisional separation, search and cursor pagination | [025](docs/decisions/025-rating-system.md) |
| Player profiles — public ratings/team/LFT history plus a private account and security surface | [029](docs/decisions/029-profile-login-footer.md) |
| Plugin API — HMAC-signed ingest, sequencing, reconciliation, integrity gates | [009](docs/decisions/009-api-contract.md), [035](docs/decisions/035-match-result-integrity.md) |
| Organiser servers — tenant-authorised `host:port` registration, one-time HMAC secret, write-only health view, and verification by the first valid signed heartbeat | [007](docs/decisions/007-server-hosting-model.md), [048](docs/decisions/048-organiser-server-configuration.md), [051](docs/decisions/051-phase-4-remainder.md) |
| Match lobby and web veto — captain readiness, durable legal turns, lazy 30-second expiry, BO1/BO3/BO5 map-and-side output, and plugin config from the frozen result | [028](docs/decisions/028-veto-and-lobby.md), [051](docs/decisions/051-phase-4-remainder.md) |
| **Match results → ratings** — a reported `map_end` stores the winner and advances the bracket; lineups declare themselves at `match_loaded`; a `player_bound` event carrying **both** factors promotes `Declared` → `Bound`; an append-only ledger and a background sweeper settle teams (Glicko-2) and players (OpenSkill) in `(EndedAt, Id)` order. Captains confirm or dispute; `organiser.trusted` skips the wait but never the integrity gate | [050](docs/decisions/050-match-results-and-ratings.md), [025](docs/decisions/025-rating-system.md), [035](docs/decisions/035-match-result-integrity.md) |
| **Referee decisions** — `/org/disputes` dismisses a captain's claim or reverses a result, and a reversal supersedes the ledger and replays every rating from history | [036](docs/decisions/036-authorization.md), [050](docs/decisions/050-match-results-and-ratings.md) |
| **Audit log** — append-only *enforced by PostgreSQL*, recording actor, resource, reason and **outcome**, so a **denied** privileged attempt leaves a row even though it changed nothing | [012](docs/decisions/012-security.md), [036](docs/decisions/036-authorization.md), [051](docs/decisions/051-phase-4-remainder.md) |
| **Home page** — live matches, announcements, upcoming events, recent results and the CS2 top five, each with a designed empty state | [043](docs/decisions/043-page-contents.md), [020](docs/decisions/020-motion-and-ux.md) |
| **Notifications** — an inbox and a server-rendered unread count; invites and referee alerts are finally readable | [043](docs/decisions/043-page-contents.md) |
| **Search** — one GET form over teams, players and tournaments, accent-insensitive both ways via an `IMMUTABLE` `f_unaccent` wrapper and trigram indexes; works with JavaScript off | [043](docs/decisions/043-page-contents.md), [051](docs/decisions/051-phase-4-remainder.md) |
| **Seasons archive** — `/seasons/{n}` read as at the closing instant, with a database constraint refusing to close a season without recording when | [021](docs/decisions/021-site-structure.md), [051](docs/decisions/051-phase-4-remainder.md) |
| **News** — `/news`, scheduled posts invisible until due, markdown with raw HTML disabled and a link-scheme allow-list applied to the parsed document | [015](docs/decisions/015-admin-panel.md), [040](docs/decisions/040-threat-model.md), [051](docs/decisions/051-phase-4-remainder.md) |
| **Competitive history** — integrity-aware team record and ledger-backed rating trend; player match history labelled by provenance | [049](docs/decisions/049-team-page.md), [029](docs/decisions/029-profile-login-footer.md) |
| **Staff panel** — `/admin/*`: the `trusted` flag finally has a user interface, bans end every live session and are refused for staff and the owner, the audit log has a reader with a denied-only filter, the **news CMS** finally has an author, reported adverts have a **moderation queue**, and **account merges** are owner-only | [015](docs/decisions/015-admin-panel.md), [036](docs/decisions/036-authorization.md) |
| **Organiser panel** — `/org/*`: servers, the referee's desk, and **staff roles** — every organiser-scoped permission reads `organiser_staff`, and until now nothing could write it. All panel sections are reachable from a navigation strip that asks operations rather than roles | [015](docs/decisions/015-admin-panel.md), [021](docs/decisions/021-site-structure.md), [036](docs/decisions/036-authorization.md) |
| **LFT board** — `/lft`, two explicit sides, freshness-ordered with rating never buying prominence; a team approach always names a real opening; ten pending per side; mutual acceptance reveals one verified contact each way for 30 days, and there is no recruitment chat. **Weekly availability** is captured per weekday and compared as genuine overlap across time zones; adverts can be **reported** and moderated | [056](docs/decisions/056-lft-board.md) |
| **Secrets encrypted at rest** — the signing credential, the TOTP seed and the per-match link code are encrypted with a key held **outside** the database; none can be hashed, since each must be replayed. A startup backfill closes the pre-encryption window | [012](docs/decisions/012-security.md) |
| **Rate limiting behind a proxy** — the auth limiter partitions on the visitor's address, recovered from `X-Forwarded-For` **only** for requests arriving from a configured network. Empty configuration leaves it inert | [011](docs/decisions/011-accounts-and-login.md), [042](docs/decisions/042-hosting.md) |

### The three things that most need saying

1. **Localisation is now a habit, not yet a translation set.** The shared shell reads
   `IStringLocalizer<UiText>` from a neutral Polish `.resx`; `/de/...` serves that Polish by
   designed framework fallback. Existing page-specific copy remains hardcoded until each page is
   touched, and no en/ru/de translation catalog exists yet.
2. **The product ships monochrome on purpose** until [044](docs/decisions/044-visual-direction.md)
   closes. Colour is an open question, not an oversight.
3. **Phase 4 is nearly complete, and what remains is named.** Match completion, ratings, the
   referee's desk, both panels, the LFT board with availability and moderation, the home page,
   notifications, search, seasons, news with an editor, and competitive history all run end to end.
   **Every buildable item is now built.** What remains is blocked on something outside Phase 4:
   - **Team trophies**, blocked on placement standings: nothing computes a top-three finish beyond
     the bracket winner.
   - **Team logos**, blocked on [044](docs/decisions/044-visual-direction.md), and needing upload
     validation, storage and moderation decisions of their own.
   - **A real email/SMS provider** and deliverability, blocked on production credentials. The seam
     exists and **fails closed**, so production refuses to start without a real sender.
   - **Authentication events in the audit log**, deferred with the reader that would show them.
   - **Maintenance mode** ([033](docs/decisions/033-maintenance-mode.md)) is Phase 7 by design.
   - The organiser server inventory stays unusable by a real organiser until the Phase 6 plugin can
     send a signed heartbeat — which is the real gate on a first live cup, not anything in Phase 4.

   See [BUILD-PLAN](docs/BUILD-PLAN.md) for the itemised list, which is kept honest rather than
   flattering.

### Team-page language

**Competitive game context:** the selected CS2 or CS 1.6 view governing every rating, match,
trophy and recruitment fact. Team identity and roster remain shared.

**Trophy:** an authoritative top-three placement from a NEXTSKILL or trusted-organiser event.
An untrusted event result remains history, not a self-issued profile credential.

**Open slot:** a captain-published, game-specific recruitment position. Spare roster capacity
alone is not an open slot.

**Application:** a player's private expression of interest in one open slot. It grants no team
membership; joining still requires the ordinary invitation and acceptance flow.

### Recruitment language

**LFT board:** one public, server-rendered recruitment marketplace with separate **teams
recruiting** and **players looking for teams** views under a page-wide CS2 / CS 1.6 context. It
defaults predictably to CS2 openings; filters are explicit and freshness-ordered, never an opaque
compatibility or highest-rating feed.

**Recruitment cycle:** one fourteen-day publication of a structured player listing or team opening.
Material compatibility changes and explicit renewal start a fresh cycle; note edits do not. Players
may list while rostered or unrated, with those facts displayed honestly.

**Team interest:** a captain's private, reversible approach to an LFT player on behalf of one real
opening. It is not the binding roster invitation and cannot become context-free direct messaging.

**Mutual contact:** acceptance of an application or team interest temporarily reveals only each
side's chosen verified Discord/Steam contact. Conversation leaves NEXTSKILL; there is no recruitment
chat. Only a later accepted roster invitation counts as recruitment success.

The full board, compatibility, consent, lifecycle, abuse, notification and retention contract is
[056](docs/decisions/056-lft-board.md).

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

### 2.5 CS:GO — ~~effectively dead~~ **re-checked, and the picture changed**

Valve replaced CS:GO with CS2 in September 2023.

> **Corrected 2026-08-17.** The earlier entry said CS:GO was reachable only through a
> `-csgo_legacy` branch with negligible community population. **That framing is out of date.**
> On **3–4 March 2026** Valve put up a **separate, unlisted CS:GO store page** — a frozen
> build with **no official matchmaking**, playable only against bots or **on community
> servers**
> ([Godlike](https://godlike.host/csgo-back-on-steam-in-2026-the-new-csgo-steam-page-what-it-means-and-the-catch-blog/),
> [HLTV](https://www.hltv.org/news/37221/csgo-remains-available-on-steam-in-frozen-build)).
> App ID reported as **4465480**
> ([cs2.ad](https://cs2.ad/blog/csgo-back-on-steam)) — **single-sourced, see V8**.

Two consequences, both recorded in [057](docs/decisions/057-server-browser-and-seo.md):

1. A game whose *only* online option is community servers is exactly the audience a server
   list serves — so CS:GO is listed, and [001](docs/decisions/001-game-scope.md) is reopened
   to admit CS:GO tournaments.
2. **CS:GO is the only one of the three games where plugin risk is permanently zero.** The
   top open risk below is "a Valve update breaks CounterStrikeSharp"; a frozen build cannot
   break a plugin. It also runs SourceMod/Metamod, a decade more mature, with Get5 as prior
   art.

The population itself is still uncounted — [055](docs/decisions/055-multi-game-scope.md)'s
open question. The server listing measures it for free before any plugin work is committed.

_Checked: 2026-08-10; corrected and re-checked 2026-08-17._

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
and returns a SteamID. OpenID 2.0 is a legacy *protocol* in general, so the provider remains
behind a swappable seam and its official documentation must be re-checked periodically.

_Checked: 2026-08-11._

---

### 2.8 EasyCheatDetector (ECD) — checked at source, and it is not what it looks like

The Polish/CIS CS 1.6 scene already runs free client-side cheat scanners. ECD is the most
prominent, and it was assumed to be a live client anti-cheat. **It is not.**

| | |
| --- | --- |
| **Games** | CS 1.6 **95%**, CS:S, **CS2 50%**, HL1/HL2 mods, Minecraft; RUST 0% (in development) |
| **Detects** | Running processes, loaded modules, active drivers, cheat traces (installed / run / downloaded) |
| **Mode** | **On-demand scanner.** The user runs the executable and it produces a `report.ecd`. **Not real-time, not a live anti-cheat** |
| **Transmits** | SteamID and partial IP, AES-256 + RSA-2048. **No hardware identifier** |
| **Licence** | **None stated** — no `LICENSE` file, binary-only releases, no source published |
| **Integration** | No API, no webhook. An offline Report Viewer only |

**Three consequences, all load-bearing for
[058](docs/decisions/058-what-we-sell.md) §6b–§6c:**

1. **It cannot be the client half of a live anti-cheat product**, because it does not run during a
   match. What it *is* — and this is genuinely useful — is the *"run ECD and post your report"*
   workflow the scene already uses, which fits a referee's dispute flow exactly.
2. **We cannot ship or bundle it.** No licence means all rights reserved by default. Any plan that
   redistributes it needs written permission first, which is why
   [058](docs/decisions/058-what-we-sell.md) §6b makes a signed partnership a **precondition**
   rather than an opportunity.
3. **CS2 coverage is 50%**, so leaning on it for CS2 leaves half the problem unsolved. This is the
   concrete gap that justifies writing our own scanner — a capability argument, not a branding one.

Contact is public (Telegram `@karaul0v`, `karaulov@fungun.net`) should a partnership ever be
pursued.

Sources: [github.com/UnrealKaraulov/EasyCheatDetector](https://github.com/UnrealKaraulov/EasyCheatDetector/),
[fungun.top/ecd](https://fungun.top/ecd/).

_Checked: 2026-08-22._

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
7. **The product exists to remove manual work, so a feature that adds manual work must justify
   itself.** Organisers told us the wound directly: *"organization — a lot of delays, manual work,
   no free time"*, **3–5 people per event**, registration on a forum that is *"tedious and
   unprofessional"*. Every step that asks a human to click something is measured against that.
   A blocking gate must beat the automated alternative at the same job — otherwise it is a
   non-blocking default with an opt-out, and the existing forfeit/expiry machinery handles whoever
   does not act. Advancement, seeding and ratings are already automatic, and **check-in was the one
   surviving blocking gate — now replaced by three layers** ([046](docs/decisions/046-tournament-lifecycle.md)):
   automatic on registration close, optional explicit confirmation for captains who want to deviate
   or withdraw, and presence at match time as ground truth with auto-forfeit after a grace window.

---

## 2a. Strategy

Go-to-market, positioning and the commercial model live in
[MARKETING-AND-SALES-STRATEGY.md](docs/MARKETING-AND-SALES-STRATEGY.md), **revised 2026-08-12** after
research, strategy grilling and the LFT boundary pass. **Scope is Poland first.**

Five points from it that constrain engineering:

1. **Organisers are the customer**, tier 2–5 teams are the demand, players are the reason the
   product exists. One converted organiser brings dozens of players; one converted player brings
   nobody.
2. **Growth is shared links, not advertising — and the channel is Discord**, because community
   forums are dying. Server rendering and Open Graph cards are therefore load-bearing rather than
   cosmetic ([018](docs/decisions/018-frontend-architecture.md)), and **the OG card should move
   earlier than Phase 5d**: it is currently a `<meta>` tag and it is the entire top of the funnel.
3. **The gap is structural, and it is not CS 1.6 alone.** Every alternative — FACEIT, CYBERSHOKE,
   FastCup, Starter League, G5API — forces a community to choose between *owning the event* and
   *having a rating anyone recognises*. NEXTSKILL is the only one offering both, and it is only
   able to because tournaments are tenant-scoped while ratings are global
   ([036](docs/decisions/036-authorization.md)). **The architecture is the positioning.**
4. **Volume is a product requirement, not a growth metric.** Glicko-2 keeps teams `provisional`
   until RD settles ([025](docs/decisions/025-rating-system.md)), so sporadic cups mean nobody is
   ever ranked — and the acknowledgment promised to tier 2–5 teams silently never arrives. This is
   why a continuous first-party ladder runs alongside community cups.
5. **CS 1.6 is still a moat, but a narrower one than claimed.** [FastCup](https://cs.fastcup.net/)
   already serves CS 1.6 matchmaking in CIS, so "nobody else serves 1.6" is false. What remains
   unserved is *community-hosted 1.6 competition* — which keeps **V3** (does `setinfo` behave the
   same on non-Steam clients?) the single biggest product risk on the board.

> **Cheating now has its own model.** [052](docs/decisions/052-cheating-and-rating-integrity.md)
> defends the record with layered evidence, optional organiser-required user-mode anti-cheat and
> reversible ratings. Kernel anti-cheat remains behind explicit preconditions rather than becoming
> an accidental launch dependency.

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
| GitHub remote | CREATED NAMED `NEXT-SKILL-PROJECT`. Visibility: **private**. |
| Git | `C:\Program Files\Git\cmd\git.exe` |
| OS | Windows 11 Pro |

> ✅ **Resolved 2026-08-11.** The path still *reads* `…\OneDrive\…`, but **OneDrive sync is
> disabled** — it is an ordinary folder now. The known-bad combination of a sync client and
> `.git` (corrupted objects, `index.lock` held mid-operation) no longer applies. The directory
> name is cosmetic; renaming it is optional and would only break existing shell history.

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
| [040](docs/decisions/040-threat-model.md) | **Threat model** | **decided** — threat-by-threat; IDOR/tenant leakage and demo decompression bombs remain major risks. [051](docs/decisions/051-phase-4-remainder.md) removed the original server-registration SSRF sink by deciding that the panel never dials game servers |
| [012](docs/decisions/012-security.md) | Security measures | **decided** — HMAC + replay window, demo-upload hardening, secrets never logged, rate limits, GDPR retention/anonymisation |
| [013](docs/decisions/013-server-connect.md) | Connecting players to the server | **decided** — **always show both** `steam://` button and copyable console command; `steam://` silently fails for non-Steam 1.6 players |
| [014](docs/decisions/014-discord.md) | Discord integration | **decided** — outgoing **webhooks only**, no bot. Webhook URL is a credential *and* an SSRF sink |
| [015](docs/decisions/015-admin-panel.md) | Admin panel & CMS | **decided** — fixed formats + parameters + clonable organiser templates. **No rule engine.** Tournament template snapshots and lifecycle-safe editing are implemented; CMS manages news, static pages, map pools — not layouts |
| [016](docs/decisions/017-deployment-and-cicd.md#code-optimisation-016) | Code optimisation | **decided** — no optimisation without measurement; only design-time choices (indexes, N+1 avoidance, cursor pagination) done up front |
| [017](docs/decisions/017-deployment-and-cicd.md) | Deployment & Docker | **decided** — Docker Compose, single EU host, CDN in front, no Kubernetes, no Redis. **Tested restores, not just backups** |

### Frontend

| # | Decision | Status |
| --- | --- | --- |
| [018](docs/decisions/018-frontend-architecture.md) | Frontend architecture & i18n | **decided** — Razor Pages + TS islands; **4 locales** (pl primary, en/ru/de) with locale in the URL; neutral Polish `UiText.resx` and the localised shared shell are built; page copy retrofits when touched; no Node runtime |
| [043](docs/decisions/043-page-contents.md) | **Page contents** | **decided** — home leads with activity + announcements; in-app inbox + email; trigram search; state-driven tournament page; **NEXTSKILL as first-party organiser** |
| [041](docs/decisions/041-layout-system.md) | **Layout system** | **decided** — five page archetypes, three content widths, 4px spacing scale, two breakpoints, 18 components |
| [019](docs/decisions/019-visual-identity.md) | Visual identity | **split** — the *behavioural* rules hold (colour never carries meaning alone, uncertainty shown, spacing is information). The **palette is reopened**: CT/T colours were picked in one pass and never argued against |
| [044](docs/decisions/044-visual-direction.md) | **Visual direction** | 🔴 **OPEN — needs grilling.** Palette, type stack, named style, light theme, OG card. **The product ships monochrome until it closes**, so there is no provisional colour to unpick later |
| [020](docs/decisions/020-motion-and-ux.md) | Motion & UX behaviour | **decided** — motion marks state change only; progressive enhancement is a hard rule; empty states are a designed surface |
| [021](docs/decisions/021-site-structure.md) | Site structure, features & navigation | **decided** — four surfaces mirroring the role model; **+LFT board, news/CMS, seasons**; headbar public, sidebar panels |
| [022](docs/decisions/022-tournament-formats.md) | Tournament formats | **decided** — all five at launch, built as **two engines** (elimination + round-robin) composed into five formats. Team sizes are **1v1, 2v2, 5v5** ([045](docs/decisions/045-teams-and-rosters.md)) |
| [046](docs/decisions/046-tournament-lifecycle.md) | **Tournament lifecycle** | **decided** — state machine as a pure function; drafts private; entry requires enough *fieldable* players; seeding on close puts provisional teams last and is deterministic |
| [047](docs/decisions/047-two-factor.md) | **Two-factor & password reset** | **decided** — reset links single-use and 5 minutes; TOTP (any authenticator) preferred, SMS offered but named as weaker; recovery codes issued with the factor; messaging seam fails closed; token-bucket rate limiting |
| [048](docs/decisions/048-organiser-server-configuration.md) | **Organiser server & plugin config** | 🔴 **OPEN** — BYO servers, a plugin *list*, per-tournament config. Also a **sales position**, cross-linked to the strategy |
| [049](docs/decisions/049-team-page.md) | **Team page competitive history & recruitment** | **decided** — one shareable game context; integrity-aware history and record; accessible rating trend; trusted authoritative trophies; intentional expiring openings with private applications; monograms remain until 044 closes |
| [050](docs/decisions/050-match-results-and-ratings.md) | **How a result becomes a rating** | **decided** — the join nothing had: `MatchParticipant` with `Declared`/`Bound` provenance, lineup frozen at check-in, an **append-only rating ledger** whose unique index *is* the sweeper's idempotency, and one rule for both human actions — **judgement holds the reversible thing (the rating), never the irreversible one (the bracket)**. `organiser.trusted` finally means something. Bind in, demos out |
| [051](docs/decisions/051-phase-4-remainder.md) | **Phase 4 remainder** | **decided** — veto before matches (server-rendered, SSE is Phase 5); **we never dial a game server**, which deletes 040's top risk; localisation habit now with Polish as the *neutral* resource; seasons frozen by timestamp not snapshot; recruitment is three objects because an invite costs a roster place and interest does not; search SQL corrected (`unaccent` is not `IMMUTABLE`); audit log exists because a **denied** attempt has no row; `DisableHtml()` alone does not stop `javascript:` links |
| [052](docs/decisions/052-cheating-and-rating-integrity.md) | **Cheating & rating integrity** | **decided** — **zero-install and kernel anti-cheat are the same decision seen from two ends**, recorded as a deliberate trade. Defend the *record*, not the machine: the team-based scheduled format is the first defence, Steam ban checks and demos are the cheap signals, and [050](docs/decisions/050-match-results-and-ratings.md)'s ledger already makes reversal a recompute. No fork (AGPL contagion, public detections, [003](docs/decisions/003-build-basis.md)). **Three tiers: none by default, an optional user-mode client an organiser may require per event, and a kernel driver reserved behind four hard preconditions** — whether the client ran is recorded *on the match* as evidence, never as an automatic verdict. **The labelled corpus is the moat, not the detector**, so demo retention and referee verdicts are load-bearing from day one |
| [053](docs/decisions/053-rewards-and-prize-pools.md) | **Rewards & prize pools** | **decided** — Valve removed progression from community servers; rewards are how we give it back. **Earned, never drawn**: no chance mechanics, because a deterministic reward *is* another verifiable fact on the record while a random one imports a permanent regulatory question. Three tiers — status (free, already built), **community-funded stackable attributed pools** (a feature, not a cost), deterministic perks. We never hold anyone's prize money |
| [054](docs/decisions/054-becoming-a-rated-player.md) | **Becoming a rated player** | **decided** — the *"this counted"* moment must land in-session or it never lands. **CS2 plays first and claims after** (verified SteamID, one-click Steam OpenID attaches the whole history); **CS 1.6 registers first and binds with two factors**, because [010](docs/decisions/010-identity-binding.md) proved its identity spoofable. Not an inconsistency — the lazy path exists in one game and is a hole in the other. Claiming reuses [050](docs/decisions/050-match-results-and-ratings.md)'s promotion shape |
| [055](docs/decisions/055-multi-game-scope.md) | **Multi-game scope** | **decided — deferred on purpose, ladder fixed.** Nothing built, nothing narrowed: `Match.Game`, `jsonb` payloads and pure engines are already game-agnostic, and [006](docs/decisions/006-architecture.md)'s rule of two forbids generalising before one game runs end to end. **Tier A** = plugin games (the moat); **Tier B** = official tournament APIs, and **LoL via Riot Tournament API is the only viable non-CS first step** because its results callback is the same inversion of control as our plugin contract; **Tier C** (CoD, Rocket League, Clash Royale) is refused — no community servers, no rating gap, different customer, and a manual-score path would make *"results are reported, not entered"* false in our own product. CS:GO stays out pending a ten-minute population count |
| [057](docs/decisions/057-server-browser-and-seo.md) | **Server browser, verified activity & SEO** | **decided** — a second front door for the query Discord links never reach. **Counts carry three confidence tiers** — `Verified` from the plugin, `Plausible`/`Implausible` inferred from a stored public time series — because the snapshot is identical for everyone and **nobody stores it**, so the differentiation is analysis rather than access. Show the graph, never a verdict; the remedy for a disputed inference is installing the plugin, so accusation and call-to-action are one sentence. **We still never dial a game server** ([051](docs/decisions/051-phase-4-remainder.md) §2 holds): CS 1.6 counts come from our plugin with bots separated, CS2/CS:GO from the Steam API — so the 1.6 catalogue is small and **verified**, and being listed is the reason an owner installs the plugin Phase 6 needs anyway. **Only players the server confirmed were playing may vote**, which makes the market's most-gamed mechanic unbotable. Two surfaces: a list ordered by live humans, a monthly ranking ordered by votes and frozen forever. **Money buys labelled placement beside a list, never a position inside one.** Programmatic SEO behind a **content floor**, because scaled thin pages would demote the whole domain. Aggregate activity always, **named presence opt-in**. CS:GO listed and [001](docs/decisions/001-game-scope.md) reopened for it. Built after Phase 5 |
| [056](docs/decisions/056-lft-board.md) | **LFT board & recruitment marketplace** | **decided** — one two-sided, game-scoped board ordered by freshness; structured compatibility with unrated/rostered players allowed; real openings on every team approach; bounded applications/interests; verified contact revealed only by mutual consent and then taken off-platform; `/me/recruitment`, explicit expiry, blocking and short retention. **No mixed feed, self-rated skill, opaque matching or recruitment chat** |
| [045](docs/decisions/045-teams-and-rosters.md) | **Teams: roster size, edit, disband, restore** | **decided** — **5 players + 3 subs + 2 coaches**, capped per slot. Slot is a second axis to role, so a coach can captain without being fieldable. Pending invites occupy a place; disband is soft and reserves the tag; a captain present at disband may restore the same team without rewriting old roster history |
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
| [058](docs/decisions/058-what-we-sell.md) | **What we sell** | **decided** — five lines: **boost**, **server-side anti-cheat**, a standalone plugin licence, managed CS 1.6 servers, sponsored slots. **Free where it feeds the rating, the catalogue or the corpus; paid on top.** **Overturns [057](docs/decisions/057-server-browser-and-seo.md) §5** — money buys position inside the list, but **only a `Verified` server may be boosted**. Anti-cheat is two components: a **sold server-side AC** and a **free on-demand scanner**, with suspicion as a meter, a monitoring phase, POV-demo appeals and salted-hash HWID propagation. Licensing withholds config rather than shipping a checkable branch. **V12 blocks boost; V13–V15 block the anti-cheat** |

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
| **A future feature reintroduces outbound game-server control** | It recreates both the deleted SSRF sink and a high-value third-party credential store. | [051](docs/decisions/051-phase-4-remainder.md) makes `host:port` display data and the normal contract plugin-initiated. Any RCON proposal must reopen that decision explicitly; Phase 4 stores no RCON password. |
| **Malicious organiser fabricates a result** | Match-fixing. Inherent to BYO servers; not solvable at the protocol level | Corroboration: both-team confirmation, demo evidence (CS2 only), dispute flow, challenge window before ratings settle |
| **We don't control plugin versions on BYO servers** | Old plugins send stale contracts; we cannot force upgrades | Plugin reports version; panel refuses match allocation on incompatible versions; contract carries a version field; deprecation windows |
| ~~Repo inside OneDrive~~ | ~~Git corruption, `index.lock` contention~~ | **RESOLVED 2026-08-11** — OneDrive sync disabled; the folder name is now cosmetic. The remaining backup story is the private GitHub remote, so **push regularly** |
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
| V5 | RCON is **not** part of the normal CS2 match lifecycle | [051](docs/decisions/051-phase-4-remainder.md) closes this as a product boundary: the plugin always dials us and the panel never stores or uses RCON | **Resolved by design.** A future outbound-control proposal must reopen 051 rather than quietly adding a credential |
| V6 | CS2 can expose authoritative final match state for reconciliation | [035](docs/decisions/035-match-result-integrity.md) | Inspect what a map-end payload can contain |
| V7 | Steam OpenID 2.0 remains available with no announced sunset | [011](docs/decisions/011-accounts-and-login.md) | Re-check Steamworks docs periodically |
| **V8** | CS:GO legacy's App ID is **4465480** | [057](docs/decisions/057-server-browser-and-seo.md) | **Single-sourced.** Confirm against the Steam store page or `ISteamApps` before it reaches code |
| **V9** | `IGameServersService/GetServerList` returns community servers for app 730 and for CS:GO, with usable filters and a workable rate limit | [057](docs/decisions/057-server-browser-and-seo.md) | Get a Web API key, call it, record the rate ceiling. **Now gates more than the listing:** the confidence tiers need the whole Polish catalogue polled every ~15 min, so the rate ceiling decides whether the CS2 differentiation is possible at all |
| **V10** | The CS:GO community-server population justifies reopening [001](docs/decisions/001-game-scope.md) for tournaments | [057](docs/decisions/057-server-browser-and-seo.md) | The listing measures it — ship the list, count, then commit plugin work |
| **V11** | Padded player counts are distinguishable from real ones over a multi-week series, at a false-positive rate low enough to publish | [057](docs/decisions/057-server-browser-and-seo.md) §5a | Poll for a month before labelling anything. **The plugin-verified servers are the ground truth** to calibrate against — a false accusation against a legitimate server cannot be undone |
| **V12** | Paid-placement disclosure under the Polish implementation of the Omnibus Directive (2019/2161) and the DSA — is **`Promowane`** sufficient wording, is a bordered band sufficient prominence, and must the ranking criteria themselves be published? | [058](docs/decisions/058-what-we-sell.md) §6 | **Blocks boost shipping.** The label is decided (`PROMOWANE · ZWERYFIKOWANE`); what is unconfirmed is whether it satisfies the statute. The first item here gated on law rather than engineering, and single-sourced — confirm against the current text or counsel *before* it reaches code |
| **V13** | Lawful basis, retention period and erasure-right reconciliation for a stored hardware identifier | [058](docs/decisions/058-what-we-sell.md) §6c | **Blocks HWID banning.** A hardware identifier is personal data, and [012](docs/decisions/012-security.md) currently advertises **zero persisted application IPs** — reversing that is a decision, not a side effect. **Salted hash only; the raw value is never stored** |
| **V14** | Does the server-side AC decide ~65% of cases at a false-positive rate low enough to auto-ban on? | [058](docs/decisions/058-what-we-sell.md) §6c | **Blocks auto-banning and any published rate.** An estimate made before a single match was played. Calibrate against the corpus first — **a sold anti-cheat makes a promise a free one does not** ([052](docs/decisions/052-cheating-and-rating-integrity.md)) |
| **V15** | Can the game server trigger a client-side POV demo recording on CS 1.6, CS:GO and CS2, or is it a protected client command? | [058](docs/decisions/058-what-we-sell.md) §6c | **Blocks the appeal path.** CS:GO/CS2 restricted server-issued client commands; **1.6 both needs it most and is likeliest to allow it** — it has no server-side demo pipeline ([024](docs/decisions/024-demos.md)). Verify per game on real hardware before any code depends on it |

**V3 is the highest priority**, and this paragraph used to say V1 — which its own table above
had already marked resolved on 2026-08-10. Corrected 2026-08-17.

If `setinfo` behaves differently on **non-Steam clients**, binding is unreliable in exactly the
game NEXTSKILL is differentiated by, and most active CS 1.6 servers admit those clients. It does
not break the second factor the way V1 would have — [050](docs/decisions/050-match-results-and-ratings.md)
deliberately rates `Declared` participants as well as `Bound` ones, precisely so an unreliable
bind cannot mean *no player ratings in CS 1.6*. What it costs is the provenance being honest
rather than impressive. V2b and V4 sit behind it and are answered by the same evening on a real
server.
