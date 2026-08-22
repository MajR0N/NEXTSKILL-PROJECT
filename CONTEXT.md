# NEXTSKILL — Context

The shared, general-purpose document. Anything true across the whole project lives here.
Anything specific to one decision lives in `docs/` as its own file.

**Status:** **building.** Phases 0–3 complete; **Phase 4 (web platform) past its keystone**.
**364 checks passing** against a real PostgreSQL, including in-process end-to-end runs of the
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
### 2.9 Server-triggered client POV demo recording — per game, and the answer differs

Researched for [058](docs/decisions/058-what-we-sell.md) §6c's appeal path. **The one game that needs
it most has it proven; the other two are open, and GOTV does not cover for them.**

| Game | Mechanism | State |
| --- | --- | --- |
| **CS 1.6** | AMXX `client_cmd(id, "record <name>")` | **Works, and is field-proven.** "Auto demo recorder" AMXX plugins already do exactly this — start a client-side recording when a player connects, precisely so an unban appeal has something to look at |
| **CS:GO / CS2** | SourceMod `ClientCommand` / CSSharp equivalent | **Open — reopened 2026-08-22.** GOTV is *not* an adequate substitute (see below). Whether the server can force `record` is unresolved, and the CS2 freeze status is untested for 2026 |

#### CS 1.6 — the one caveat worth knowing

Valve added a **command filter** to CS 1.6. With `cl_filterstuffcmd 1` a client refuses a range of
server-issued commands — the documented list includes `say`, `fps_max`, `setinfo`, `bind`,
`connect`, `exec`, `quit`. **The filter appears to default to off**, and `record` was **not** among
the commands named as filtered in any source found. So the realistic risk is not that this fails
generally, but that **a player who has deliberately enabled the filter can refuse the recording** —
which [058](docs/decisions/058-what-we-sell.md) §6c already handles: **a recording that does not
start is refusal, not clean.**

> ⚠ **Cross-finding for [005](docs/decisions/005-cs16-plugin.md) — `setinfo` is on that filter
> list.** V2b/V3/V4 assume `setinfo` is readable at connect. The filter restricts the **server**
> from *setting* it, and our design has the *player* set it in his own config, so this is probably
> harmless — **but it is close enough to the identity mechanism that it must be checked while V2b/V3
> are being tested**, not discovered afterwards.

#### CS:GO / CS2 — GOTV alone cannot answer the *aim* question *(the earlier entry was wrong twice — see the correction below)*

> ⚠ **Corrected twice on 2026-08-22.** The first version claimed CS2 POV recording was broken —
> sourced from a Leetify post of **8 Nov 2023** describing a brief outage, with CSTV restored **the
> next day**. The second version concluded CS2 therefore *"does not need"* client POV because GOTV
> can be spectated. **That second conclusion is also wrong, for a reason that matters more than the
> first error.**

**GOTV is a broadcast artefact, not a forensic one.**

| | GOTV / server demo | Client POV demo |
| --- | --- | --- |
| **Rate** | Server **snapshot** rate (`tv_snapshotrate`), typically below the server tick | The client's own stream, tick for tick |
| **Angles** | Interpolated and network-quantised for other players | **The player's actual view angles** |
| **Captures** | What the broadcast reconstructed | **What was on his screen** |
| **Size** | Large, and larger the more fidelity is kept — [024](docs/decisions/024-demos.md) already budgets ~156 GB/year | Held by the player; **uploaded only on appeal, so ~0 storage for us** |

**Why this is decisive for anti-cheat rather than a detail.** The signal that separates an aimbot
from a good player is **per-tick view-angle behaviour** — the snap, the microcorrection, the
impossible settle. Snapshot-rate interpolation is exactly the process that smooths those away. A
GOTV demo answers *"what happened in the round"*; it does not reliably answer *"did his crosshair
move like a human's."*

> **So the client POV demo is needed in all three games, not only as a CS 1.6 workaround** — GOTV
> at default settings does not close the per-tick gap the aim question depends on.
>
> ⚠ **Superseded in part below.** This once read *"the preferred artefact"*, which was wrong: **GOTV
> and POV are complementary, not ranked.** GOTV answers behaviour (wallhack, pre-aim); POV answers
> mechanics (aimbot). See the correction two subsections down.

**Where CS:GO and CS2 now stand:**

| Question | State |
| --- | --- |
| **Does CS2 `record` work at all?** | **Answered — yes.** Measured 2026-08-22 on a **loopback host** — repeat under load. See below |
| Can a server *force* `record` on CS:GO / CS2? | **Open.** `FCVAR_SERVER_CAN_EXECUTE` gates server-forced client commands, and the CS2 cvar dump shows `_record` **without** that flag. SourceMod `ClientCommand` is the CS:GO mechanism, on a timer, not on connect. **Needs the Phase 6 plugin to test** |
| If forcing is impossible, is *asking* enough? | A player told to record either does or does not. §6c treats a missing recording as **refusal, never clean**, so a voluntary route still yields a signal — weaker than an armed one |

**Measured 2026-08-22 — first-party, and it settles the freeze question.** Tested on a **dedicated
server** with `tools/cs2-pov-check`:

| Result | |
| --- | --- |
| `record` produced a demo | **Yes** |
| Client froze | **1–2 seconds, then recovered.** A stutter, not a hang |
| Recording stopped and saved | **Yes** |

> **The 2023 claim is overstated for August 2026.** *"`record` will freeze your game most of the
> time"* described a hang; what actually happens is a **1–2 second stall that resolves**. The
> feature works.

**But 1–2 seconds is not free**, and it changes a design detail rather than merely reassuring us —
see the two consequences below.

Still untested: `playdemo` on the resulting file (a demo a referee cannot open is worth nothing),
and whether the stall is worse mid-round than during freezetime.

#### Consequence 1 — arm recording at freezetime, never the instant the meter crosses

[058](docs/decisions/058-what-we-sell.md) §6c arms the demo **at the flag**, which can land in the
middle of a live round. A 1–2 second freeze there:

- **disadvantages the player being monitored — who is very often innocent**, since most flags are
  meant to resolve silently;
- can lose a round, which turns a routine check into a legitimate complaint;
- would make the monitoring phase itself unfair, which is the one thing it must not be.

> **So the record command is issued at the next freezetime boundary, not at the moment the meter
> crosses.** Round start is when nobody is shooting and a beat is already expected. The delay is at
> most one round, and the monitoring phase runs for longer than that anyway.



#### `tv_snapshotrate` measured — 32 / 64 / 128, 2026-08-22

**Higher rate is visibly better**: smoother flicks, less tearing. That is the right evidence for the
use case that matters — [052](docs/decisions/052-cheating-and-rating-integrity.md) rests on **human
review**, so a referee being able to *see* the difference is the workflow improving, not a proxy
for it.

**Two things it does not yet establish**, both recorded in the check sheet:

- **Whether 128 beats 64 as *data*.** A snapshot rate above the server tick cannot capture state
  the server does not have — at 64-tick, `tv_snapshotrate 128` records the same state twice. **The
  server's tickrate decides whether that step was real**, and it was not recorded.
- **The cost.** `tv_record` writes server-side, so the files are on the game server. Sizes still
  needed before the fidelity/disk trade can be priced against
  [024](docs/decisions/024-demos.md)'s measured 233 MB mean.

> **And it does not retire the client POV demo yet.** A human seeing smoother flicks is not the
> same as a detector having per-tick angles. That question needs the demos **parsed and diffed**,
> not watched — which is only worth doing once there is a detector to feed.
#### Measured demo sizes, while we were in there

Seven real CS2 GOTV demos on a local install: **150–299 MB, mean 233 MB** — against
[024](docs/decisions/024-demos.md)'s assumed 50–150 MB. **Roughly 2.5× low**, and 024 had asked for
exactly this confirmation. The client POV test file was **2.6 MB** for a short recording.

Two consequences, both recorded in [024](docs/decisions/024-demos.md):

- **Local disk is arithmetically dead.** ~37–75 GB live at 90-day retention against
  [042](docs/decisions/042-hosting.md)'s 40 GB VPS disk. 042 had already superseded 024 on this;
  024 now says so too, instead of still reading *"object storage was rejected"*.
- **The fidelity/disk trade is now quantified.** Raising `tv_snapshotrate` or enabling
  `csgo_fatdemo` buys detail and costs megabytes — and we now know roughly what a megabyte of
  match is worth.

#### Consequence 2 — the freeze is a tell, and a felt one

CONTEXT already notes the player can *see* the recording indicator. Now we know he can **feel** the
moment it starts. That sharpens the evasion loop already recorded in §6c: a cheater who notices the
stall knows precisely when he was flagged and can switch off for the duration.

Arming at freezetime partly masks it — a beat at round start is ordinary — but does not remove it.
**The countermeasure is unchanged and now matters more: a suspicion meter that collapses the instant
recording starts is itself scored.**

#### Corrected 2026-08-22: we need **both** artefacts. They answer different questions

An earlier version of this section called the POV demo *"the preferred artefact"* and treated GOTV
as a fallback. **That is wrong.** They are complementary, and the split follows the kind of cheating
being investigated:

| | **GOTV** (`tv_record`) | **Client POV** (`record`) |
| --- | --- | --- |
| **Answers** | **Overall behaviour** — wallhacking, pre-aiming an unseen enemy, prefiring, positioning that keeps being *too* right | **Mechanics** — aimbot, snapping, the impossible settle |
| **Why it suits that** | Wallhack shows up as *where someone looks, relative to enemies they cannot see, over seconds*. Snapshot rate is plenty for that | Aim shows up **per tick**. Interpolation destroys exactly that |
| **Who holds it** | **Us**, always, for every match | The player, and only if he appeals |
| **Cost** | Disk — [024](docs/decisions/024-demos.md) budgets ~156 GB/year | Nothing until an appeal |
| **Weakness** | Too coarse for aim analysis | Absent unless armed, and withholdable |

> **So the two are a screening pair, the same shape as the suspicion meter itself.** GOTV is always
> there and catches the behavioural class cheaply. POV is expensive to obtain and answers the one
> question GOTV cannot. Neither replaces the other, and a referee reaching for the wrong one gets a
> confident wrong answer.

`spec_show_xray` is what makes the GOTV half practical: it renders players through walls during
playback, so *"was he tracking someone he could not see?"* is a visual check, not an inference.

#### The command inventory that matters to us

| Command | Job |
| --- | --- |
| `tv_enable 1` | Required before any GOTV recording |
| `tv_record` / `tv_stoprecord` | Start/stop the **server-side** match demo |
| `tv_record_immediate` | Start now, ignoring `tv_delay` — **use it**, or the demo is missing its opening |
| `tv_snapshotrate` | GOTV capture rate. **A knob, not a fixed limit** |
| `record` / `stop` | Start/stop the **client-side** POV demo |
| **`csgo_fatdemo_enable`** | **Records additional data for analysis — "fat" demos.** Default `false`; flags include both `sv` and `cl`. Paired with `csgo_fatdemo_output` (default `test.fatdem`) |
| **`demo_flush`** | Forces the demo file to be written immediately |
| `demo_recordcommands` | Records console commands into the `.dem` |
| **`demo_gototick` / `demo_goto`** | **Jump straight to a tick.** The referee's most important command — see below |
| `demo_marktick` / `demo_gotomark` | Mark a moment, return to it |
| `demo_timescale` | Slow playback down — how an aim review is actually done |
| `demo_info`, `listdemo` | Inspect a demo without watching it |
| **`spec_show_xray`** | See players through walls in playback — **the wallhack check** |

#### Three consequences

**1. `csgo_fatdemo_enable` is a lead worth chasing before assuming GOTV is too coarse.** It exists
precisely to record more than a normal demo, and its flags cover both server and client. **Whether a
fat demo carries per-tick angle data is untested** — if it does, the fidelity gap may close without
depending on client cooperation at all. Test it alongside `tv_snapshotrate`.

**2. `demo_flush` sits directly on [002](docs/decisions/002-cs2-plugin.md)'s Tier 3 warning.** That
decision already flags GOTV's framebuffer flush as a thing that lag-spikes every player when
mistimed. `demo_flush` is the explicit form of it — **so the plugin has a lever here, and levers
that write to disk mid-match must be pulled deliberately, not on a timer.**

**3. Every flag must carry a tick number.** `demo_gototick` means a referee can be *at* the
evidence in one command — or can scrub a forty-minute demo hunting for a moment somebody described
as "round 12ish". The difference is entirely whether the anti-cheat recorded the tick.

> **So the calibration record ([§2.12](#212-v14--why-65-of-cases-is-the-wrong-number-and-what-to-measure-instead))
> and every referee-facing flag carry `(demo, tick)`.** It costs one integer and it is the
> difference between a referee flow that works and one nobody uses twice.

Sources:
[Total CS — demo and overwatch commands](https://totalcsgo.com/commands/categories/demo-and-overwatch),
[ArminC CS2 cvar list](https://github.com/ArmynC/ArminC-CS2-Cvars/blob/main/cvars/cvarlist.md).

#### `tv_snapshotrate` partially reopens the fidelity question

The objection recorded above — GOTV is interpolated and below tick, so it smooths away the per-tick
view-angle behaviour an aimbot signal lives in — **is true of the default, and the default is
configurable.**

> **Untested and worth testing: does raising `tv_snapshotrate` toward the server tick close enough
> of the gap for aim analysis?**

The trade is the one already named: **fidelity against file size**, and
[024](docs/decisions/024-demos.md) already budgets ~156 GB/year at ordinary settings. If a raised
snapshot rate is good enough, CS2 gains a forensic artefact **we hold ourselves** — no client
cooperation, nothing for the accused to withhold, and V15's forcing question stops mattering for
CS2. If it is not, the POV demo remains the preferred artefact and the position above is unchanged.

**This is a measurement, not an argument.** Record at two snapshot rates, compare what the angle
data looks like, and price the disk.

#### Both kinds of recording cost something, and both must be timed

[002](docs/decisions/002-cs2-plugin.md) already flags GOTV's danger as Tier 3: *stopping recording
flushes the framebuffer to disk, and doing it at the wrong moment lag-spikes every player on the
server.* We have now measured the client-side counterpart: **`record` stalls the client 1–2
seconds.**

> **The same lesson from both ends: recording is not free, and when it starts or stops is a design
> decision rather than an implementation detail.** Client recording is armed at freezetime;
> GOTV recording must be stopped only after the broadcast has finished.

**CS 1.6 is unaffected and remains proven.**

Sources:
[AMX Mod X `client_cmd`](https://www.amxmodx.org/api/amxmodx/client_cmd),
[SourceMod `ClientCommand`](https://sm.alliedmods.net/new-api/console/ClientCommand),
[`point_clientcommand` / FCVAR_SERVER_CAN_EXECUTE](https://developer.valvesoftware.com/wiki/Point_clientcommand),
[Leetify — CS2 POV demos, 8 Nov 2023](https://leetify.com/blog/cs2-pov-demos/) *(and its next-day
follow-up, "Demos are back!")*,
[Steam discussion, Sept 2023 – Apr 2024](https://steamcommunity.com/app/730/discussions/0/3881596897262163840/),
[`cs2pov` — POV from server demos](https://github.com/scharkonline/cs2pov),
[CS2 ConVar dump](https://gist.github.com/SuGolYolLom/7637a0a427c41e9668fdc0ea2fe1a78a).

_Checked 2026-08-22. The CS:GO/CS2 half was corrected twice the same day — first for a stale source, then for treating GOTV as a forensic substitute — and then **measured**, which is what should have happened first. The freeze claim survived two rounds of research and died in five minutes of testing._


---
### 2.10 Paid-placement disclosure law — V12 researched at statute level

Researched for [058](docs/decisions/058-what-we-sell.md) §6 (boost). **Not legal advice — but the
provisions are specific enough to design against, and one of them changed our label.**

#### The binding rule: Art. 7 pkt 11a u.p.n.p.r.

Poland implemented the Omnibus Directive (EU) 2019/2161 in the **ustawa o przeciwdziałaniu
nieuczciwym praktykom rynkowym**, in force **1 January 2023**. The operative provision is on the
**black list** — practices that are unfair *in all circumstances*, with no balancing test and no
harm to prove:

> **Art. 7 pkt 11a** — *"podawanie wyników wyszukiwania bez wyraźnego ujawnienia płatnej reklamy lub
> płatności dokonanej specjalnie w celu uzyskania wyższego plasowania produktów w ramach wyników
> wyszukiwania"*

**There is no size exemption.** This binds us from the first boost sold.

#### The second obligation, and it is a build item nobody had noticed

> **Art. 6 ust. 4 pkt 8** — general information on **the main parameters determining placement and
> their relative importance**, made available *"w specjalnej części interfejsu internetowego
> dostępnej w sposób bezpośredni i łatwy ze strony, na której prezentowane są wyniki wyszukiwania"*.

So the ranking criteria **must be published**, and in a specific place: **a dedicated section
reachable directly and easily from the results page.** A line buried in a privacy policy does not
satisfy it. This is a page we have to write, not just a label we have to render.

**It also suits us.** [057](docs/decisions/057-server-browser-and-seo.md) already orders the list by
live human count and the ranking by player votes; being *required* to publish that is an
advertisement for the part of the product competitors cannot copy.

#### The DSA does not reach us at this size

**DSA Art. 26** (advertising transparency) and **Art. 27** (recommender-system transparency) sit in
Chapter III **Section 3 (Arts. 24–28)**, and **Art. 19 excludes providers that are micro or small
enterprises** (Recommendation 2003/361/EC: <50 staff, ≤€10m) — everything except **Art. 24(3)**. The
carve-back applies only to designated VLOPs.

> **So the DSA's "who paid for this ad" disclosure and ad repository are not obligations for us**,
> and should not be built speculatively. **Re-check on crossing the small-enterprise threshold** —
> and note the exemption is by *enterprise size*, not by traffic.

#### The wording finding that changed our label

The statute says *"wyraźne ujawnienie"* — clear disclosure — and prescribes no specific word. But
UOKiK's published guidance on marking paid content is explicit that a label must convey the **paid**
character: it **rejects *"współpraca"*** precisely because it states only that a relationship exists.
Its accepted vocabulary is built on **`reklama`**, **`sponsorowany`**, **`płatna współpraca`**. It
also warns against foreign-language, abbreviated or ambiguous labels.

> **`Promowane` has exactly the weakness UOKiK named.** It says a row was promoted; it does not say
> anyone paid. **Changed to `SPONSOROWANE`** — Google's Polish wording, in UOKiK's accepted family,
> same length, no cost.

**Honest limits of this finding:** UOKiK's guidance addresses influencer marketing rather than search
ranking, and **Allegro uses `Promowane` at scale without apparent sanction**, so `Promowane` is
defensible market practice. We took the safer word because the rule it sits under is black-listed —
if it is wrong there is nothing to argue.

Sources:
[Omnibus Directive 2019/2161](https://eur-lex.europa.eu/eli/dir/2019/2161/oj/eng),
[u.p.n.p.r. rozdz. 2 — Art. 6, Art. 7](https://lexlege.pl/ustawa-o-przeciwdzialaniu-nieuczciwym-praktykom-rynkowym/rozdzial-2-nieuczciwe-praktyki-rynkowe/3728/),
[DSA Art. 19 — exclusion for micro and small enterprises](https://www.cms-digitallaws.com/en/dsa/article-19/),
[UOKiK ad-labelling recommendations](https://www.parp.gov.pl/component/content/article/84396:reklamowanie-towarow-i-uslug-przez-influencera-w-swietle-prawa-i-rekomendacji-uokik).

_Checked: 2026-08-22. **Statute-level research, not counsel.** Confirm before the first paid
placement goes live._

---
### 2.11 HWID, GDPR and automated bans — V13 researched

Researched for [058](docs/decisions/058-what-we-sell.md) §6c. **Not legal advice**, but specific
enough to build against. Two findings matter more than the retention question that prompted it.

#### It is personal data, and hashing does not change that

A hardware identifier tied to an account is an identifier relating to an identifiable person.
**A salted hash is pseudonymisation ([Art. 4(5)](https://gdpr-info.eu/art-4-gdpr/), Recital 26), not
anonymisation** — it stays personal data precisely *because* it can still be matched, which is the
whole point of holding it. Hashing is a security and minimisation measure. **It is not an exit from
GDPR**, and nothing downstream should be written as though it were.

#### Lawful basis: legitimate interests, and Recital 47 says so directly

**Art. 6(1)(f)**, supported by **Recital 47**: *"the processing of personal data strictly necessary
for the purposes of preventing fraud also constitutes a legitimate interest."* Cheat prevention on a
rated competitive platform sits squarely in that.

Consent is the wrong basis — a cheater simply withdraws it. Contract is arguable but weaker.

**It requires a written Legitimate Interests Assessment** (purpose / necessity / balancing).
**The necessity limb is the sharp one:** *necessary* does not mean *useful* or *more efficient*. We
must be able to say why an account-level ban alone is insufficient — the honest answer being that a
banned cheater re-registers in a minute — and why nothing less intrusive achieves it.

#### Art. 22 is the provision that bites, and our design already answers it

**Platform account suspensions are a recognised
[Art. 22](https://gdpr-info.eu/art-22-gdpr/) scenario**: a decision based *solely* on automated
processing producing legal or similarly significant effects. **The Italian Garante fined Foodinho
€5 million** for deactivating rider accounts algorithmically with no human intervention and no way
to contest.

Art. 22(3) safeguards — **human intervention, the right to express a point of view, the right to
contest**. [058](docs/decisions/058-what-we-sell.md) §6c already provides all three: the report is
shown to the player with its verdict, he may appeal, and a referee decides.

> ⚠ **The trap: *"solely automated"* includes rubber-stamp human sign-off.** A reviewer who cannot
> genuinely overrule the machine does not count. **Ours can** —
> [050](docs/decisions/050-match-results-and-ratings.md) lets a referee reverse a result and replay
> every rating from history, so the human authority is real and demonstrable.
>
> **This gives V14 a second, independent reason.** Auto-banning before the false-positive rate is
> measured is not only a product risk; without a genuine human path it is an Art. 22 problem.

#### A DPIA is very likely mandatory — and it is a deliverable, not a formality

EDPB **WP248** treats two or more of its nine high-risk criteria as triggering a DPIA. We hit at
least two plainly:

| Criterion | Us |
| --- | --- |
| **Evaluation or scoring** | The suspicion meter scores behaviour and reliability |
| **Systematic monitoring** | Observing player behaviour in the online environment |

And **Art. 35(3)(a)** makes it *mandatory* for systematic, extensive automated evaluation on which
decisions with significant effects are based — which is what a meter that ends in a ban is.

> **Write the DPIA before the anti-cheat ships, not after.** It also does useful work for us: the
> balancing test is the same argument as the LIA, and the outcome feeds the privacy notice.

#### Retention and the erasure right — the part V13 originally asked

| Question | Answer |
| --- | --- |
| **How long?** | **Art. 5(1)(e)** storage limitation. *Indefinite* is hard to defend; a defined period tied to the ban's duration plus the appeal window is. A permanent ban needs a stronger justification **and periodic review**, both recorded |
| **Does the ban survive an erasure request?** | **Yes, if reasoned.** **Art. 17(3)(e)** covers retention for the establishment, exercise or defence of legal claims, and **Art. 17(1)(c)** with **Art. 21(1)** lets us refuse where we demonstrate *compelling legitimate grounds*. **Demonstrate is the operative word** — it must be documented, not asserted |
| **What survives, exactly?** | The **minimum**: the hash and the decision. Not the account, not the contact data. This is [012](docs/decisions/012-security.md) §6's existing rule — *anonymise the player, keep the match* — applied one step further: **erase the person, keep the ban tombstone** |
| **Right to object** | Because the basis is legitimate interests, **Art. 21(1)** gives a right to object, and the compelling-grounds answer must be ready in advance rather than improvised at the first request |
| **Transparency** | **Art. 13**: the privacy notice must name HWID collection, its purpose, the basis, the retention period and the right to object. Undisclosed collection is a separate breach from unlawful collection |

#### The handling rule that falls out

**Transmit over TLS, hash server-side with a secret pepper, persist only the hash, never log the raw
value.** Client-side hashing was considered and rejected: it would need a salt the client knows,
and a modified client can fake either form equally, so it buys nothing while making rotation
harder. The raw identifier exists in memory for the length of one request and is never written.

**This is a deliberate reversal of [012](docs/decisions/012-security.md) §6's posture** — *"no
persistent application IP column, IP retention zero."* We are choosing to persist a stable
identifier for the first time, and that choice belongs in the privacy notice and the DPIA, not in a
migration nobody reviewed.

### 2.11a What writing the documents changed — **2026-08-22**

The LIA and DPIA are written: [`docs/legal/hwid-lia-dpia.md`](docs/legal/hwid-lia-dpia.md), with the
Art. 13 text in [`docs/legal/privacy-notice-hwid.md`](docs/legal/privacy-notice-hwid.md). **Drafts —
counsel review, the controller identity, the attribute list and the [PROPOSED] retention numbers are
still owed.** But the balancing test surfaced **three things the desk research did not**, and all
three are design changes rather than paragraphs:

1. **Shared hardware is a first-class risk, not an edge case.** Family PCs, siblings, LAN cafés,
   second-hand machines — ordinary in Polish grassroots CS. **So a hash must never ban on its own:
   it raises a case a referee decides**, and a person whose only connection is the machine must be
   able to rebut it cheaply. This is a change to [058](docs/decisions/058-what-we-sell.md) §6c
   step 8, which reads as though the hash *is* the ban.
2. **The scanner is the bigger privacy surface, not the hash.** A hash is one machine attribute; a
   disk scanner sees documents, file names and unrelated software. **The constraint — signature
   matches only, never a file listing, never a directory tree — belongs in the scanner's spec now**,
   while the scanner is unwritten and it costs nothing.
3. **A ~0% referee override rate is evidence *against* us.** Art. 22's *"solely automated"* includes
   rubber-stamp sign-off, so a referee who never overrules the machine is the failure mode, not the
   success case. **It must be instrumented**, and it is a third independent reason V14 gates the
   auto-ban.

_Checked: 2026-08-22. **Documents drafted, unsigned. The three findings above are actionable now.**_

Sources:
[Recital 47](https://gdpr-info.eu/recitals/no-47/),
[EDPB Guidelines 1/2024 on legitimate interest](https://www.edpb.europa.eu/system/files/2024-10/edpb_guidelines_202401_legitimateinterest_en.pdf),
[Art. 22](https://gdpr-text.com/read/article-22/),
[Garante v Foodinho](https://secureprivacy.ai/blog/gdpr-article-22-automated-decision-making-guide),
[EDPB WP248 DPIA criteria](https://www.ictlc.com/the-edpb-establishes-common-criteria-for-data-protection-impact-assessment-lists-drafted-by-national-supervisory-authorities/?lang=en).

_Checked: 2026-08-22. **Statute and guidance level, not counsel.** The LIA and DPIA are documents we
must actually write._

---
### 2.12 V14 — why "65% of cases" is the wrong number, and what to measure instead

V14 cannot be researched. It needs matches we do not have. **But it can be specified**, and doing so
found that the question as posed measures the wrong quantity.

#### The two numbers, and only one of them gates an auto-ban

| | |
| --- | --- |
| **Coverage / recall** — *"decides 65% of cases"* | Of the cheaters present, how many we catch. **Missing one costs a match.** |
| **Precision** — of everyone we ban, how many actually cheated | **Banning one innocent player costs the scene's trust in the rating**, which is the entire asset |

**Only precision gates auto-banning**, and precision is not a property of the detector alone — it
depends on how common cheating is in the pool being judged.

#### The base rate, worked, because the result is counter-intuitive

`precision = p·TPR / (p·TPR + (1−p)·FPR)`, where `p` is prevalence in the judged pool.

| Scenario | p | recall | false-positive rate | **Precision** |
| --- | --- | --- | --- | --- |
| Judging **every player**, good detector | 2% | 90% | **1%** (99% specificity) | **65%** — **more than a third of bans are innocent people** |
| Same, to reach 99% precision | 2% | 90% | **0.019%** — about **1 in 5 400** | 99% |
| Judging **only flagged players** | 50% | 90% | **1%** | **98.9%** |

**Read the third row against the first.** *The same detector*, at *the same accuracy*, goes from
ruining one player in three to being safe — purely because the pool it judges was filtered first.

> **This is what the suspicion meter actually buys.** It is not a cost saver or a politeness
> mechanism: **it raises prevalence in the judged pool, and prevalence is what makes an automated
> ban statistically defensible at all.** A detector applied to everyone cannot be safe at any
> plausible accuracy; the same detector applied to a screened pool can.

It also gives [052](docs/decisions/052-cheating-and-rating-integrity.md)'s existing rule — *no
automatic bans from statistical anomalies* — a quantitative justification rather than only an
ethical one. **Auto-ban belongs to input no human hand produces** (spinbot view-angle deltas,
impossible reaction distributions), where the false-positive rate is near zero *by construction*.
For anything merely unusual, the arithmetic above says a referee.

#### The prior art says the corpus comes first, and it is Valve

**VACnet** is the closest comparator: server-side machine learning that scores gameplay for the
probability *a human reviewer would call it cheating*. It was **trained on Overwatch human
conviction data**, and Valve reported conviction rates rising from **15–30% under human review alone
to 80–95%** with the model (GDC 2018).

Three things follow for us:

1. **Valve built the detector out of human verdicts.** That is exactly
   [052](docs/decisions/052-cheating-and-rating-integrity.md)'s *"the corpus is the moat, not the
   detector"*, confirmed by the largest example available. **Collecting verdicts is the work; the
   model is the consequence.**
2. **Even Valve routes through human labelling**, still in 2026 — the CS2 VACnet labelling portal
   has humans labelling to train the system rather than issuing bans directly. *(From headlines;
   the article itself would not fetch.)*
3. **A VACnet bypass let rage cheats evade detection for months** (reported July 2026). The best
   funded server-side detector in Counter-Strike has gaps measured in months. **Any claim we make
   about ours should be quieter than that.**

#### What V14 becomes

Three questions, and one of them is answerable today:

| | Kind | Answer |
| --- | --- | --- |
| **What precision do we require before auto-banning?** | **A decision, makeable now** | Effectively no measured false positives on the auto-ban class — because a false ban in a scene this small is unrecoverable, and **Art. 22** makes it a regulatory matter too ([§2.11](#211-hwid-gdpr-and-automated-bans--v13-researched)) |
| **What is prevalence in the flagged pool?** | Measurement | Needs matches |
| **What is the false-positive rate on the auto-ban class?** | Measurement | Needs matches **and negative labels** — see below |

#### The instrumentation problem, found while specifying this

> ⚠ **[058](docs/decisions/058-what-we-sell.md) §6c check 22 says a fallen meter "notifies nobody
> and stores nothing." As written, that makes V14 permanently unanswerable.**

A false-positive rate is computed from **flags that turned out to be nothing**. If only bans are
recorded, the denominator does not exist and precision can never be calculated — so the system could
never be shown safe enough to auto-ban, no matter how long it ran.

**Resolution:** a resolved-silently flag still writes a **pseudonymous calibration record** — meter
trajectory, which signals fired, the outcome — with **no notification to the player and no mark on
his account.** The player experience is unchanged, which was the point of the rule; what changes is
that the negative label survives. It is the same legitimate-interest basis as §2.11, and its
*necessity* argument is unusually strong: **calibration is what prevents wrongful bans.**

Sources:
[VACnet — GDC 2018 analysis](https://d3.harvard.edu/platform-rctom/submission/valve-using-machine-learning-and-deep-learning-to-catch-cheaters-on-csgo-794-words/),
[Valve Anti-Cheat, VDC](https://developer.valvesoftware.com/wiki/Valve_Anti-Cheat),
VACnet labelling portal and bypass reports, August and July 2026 *(headlines only — both sources
returned 403)*.


> **Tooling, built 2026-08-22.** The arithmetic above is not prose — it is
> `NextSkill.Domain/Integrity/DetectionCalibration.cs`, pinned by `CalibrationChecks` in the
> ordinary runner. **It runs today**: every figure quoted in this section is asserted, so if
> someone later "simplifies" the maths, the docs and the code cannot drift apart silently.
> The probes for the engine questions live in [`tools/`](tools/README.md).
_Checked: 2026-08-22. **The arithmetic is exact; the inputs are assumptions until measured.**_

---
### 2.13 V9 — `GetServerList`, and two findings that change the question

Researched 2026-08-22 for [057](docs/decisions/057-server-browser-and-seo.md) §1 and §5a. **Not yet
run against the API** — that needs a key, and [`tools/steam-serverlist-probe`](tools/steam-serverlist-probe/)
is ready for it. But the documented behaviour already moves two things.

#### The endpoint

```
https://api.steampowered.com/IGameServersService/GetServerList/v1/
    ?key=<key>&filter=<master-server-filter>&limit=<n>
```

Filters use Master Server Query Protocol syntax: `\appid\730`, `\region\3`, `\dedicated\1`,
`\empty\1`, `\map\de_dust2`, `\name_match\*foo*`, `\gametype\`, `\secure\1`, `\linux\1`,
`\collapse_addr_hash\1`, and about a dozen more.

#### Finding 1 — **there is no country filter**

Geography stops at broad `MSRegion` codes. **You cannot ask Steam for Polish servers.**

> **So "the whole Polish catalogue" is not a query — it is a query plus a GeoIP pass we run
> ourselves.** Pull a region, then filter the addresses locally.

[057](docs/decisions/057-server-browser-and-seo.md) is written as though the Polish catalogue is
directly addressable. It is not, and the consequences are real but manageable:

- We fetch **more than we need** every cycle and discard most of it.
- **GeoIP becomes a dependency** the design had not accounted for — a database to ship, refresh
  and be wrong occasionally. A server's IP country is not always its audience's country.
- A Polish server hosted in Germany, which is ordinary, is **only Polish by name and player base**
  — so GeoIP alone will misclassify some of exactly the servers we care most about.

#### Finding 2 — the constraint is **timeouts**, not the call ceiling

[057](docs/decisions/057-server-browser-and-seo.md) §5a says the rate ceiling *"decides whether the
CS2 differentiation is possible at all."* On the numbers that is probably the wrong worry:

| | |
| --- | --- |
| Polling every 15 min | **96 calls/day per query.** Even twenty partitions is ~1 900/day |
| Documented Steam Web API key ceiling | Far above that |
| **What actually bites** | *"Queries that return a large number of IPs will time out before returning everything, and **there is no way to resume the query**."* Reported practical ceiling around **20 000 servers** |

> **So the risk is not being throttled. It is one big query that never completes — and cannot be
> continued from where it stopped.**

**The answer is partitioning**, and the filter set supports it: by region, by `\map\`, by
`\empty\1` / `\full\1` / `\noplayers\1`, by `\dedicated\1`, by `\secure\1`. Each partition returns
a set small enough to finish, and the partitions are cheap because call volume was never the
problem.

This is a better position than the one 057 feared: **a design constraint with an obvious workaround,
rather than a hard ceiling on the whole idea.**

#### What the probe still has to answer

Documented behaviour is not measured behaviour — the lesson from the CS2 freeze claim, which
survived two rounds of research and died in one test.

1. **Does it return community servers for 730 and 4465480 at all?** V9's literal question.
2. **How many, and how long?** Whether Europe finishes inside a sensible timeout.
3. **Does the world query time out?** If it does, partitioning stops being optional.
4. **What fields come back per server?** The listing can only display what the API returns, and
   [057](docs/decisions/057-server-browser-and-seo.md)'s per-server pages assume a shape nobody has
   checked.
5. **Are the counts the spoofable ones?** Almost certainly yes — that is the entire premise of the
   confidence tiers — but confirm rather than assume.

Sources:
[Steam Web API GetServerList usage](https://ribasco.github.io/async-gamequery-lib/examples/webapi_steam_example.html),
[master server filter reference](https://steam.readthedocs.io/en/stable/api/steam.game_servers.html),
[practical result ceiling and timeouts](https://developer.valvesoftware.com/wiki/Talk:Master_Server_Query_Protocol).

### 2.13a V9 — **RUN 2026-08-22.** What three runs measured

**V9 is answered: yes, for both games.** `GetServerList` returns community servers for 730 and
4465480 on an ordinary Steam Web API key — **no publisher key needed** — in 2–5 seconds, with enough
fields to build 057's listing. **15-minute polling is not in question.** Everything below is the
constraint, and **most of it contradicts what §2.13 predicted from documentation.**

#### Finding 3 — the constraint is a **silent 10 000-row cap**, not a timeout

| Predicted (§2.13, desk research) | Measured |
| --- | --- |
| *"a large result set times out with no way to resume"*, practical ceiling ~20 000 | **A hard cap at 10 000 rows, returned in ~3 s with HTTP 200 and no indication it is partial** |

**Proved by contradiction, needing no documentation:** `CS2 / Europe` returned **9 997** while
`CS2 / Europe **/ dedicated**` returned **10 000**. A subset cannot exceed its superset, so both are
truncated.

> **This is worse than the timeout we were braced for, and the reason is worth keeping.** A timeout
> is loud — the call fails, you know you lost data. **A cap returns 200, fast, well-formed, and
> lies.** We would have polled for a month, published "the Polish catalogue", and never learned what
> was missing — and [057](docs/decisions/057-server-browser-and-seo.md) §5a's confidence tiers would
> have been computed on a series with a hole in it.

**Equality is not the completeness test — proximity is.** `limit=5000` returned **4 988** and
`limit=9999` returned **9 977**: the API drops ~0.2% of rows *after* applying the limit. So
`world / de_dust2` returning **9 999** is as truncated as the ones returning 10 000. The probe now
flags anything within 1% of the ceiling.

#### Finding 4 — an empty response is a **failure**, not an answer

Run 2: `limit=10000` and `limit=10001` both returned **zero rows in ~17 s**, while `limit=20000`
returned 10 000 rows in 3.5 s. It did **not** reproduce in run 3 — so it looked transient, and ~17 s
looks like Steam's internal deadline (`CS:GO / Europe` and `CS2 / world` both took 16.9 s in run 3).

> **Superseded by run 7 ([§2.13e](#213e-run-7--finding-4-is-recurring-and-the-first-honest-csgo-number)):
> it is recurring, not transient.** `CS2 / Europe` returned empty after **17.6 s on the first call of
> the run** — the largest partition, and the one the product depends on. **Two observations, both at
> ~17 s, both on the biggest query available.** Treat the deadline as a property of the endpoint.

**Steam signals this as HTTP 200 with `{"response":{}}` — the `servers` key simply absent.** The
probe was reading that as an empty list, i.e. *"there are zero CS2 servers in Europe."*

> **This matters far more for the poller than for the probe.** A Steam hiccup recorded as *"every
> server in Europe went dark"* is a poisoned data point in exactly the time series
> [057](docs/decisions/057-server-browser-and-seo.md) uses to label servers `Implausible`.
> **A missing `servers` key must be retried, never stored.**

#### Finding 5 — **the CS:GO list is fake at scale**, and this is the finding that moves the product

| Query (Europe, secure **and** occupied) | Servers | Players reported | Mean/server |
| --- | --- | --- | --- |
| **CS2** | 5 734 *(complete)* | 52 874, of which 2 022 bots | **9.2 — plausible** |
| **CS:GO legacy** | 10 000 *(capped)* | **791 780**, of which **45** bots | **79 — impossible** |

CS:GO community servers seat 32–64. **A 79-player mean is padding, and `\secure\1` did not filter
any of it out** — the fakes report VAC-secure. The `bots` column is a second tell: **45 bots across
10 000 servers**, against 2 022 across CS2's 5 734. Real community servers run bots; fake listings
do not bother setting the field.

**Three consequences:**

1. **V10 cannot be answered by counting rows**, and neither can any CS:GO listing built on this API.
   The population question needs padding-filtered data, which is the next measurement.
2. **[057](docs/decisions/057-server-browser-and-seo.md)'s decision to order the list by live humans
   was right for a reason nobody had measured.** Ordering by reported players ranks the fakes first.
3. **V11 gets cheaper — but less than first claimed, and the arithmetic says why.** A server
   reporting **more players than it has slots** is an impossibility rather than an inference, so it
   is publishable from one snapshot. **It catches ~5% of the fakes, not the fakes.** Measured:
   474 of 10 000 CS:GO listings exceed their slots, yet the mean is 78 — so the remaining ~9 500
   servers still average ~70 each. **They pad `max_players` too, and stay internally consistent.**
   A listing claiming 64/64 is a lie but not an arithmetic error. See §2.13b for what does scale.

#### What this makes the design

- **Partition, and verify each partition is under the cap.** Anything returning within 1% of 10 000
  is incomplete and must be split again.
- **`Europe / occupied` returns ~5 700 and is complete** — and empty servers are noise a
  humans-ordered list discards anyway. **The partition we actually need already fits**, so the
  partitioning problem largely dissolves for the real product need.
- **Poland is GeoIP over ~5 700 addresses**, not a query. Finding 1 stands.
- **`bots` is its own field**, so CS2 counts can separate bots for free —
  [057](docs/decisions/057-server-browser-and-seo.md) assumed only the 1.6 plugin could do that.
- **Unverified:** whether `players` includes `bots`. Both are returned; the convention is not
  documented and the arithmetic depends on it.

### 2.13b The padding, measured — and the false positive that calibrated the detector

Run 4 added the padding check. **Both halves of what it found matter, and the second one is a
warning about ourselves rather than about the fakes.**

#### The fakes are saturated, not merely inflated

The three worst CS:GO listings report **254/64, 254/64, 254/32**. **254 is the A2S player-count byte
at its ceiling** (`2^8 − 2`) — these are not servers exaggerating, they are fields slammed to the
maximum the protocol can represent. Crude, deterministic, trivially detectable.

#### But the crude check catches only ~5% of them, and the mean proves it

| | CS2 | CS:GO legacy |
| --- | --- | --- |
| Servers (secure + occupied, Europe) | 5 790 *(complete)* | 10 000 *(capped)* |
| Mean players/server | **9.2** | **78.3** |
| Beyond their own slot count | **1** | **474** |
| Exactly full | 16 (0.3%) | 475 (4.8%) |
| Bots, total | 2 082 | **34** |

**Do the arithmetic on the CS:GO column.** 474 servers at ~254 each is ~120 000 players. The total
is 783 034 — so **the other ~9 500 servers still average ~70 each** and are *not* flagged, because
they inflate `max_players` alongside `players` and stay internally consistent.

> **A listing claiming 64/64 is a lie, but it is not an arithmetic error.** So the impossibility
> check is a floor, not a solution, and last-turn's *"V11 gets much cheaper"* was an over-claim.
> **What scales instead is address concentration** — a spam farm runs thousands of listings from a
> handful of hosts, and listings-per-`/24` is still a one-snapshot measurement. That is what the
> probe now counts.

#### The false positive that set the threshold

The strict `players > max_players` test flagged **exactly one CS2 server: `[PT] Tuga Army •
Competitivo #4`, at 11/10.** That is a real Portuguese competitive server which had almost certainly
dropped a slot while eleven people were connected.

> **This is V11's entire risk, reproduced in miniature on the first run of the detector.** *"A false
> accusation against a legitimate server cannot be undone"* — and a strict inequality would have
> published one on day one, against a real server, from a rounding-shaped edge case.
>
> **One-over is noise. Four-times-over is fraud.** The threshold is now **1.5×**, which clears
> `11/10` and catches `254/64`. **The measured false positive is more valuable than the 474 true
> ones** — the true positives only confirmed what the mean already said.
>
> **Corrected 2026-08-22:** this paragraph and [057](docs/decisions/057-server-browser-and-seo.md)
> both claimed the threshold was *"pinned by an assertion"* when no assertion existed. It does now —
> `python3 tools/steam-serverlist-probe/probe.py --selftest`, no key and no network, asserts every
> judgement this probe publishes about a real server against the case that calibrated it: `11/10`
> must **not** flag, `254/64` must, `9999` counts as truncated, `100.63.x` is not relay space.
> **A claim of a check is not a check**, and this one had been written into two documents.

### 2.13c The address finding — **each game breaks the plan in a different half**

Run 5 counted addresses, and it is the most consequential result of the five.

| | CS2 | CS:GO legacy |
| --- | --- | --- |
| Distinct `/24`s | **83** for 5 862 servers | 34 for 10 000 *(capped)* |
| Densest | `100.64.194.x` = 1 260, `100.64.199.x` = 1 134, `100.64.201.x` = 1 117, `100.64.192.x` = 962 | `91.211.116.x` = 2 800, `91.219.23.x` = 671 |
| Address kind | **`100.64.0.0/10` — RFC 6598 shared space** | **Ordinary public IPs** |
| `max_players` | 64 on 5 595 of 5 862 (95%) | **255 on 2 937**, then 24, 64, 32, 42 |

#### `100.64.0.0/10` is not routable, and **cannot be geolocated**

RFC 6598 shared address space, used for carrier-grade NAT. **The same address means different
machines on different networks**, so no GeoIP database can place it — not badly, *at all*. The four
densest CS2 `/24`s alone hold ~4 500 of 5 862 servers.

> ### **This kills [§2.13](#213-v9--getserverlist-and-two-findings-that-change-the-question) Finding 1's remedy for CS2.**
> Finding 1 said there is no country filter, so *"the Polish catalogue is a region query plus our own
> GeoIP pass."* **There is nothing to run GeoIP against.** The addresses Steam returns for CS2 are
> relay-side, not server-side — almost certainly Valve fronting community servers through its own
> relay network, though **the mechanism is inferred and the conclusion does not depend on it.**
>
> **A Polish CS2 catalogue cannot be built by geolocating this API.** That is a new open item, not a
> detail — see **V16**.

#### The symmetry worth naming

**CS:GO legacy gives real addresses and a fake population. CS2 gives a real population and fake
addresses.** Neither game hands us what [057](docs/decisions/057-server-browser-and-seo.md) assumed,
and they fail in opposite directions.

#### It also voids the concentration detector — for CS2 only

CS2 shows **70.6 listings per `/24`**, which by §2.13b's proposed heuristic reads as a spam farm.
**It is an artefact of relaying.** CS:GO's 294 per `/24`, on real IPs, is the genuine signal.

> **That is the second time in two runs that a heuristic of mine would have accused the innocent** —
> first one real server at 11/10, now an entire game's worth. **Address concentration means "farm"
> only where addresses are real**, and the check must test that first. Recorded because the pattern
> matters more than either instance: **every signal this product publishes needs its precondition
> checked, not just its threshold tuned.**

#### And `max_players` saturates too, which is the better fake detector

**2 937 CS:GO servers claim `max_players` = 255** — the byte ceiling again, and impossible for a game
that seats 64. Combined with the 474 claiming more players than slots (no overlap: at 255 slots you
cannot exceed 1.5× within a byte), **two trivial one-snapshot checks account for roughly 90% of
CS:GO's 783 000 claimed players.**

So §2.13b's *"beyond-slots catches only ~5%"* stands, but its pessimism does not: **the cheap
detector was the wrong field, not the wrong idea.** Check the slots, not the players.

_Checked: 2026-08-22. **Measured, five runs, real API. V9 closed; V10 and V11 moved; V16 opened.**_

_(§2.13 above is the pre-run desk research. Its Finding 1 held; **Finding 2 was wrong** and is
superseded by Finding 3.)_

### 2.13d Run 6 — **V17 half-answered on the first try, and the region filter proved real**

The probe was extended after run 5 to report the identity fields it was already fetching and
throwing away. **The extension answered more than it was built for.**

| | CS2 | CS:GO legacy |
| --- | --- | --- |
| Servers (Europe, secure + occupied) | **6 189** *(complete)* | 10 000 *(capped)* |
| `steamid` present / distinct | **6 189 / 6 189** | **10 000 / 10 000** |
| `addr` distinct | 6 189 | 10 000 |
| Beyond 1.5× their slots | **0** | 474 |
| Claiming ≥ 255 **slots** | **0** | **2 936 (29%)** |
| Mean players/server | 9.2 | 78.6 |
| Bots, total | 2 094 | **50** |

#### V17 — **the series has a key**, and the half that is left needs a second run

**`steamid` is present on every row of both games and distinct on every row.** So is `addr`. Within
a snapshot, either would key [057](docs/decisions/057-server-browser-and-seo.md) §5a's time series.

**What one run cannot show is stability**, which is the whole question — a relay address that is
unique today and reassigned tomorrow joins two different servers into one history. The probe now
writes `{steamid: addr}` per partition to `snapshot.json` and reports the join against the previous
run: **how many identities survived, and how many of those changed address.** Those two numbers say
opposite things and the second is the informative one — **high survival with high address movement
is exactly what a relayed CS2 listing looks like, and it would prove `steamid` is the key and `addr`
is not.** One more run, a day later, closes V17.

#### The region filter is honoured — settled from run 6's own numbers, no extra call

The sample row reports **`region: -1`** while the query filtered `
egion`, which looks like the
filter being ignored — and if it were, *"Europe"* would silently mean *"world"* and the ~6 200 figure
this catalogue is being sized on would be a world figure.

**It is not, and run 6 already contains the proof.** `CS2 / Europe / de_dust2` returned **3 646 and
was complete**; `CS2 / world / de_dust2` on the same map hit the **10 000 cap**. A complete partition
cannot be smaller than an ignored filter would make it, so **the region filter discriminates.** The
per-row `region` field is junk metadata, not evidence about the query.

> Same shape of argument as Finding 3, and worth naming as a habit: **when a field lies, look for two
> queries whose counts cannot both be true.** It costs nothing and it does not depend on documentation.

#### V16 is **not** all-or-nothing — 4% of CS2 rows are geolocatable today

**267 of 6 189 CS2 rows (4.3%) sit outside `100.64.0.0/10`** and carry ordinary public addresses —
the sample row is `85.13.106.250:49930`. So a free, GeoIP-built Polish CS2 catalogue is *possible*
at **4% coverage**, which is a floor rather than a plan.

**It does not change V16's answer**, and it sharpens why: a catalogue covering 4% of the scene is
worse than no catalogue, because it looks complete and is not — the same failure mode as the silent
row cap. **The `Verified` tier remains the answer.** What the 4% buys is a seed: enough real Polish
CS2 servers to have something to show before the first plugin is installed.

#### V11 — the 1.5× margin survived a second run with **zero** false positives

**No CS2 server was flagged**, and `[PT] Tuga Army` did not reappear at 11/10 — consistent with a
transient slot drop, which is what the margin was set for. **CS2 also had 0 rows claiming ≥255
slots**, against CS:GO's 2 936.

#### V10 — CS:GO is capped in **every** partition tried, so the population is *unmeasured*

Europe, Europe + occupied + secure, and even a **single map** all returned exactly 10 000. **No
number in the CS:GO column is a measurement**; each is a floor imposed by the API.

What *is* measurable is how much of the claim is provably fake. Strip only the 474 saturated rows
and the remaining ~9 500 servers still average **69.9 players each** — still impossible for a game
that seats 32–64. **29% of the list claims 255 slots.** The claim survives no arithmetic at all.

**The next call breaks the cap:** the API's `name_match` filter takes a `*` wildcard, so the list
partitions on the hostname's first character — a property the cap does not correlate with. `--sweep`
does 36 such queries, dedupes by `steamid` (the wildcard's semantics are undocumented, so a row must
not be counted twice) and reports the padding-filtered total. **It yields a lower bound** — names
starting with punctuation or Cyrillic are outside the alphabet — **and a lower bound is already more
than the cap has ever allowed us to say.**

_Checked: 2026-08-22, run 6, real API. **V17 opened and half-closed in one run; the region-filter
premise settled; V16 given a measured floor.**_

### 2.13e Run 7 — Finding 4 is **recurring**, and the first honest CS:GO number

#### Finding 4 reproduced, and it hit the query the product depends on

`CS2 / Europe` returned **`{"response":{}}` after 17.6 s, on the first call of the run.** Run 3 had
not reproduced it, so §2.13a called it transient. **It is not.** Both observations land at ~17 s and
both on the **largest partition asked for** — which makes the shape clear: this is Steam's own
deadline on a query too big to answer, dressed as a successful empty result.

> **The failure is not random, it is size-dependent** — so the partition that is most valuable is the
> most likely to fail, and the poller must be **built around partitions that already fit** rather
> than around a retry that hopes. Retry is still required, and now sits in the probe itself: a run
> should not be lost to a hiccup the design has already declared must never be stored as a zero.

#### The padding filter fired, and **70% of CS:GO's claim did not survive it**

| Europe, secure + occupied | CS2 | CS:GO legacy |
| --- | --- | --- |
| Rows | 6 171 *(complete)* | 10 000 *(capped)* |
| Claimed players | 56 853 | 786 757 |
| **Rows dropped by two one-snapshot checks** | **0** | **3 402** |
| **Claimed players dropped** | **0** | **551 382 — 70%** |
| Padding-filtered mean | **9.2** | **35.7** |
| Bots, total | 2 174 | **34** |

**Two checks — past 1.5× slots, or claiming ≥255 slots — remove seven of every ten players CS:GO
claims.** No time series, no calibration period, one snapshot.

**And the survivors are still not credible.** A padding-filtered mean of **35.7 against CS2's 9.2**,
on servers that seat 32–64, is the internally-consistent padding §2.13b predicted: a listing at 64/64
passes both checks and is still a lie. **So 35.7 is an upper bound on an upper bound** — the honest
figure is lower again, and V10 still needs `--sweep` to escape the cap before any total means anything.

> **CS2 dropped exactly zero rows on both checks.** That contrast is the result worth carrying: **CS2's
> public numbers are clean enough to publish, CS:GO's are not.** It is the same split as §2.13c's
> addresses, and it points the same way — the CS2 catalogue is buildable from public data in every
> respect except *where the server is*, and that is [057](docs/decisions/057-server-browser-and-seo.md)'s
> `Verified` tier, i.e. **V16**.

#### Two things that held on a second look

- **The region filter is honoured**, reproduced: `CS2 / Europe / de_dust2` returned **3 766 complete**
  while `CS2 / world / de_dust2` hit the cap at 9 999.
- **The `bots` tell is stable across runs** — 34 across 10 000 CS:GO rows, 2 174 across 6 171 CS2 rows.
  Fake listings do not bother setting a field nobody looks at.

#### V17's baseline is banked

**6 171 CS2 and 10 000 CS:GO identities recorded.** The next plain run prints survival and
address-movement against them and closes V17.

_Checked: 2026-08-22, run 7, real API. **Finding 4 promoted to recurring; V10 given its first
padding-filtered figure; V17 baselined.**_

### 2.13f Runs 8 and 9 — **V17 answered**, and the cap turns out to be deterministic

#### V17: `steamid` is stable, and so is `addr` — **zero address changes in ~16 000 joins**

| | Run 8 | Run 9 |
| --- | --- | --- |
| CS2 identities surviving | 5 519 / 6 147 (89.8%) | 6 061 / 6 160 (98.4%) |
| CS:GO identities surviving | 9 965 / 10 000 (99.65%) | 9 997 / 10 000 (99.97%) |
| **Addresses changed** | **0** | **0** |

**Not one server changed address between polls, in either game** — the `100.64/10` relay assignments
included. **[057](docs/decisions/057-server-browser-and-seo.md) §5a's time series is keyable**, and
V11's month of polling can start.

**Key on `steamid` anyway.** It is the operator's GSLT identity with a documented meaning, while
`addr` is stable only as an observed fact about relay assignment — a fact Valve never promised and
can change without telling anyone.

> **The honest caveat: every gap so far reads `0.0h`.** These runs were minutes apart, so what is
> measured is *short-term* stability. A relay reassigned on a weekly cycle would look exactly like
> this. **A run tomorrow and one next week are what turn "stable" into "stable enough to publish"** —
> the snapshot keeps only the previous run, so the gap is whatever the operator leaves between them.

**The survival rates differ for a reason that is not identity churn.** The partition is `\empty\1` —
*occupied* servers — so a server that empties out drops from the list without losing its identity.
CS2's 89.8% in run 8 is a busier scene turning over, not 600 servers vanishing.

#### The 10 000-row cap returns the **same** 10 000 every time

CS:GO's list is capped, so which rows come back is the API's choice — and **9 997 of 10 000 were the
same servers as the previous run.** The cap is not a sample, it is a deterministic slice.

> **This kills a workaround before anybody built it.** *"Poll the capped partition repeatedly and
> union the results"* is the obvious way round a row limit, and it would have returned **the same
> 10 000 rows forever** while looking like it was accumulating coverage. Partitioning is not the
> cheap option, it is **the only** option.

#### The sweep failed — and the failure is the useful part

`--sweep` returned **1 185 distinct servers across 36 name partitions, with 18 partitions empty**
(`b d f j q w y z 0-9`), where the unpartitioned query is capped at 10 000. **It did not enumerate
anything**, and two very different explanations fit:

1. **The queries failed.** `sweep()` called `query()` directly and **bypassed the retry** that had
   just been added to `run()` — so 36 back-to-back calls met Steam's ~17 s deadline with nothing to
   catch them. Common starting letters like `b`, `d`, `f` and `w` returning *zero* CS:GO servers is
   not credible on its face.
2. **`name_match` does not mean what we assumed** — prefix, substring, case-sensitivity and whether a
   bare `*` is even a wildcard are all undocumented.

**Both are now instrumented rather than argued.** The retry moved into a shared `fetch()`, failed
partitions print **their elapsed time** (~17 s is the deadline; a fast empty is a real zero), and the
sweep first tests `name_match` against a server name the API itself just returned — exact, prefix and
substring — using rows the probe already has, for no extra call.

> **95% of what the sweep did return was padded**, against 34% of the main list. Whatever
> `name_match` matches, it is not a neutral slice of the population — so **a lower bound built on it
> would have been wrong in a direction that flatters CS:GO**, which is the direction V10 must not err.

#### Everything else held across four consecutive runs

CS2: **0 padded rows** and **0 claiming ≥255 slots**, mean 9.2 every run. CS:GO: 474 beyond slots,
~2 920 claiming 255 slots, ~78 mean, padding-filtered to ~34.5. **The bots tell, the `/24` densities
and the 4% geolocatable share all reproduced.** The measurements are stable; only the fakes are large.

_Checked: 2026-08-22, runs 8 and 9, real API. **V17 answered for short gaps; the cap proved
deterministic; the sweep instrumented after failing.**_

### 2.13g Run 10 — **V17 reopens**, and the empty response was never one thing

Run 10 was the first with a real gap between polls — **1.7 hours** — and it reversed two conclusions
that four consecutive runs had agreed on. **Both reversals came from the same cause: every earlier
run was minutes apart, so nothing that takes hours could show.**

#### V17 collapses: **8.5% survival at a 1.7 h gap**, against 98% at minutes

| Gap | CS2 steamids surviving |
| --- | --- |
| ~0 h (runs 8, 9) | 89.8%, **98.4%** |
| **1.7 h (run 10)** | **523 / 6 157 — 8.5%** |

**And the list GREW while that happened**: 6 157 → 7 691 occupied servers, 56 898 → 71 436 players.
So this is **not** servers leaving. Nine in ten identities disappeared from a list that got bigger.

> **[§2.13f](#213f-runs-8-and-9--v17-answered-and-the-cap-turns-out-to-be-deterministic) said V17 was
> answered and recommended keying on `steamid`. That may be exactly backwards**, and the caveat
> written into it — *"a relay reassigned on a weekly cycle would look identical"* — was right in
> substance and wrong by two orders of magnitude. **It is hours, not weeks.**

**Two readings fit, and they lead opposite ways:**

1. Those servers really did leave the listing, identity intact — then `steamid` is fine and the
   scene simply turns over hard.
2. **The same servers are still listed under new `steamid`s** — then the CS2 identity is
   **per-session**, `addr` is the stable key, and §2.13f's recommendation is inverted.

**The reverse join separates them and costs nothing**: the snapshot already holds `{steamid: addr}`,
so inverting it asks *how many of the previous run's **addresses** are still listed, and how many now
carry a different `steamid`.* **High address survival with high re-keying means the identity is
ephemeral.** The probe now prints both joins on every run. **One more poll answers it.**

> **This is the third time in this measurement series that a conclusion held right up until the first
> observation at a longer timescale.** The pattern is not bad luck: *"stable"*, *"transient"* and
> *"complete"* are all claims about a window, and a probe that runs in one window cannot see the next
> one out. **Nothing here gets published on evidence shorter than the claim.**

#### The empty response was never one thing — the clock tells them apart

The `name_match` semantics check returned empty for all three patterns, and the sweep's 19 failed
partitions came back in **2–5 seconds** — not the ~17 s deadline. **A fast empty and a slow empty are
different events**, and every previous run collapsed them into whichever one it had seen:

| Runs | Claimed | Verdict |
| --- | --- | --- |
| 1–2 | every empty is *zero servers* | wrong for the 17 s ones |
| 7–9 ([Finding 4](#213a-v9--run-2026-08-22-what-three-runs-measured)) | every empty is *a failure* | wrong for the fast ones |
| **10** | **~17 s is Steam's deadline; fast is a real zero** | **the clock is the discriminator** |

**The poller rule survives intact and gains a stopping condition:** retry once, and if the retry is
also empty, *the elapsed time says whether to keep retrying or record a zero.* The probe now reads
the verdict off the clock instead of asserting one, and its own error text no longer claims
*"query failed, not zero servers"* — which run 10 disproved.

#### `name_match` is still unexplained, and the sweep is still worthless

**19 of 36 partitions empty** — `b d f g j q w y z 0-9` — and **an exact match on a name the API had
just returned found nothing.** The probe name was `'ZACZAROWANY BURDEL'`: uppercase, with a space,
while the sweep queries lowercase `a*`. So the suspects are **case-sensitivity**, **the space**, or
`name_match` not working for this appid at all. The next run tests a **bare `*` as a control** — if a
bare wildcard returns nothing, the filter is unsupported and the partition strategy is dead — plus
first-word, upper and lower prefixes.

> **95% of what the sweep does return is padded, against 34% of the main list.** Whatever it matches,
> it is not a neutral slice, and a lower bound built on it errs in the direction that flatters CS:GO.

#### A quieter finding: **the safe partition is only safe off-peak**

`CS2 / Europe / occupied` returned **7 751 — 78% of the 10 000 cap** at peak, against ~6 200 earlier
the same day. [§2.13a](#213a-v9--run-2026-08-22-what-three-runs-measured) called this partition
*"complete, so the partitioning problem largely dissolves"*. **At +25% it is 22% from silently
truncating**, and the truncation would arrive exactly at peak hours — the data a
*"best time to play"* feature is made of.

**So the poller must partition below the cap it will hit at peak, not the one it sees at 03:00**, and
the 1%-of-ceiling alarm has to be loud rather than a status column nobody reads.

**Also: the daily cycle is real and large** — +25% servers and +26% players inside a few hours, with
the mean holding at 9.2–9.3. That cycle is precisely what
[057](docs/decisions/057-server-browser-and-seo.md) §5a sells, and it is visible on day one.

_Checked: 2026-08-22, run 10, real API. **V17 reopened at 1.7 h; Finding 4 split in two; the safe
partition dated.**_

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
| V2b | Values are readable **early enough at connect** to gate team joining | [011](docs/decisions/011-accounts-and-login.md) | Log the value in a connect hook on a real server  **Tool: [tools/cs16-probe](tools/cs16-probe/RESULTS.md)** |
| **V2c** | Does `cl_filterstuffcmd 1` interfere with `setinfo` or with `record`? | [005](docs/decisions/005-cs16-plugin.md), [058](docs/decisions/058-what-we-sell.md) §6c | **Test alongside V2b/V3, on the same server, in the same session.** The CS 1.6 command filter names `setinfo` among the commands it restricts ([§2.9](#29-server-triggered-client-pov-demo-recording--per-game-and-the-answer-differs)). It restricts the *server* from setting it and our design has the *player* set it, so it is probably harmless — but it sits directly on the identity mechanism and must not be assumed  **Tool: [tools/cs16-probe](tools/cs16-probe/RESULTS.md)** |
| V3 | `setinfo` behaves consistently on **non-Steam clients** (Reunion/dproto), not just legit Steam | [011](docs/decisions/011-accounts-and-login.md) | Test with both client types  **Tool: [tools/cs16-probe](tools/cs16-probe/RESULTS.md)** |
| V4 | Info-key length limits allow a high-entropy token | [011](docs/decisions/011-accounts-and-login.md) | Set progressively longer values, find the ceiling  **Tool: [tools/cs16-probe](tools/cs16-probe/RESULTS.md)** |
| V5 | RCON is **not** part of the normal CS2 match lifecycle | [051](docs/decisions/051-phase-4-remainder.md) closes this as a product boundary: the plugin always dials us and the panel never stores or uses RCON | **Resolved by design.** A future outbound-control proposal must reopen 051 rather than quietly adding a credential |
| V6 | CS2 can expose authoritative final match state for reconciliation | [035](docs/decisions/035-match-result-integrity.md) | Inspect what a map-end payload can contain |
| V7 | Steam OpenID 2.0 remains available with no announced sunset | [011](docs/decisions/011-accounts-and-login.md) | Re-check Steamworks docs periodically |
| **V8** | ~~Is CS:GO legacy's App ID really **4465480**?~~ **Confirmed 2026-08-22.** `appmanifest_4465480.acf` on a real Steam install reads `"name" "Counter-Strike:Global Offensive"`, and `appmanifest_730.acf` reads `"Counter-Strike 2"` | [057](docs/decisions/057-server-browser-and-seo.md) | **Closed.** First-party from Steam itself, which is what the item asked for — it was flagged single-sourced. CS 1.6 is app **10** (`"Counter-Strike"`) on the same install |
| ~~**V9**~~ | ~~Does `GetServerList` return community servers for 730 and CS:GO, at a rate that allows ~15-min polling?~~ | [057](docs/decisions/057-server-browser-and-seo.md) §1, §5a | **CLOSED — measured 2026-08-22, three runs ([§2.13a](#213a-v9--run-2026-08-22-what-three-runs-measured)).** **Yes, both games, ordinary Web API key, 2–5 s per call; polling was never the constraint.** The constraint is a **silent 10 000-row cap** that returns HTTP 200 and does not say it truncated — *not* the timeout the desk research predicted. **`Europe / occupied` returns ~5 700 and is complete**, so the partition the product needs already fits. Two rules for the poller: **anything within 1% of the ceiling is incomplete**, and **a missing `servers` key is a failure to retry, never a stored zero**. Poland is still GeoIP over the addresses |
| **V10** | The CS:GO community-server population justifies reopening [001](docs/decisions/001-game-scope.md) for tournaments | [057](docs/decisions/057-server-browser-and-seo.md) | **Harder than it looked ([§2.13a](#213a-v9--run-2026-08-22-what-three-runs-measured) Finding 5).** **Row counts cannot answer it** — CS:GO legacy's Europe list reports **791 780 players across 10 000 servers, a mean of 79 on servers that seat 32–64**, and `\secure\1` filters none of it out. **The honest count needs padding-filtered data**, which the same probe now collects. For scale, CS2's Europe figure *is* trustworthy and complete: **5 734 occupied servers, ~50 850 humans** | **Run 6: every CS:GO partition tried is capped — Europe, occupied+secure, and even a single map all returned exactly 10 000. No number in that column is a measurement.** Strip only the 474 saturated rows and the rest still average **69.9 players each**, on servers seating 32–64; **29% claim 255 slots.** **`probe.py --sweep`** partitions by hostname via `name_match` (36 calls, deduped by `steamid`) and returns a padding-filtered **lower bound** — the first honest figure this item can have. **Run 7 gave it a first figure: two one-snapshot checks strip 551 382 of CS:GO’s 786 757 claimed players (70%), leaving a mean of 35.7 — against CS2’s 9.2, and still on capped rows.** So the honest answer is lower again and V10 trends **negative**; `--sweep` is what makes it a number rather than a direction.
| **V11** | Padded player counts are distinguishable from real ones over a multi-week series, at a false-positive rate low enough to publish | [057](docs/decisions/057-server-browser-and-seo.md) §5a | **Partly cheaper, and the detector is now calibrated against a real false positive ([§2.13b](#213b-the-padding-measured--and-the-false-positive-that-calibrated-the-detector)).** One-snapshot checks exist — beyond-slots, near-zero `bots` across a list, listings-per-`/24` — but **beyond-slots catches only ~5%**: the rest pad `max_players` too and stay internally consistent. **A strict `>` flagged one real server at 11/10 on its first run**, which is precisely V11's stated risk, so the threshold is **1.5×** and pinned by an assertion. **The month of polling is still owed for subtle padding**, with plugin-verified servers as ground truth | **Run 6: zero CS2 false positives at 1.5×**, and `[PT] Tuga Army` did not reappear at 11/10 — consistent with the transient slot drop the margin was set for. The margin now has a second clean run behind it and is pinned by `probe.py --selftest`.
| **V16** | **How does a CS2 server get onto a *Polish* catalogue at all?** Steam returns `100.64.0.0/10` non-routable addresses for CS2, so **GeoIP cannot place them** ([§2.13c](#213c-the-address-finding--each-game-breaks-the-plan-in-a-different-half)) | [057](docs/decisions/057-server-browser-and-seo.md) §1 — **the Polish CS2 catalogue, which is the product's front door** | **Opened 2026-08-22 by closing V9.** Candidates, cheapest first: **(a)** the server's own plugin heartbeat reports its real address — which is [057](docs/decisions/057-server-browser-and-seo.md)'s `Verified` tier already, so **the CS2 catalogue may simply *be* the verified list**, and the free listing is 1.6-only; **(b)** owner self-declares country at registration; **(c)** infer from the server name — Polish names are the actual signal a player searches on, and no GeoIP is honest about audience anyway ([§2.13](#213-v9--getserverlist-and-two-findings-that-change-the-question) already conceded a Polish server in Germany). **(a) is both the laziest and the one that sells the plugin** | **Measured floor, run 6: 267 of 6 189 CS2 rows (4.3%) are outside the relay space and geolocatable today** — enough to seed a catalogue, not to be one. **A list covering 4% of the scene looks complete and is not**, which is the silent-cap failure again, so (a) stands.
| **V17** | **REOPENED at the first real time gap.** Can a server be re-identified between polls? `steamid` survival is **98% at minutes and 8.5% at 1.7 hours** — while the list itself *grew* 6 157 → 7 691, so this is not servers leaving ([§2.13g](#213g-run-10--v17-reopens-and-the-empty-response-was-never-one-thing)) | [057](docs/decisions/057-server-browser-and-seo.md) §5a — **every tier below `Verified`** — and **V11**, which cannot start until this is settled | **Either the CS2 identity is per-session (then `addr` is the key and [§2.13f](#213f-runs-8-and-9--v17-answered-and-the-cap-turns-out-to-be-deterministic)’s recommendation is inverted), or the scene turns over hard.** The **reverse join** separates them for no extra call — the snapshot already holds `{steamid: addr}`, so inverting it asks how many **addresses** are still listed under a **new** steamid. **The probe now prints both joins; one more poll answers it.** |
| **V12** | ~~Paid-placement disclosure wording and prominence?~~ **Researched at statute level ([§2.10](#210-paid-placement-disclosure-law--v12-researched-at-statute-level)).** Art. 7 pkt 11a (black-listed, no size exemption) + Art. 6 ust. 4 pkt 8 (publish ranking parameters, dedicated section); DSA Arts. 26–27 excluded at micro/small size. | [058](docs/decisions/058-what-we-sell.md) §6 | **Design unblocked; build to it.** Label changed `Promowane` → **`Sponsorowane`**, because UOKiK requires a label to convey *payment* — it rejects *"współpraca"* for the same gap. **Counsel confirmation before the first paid placement goes live**, and re-check the DSA exemption on crossing small-enterprise size |
| **V13** | ~~Lawful basis, retention and erasure reconciliation for a stored hardware identifier?~~ **Researched ([§2.11](#211-hwid-gdpr-and-automated-bans--v13-researched)).** Art. 6(1)(f) + Recital 47; defined retention; ban tombstone survives erasure under Art. 17(3)(e) / Art. 21(1). | [058](docs/decisions/058-what-we-sell.md) §6c | **Design unblocked; the three documents are now drafted** — [`docs/legal/hwid-lia-dpia.md`](docs/legal/hwid-lia-dpia.md) (LIA + DPIA in one accountability record) and [`docs/legal/privacy-notice-hwid.md`](docs/legal/privacy-notice-hwid.md). **Remaining: counsel review, controller identity, the attribute list, and sign-off on the [PROPOSED] retention periods.** Two things the question did not ask and should have: **Art. 22 governs automated bans** (Garante fined Foodinho €5m), and a **DPIA is mandatory** — WP248 scoring + monitoring, and Art. 35(3)(a) independently. A hashed HWID is **still personal data**. **Three design changes fell out of writing the balancing test — see §2.11a** |
| **V14** | **Restated ([§2.12](#212-v14--why-65-of-cases-is-the-wrong-number-and-what-to-measure-instead)):** required precision, prevalence in the flagged pool, false-positive rate on the auto-ban class. **Coverage — the "65%" — is the wrong quantity.** | [058](docs/decisions/058-what-we-sell.md) §6c | **Blocks auto-banning and any published rate.** At 2% prevalence and 99% specificity, **a third of bans are innocent people**; screening the pool first is what fixes it, which is what the suspicion meter is *for*. Legal reason too: **Art. 22**. **A fallen meter must still write a negative label**, or the false-positive rate has no denominator and this is unanswerable forever  **Tool: `DetectionCalibration` + `CalibrationChecks`, running today** — the arithmetic is settled, the inputs are not |
| **V15** | ~~Can the server trigger client POV recording?~~ **CS 1.6: yes, field-proven. CS2 `record` works — measured 2026-08-22** on a dedicated server: demo produced, saved, client stalled 1–2s and recovered. **Still open: can a server *force* it on CS:GO/CS2?** — needs the Phase 6 plugin | [058](docs/decisions/058-what-we-sell.md) §6c | **No longer a blocker.** The freeze claim is dead; the forcing question rides along with Phase 6, and if it fails, *asking* still yields a signal. **Design change it forced: arm recording at freezetime, not the instant the meter crosses** — a 1–2s stall mid-round would penalise a player who is usually innocent |

**V3 is the highest priority**, and this paragraph used to say V1 — which its own table above
had already marked resolved on 2026-08-10. Corrected 2026-08-17.

If `setinfo` behaves differently on **non-Steam clients**, binding is unreliable in exactly the
game NEXTSKILL is differentiated by, and most active CS 1.6 servers admit those clients. It does
not break the second factor the way V1 would have — [050](docs/decisions/050-match-results-and-ratings.md)
deliberately rates `Declared` participants as well as `Bound` ones, precisely so an unreliable
bind cannot mean *no player ratings in CS 1.6*. What it costs is the provenance being honest
rather than impressive. V2b and V4 sit behind it and are answered by the same evening on a real
server.
