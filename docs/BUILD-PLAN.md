# NEXTSKILL — Build plan

The design phase is complete (34 decision docs in `docs/decisions/`). This is the
implementation sequence, derived from [038 — build order](decisions/038-build-order.md).

**Status legend:** ☐ pending · ◐ in progress · ☑ done

Each phase names the **skill lenses** to apply. They are not decoration: the lens sets the
questions asked before code is written.

---

## Environment (verified 2026-08-10)

| Tool | State |
| --- | --- |
| .NET SDK | **10.0.302** — runtime 10.0.10 only |
| Node / npm | present |
| PostgreSQL | **18.3, running on :5432** |
| Docker | **not installed** — only needed for production ([017](decisions/017-deployment-and-cicd.md)); local Postgres is used for development |
| `gh` CLI | 2.97.0, repo pushed |

### Target frameworks — do not change casually

| Project | Target | Why |
| --- | --- | --- |
| `NextSkill.Api` | `net10.0` | Latest SDK available |
| **`NextSkill.Contracts`** | **`net8.0;net10.0`** | **CounterStrikeSharp runs on .NET 8** and must reference this library. Sharing wire DTOs with the plugin is the entire reason [004](decisions/004-stack.md) chose C#. Dropping `net8.0` silently removes that benefit |
| `NextSkill.Simulator` | `net10.0` | Dev tool only |
| `NextSkill.Checks` | `net10.0` | Console assert runner — **no test framework**, per [034](decisions/034-test-strategy.md) |

---

## Phase 0 — Foundation ✅ **COMPLETE**

**Lenses:** `dotnet-core-expert` · `frontend-design` · `ui-ux-pro-max:design-system` · `csharp-developer`

- ☑ **Scaffold solution** — `NextSkill.Api`, `NextSkill.Contracts`, `NextSkill.Simulator`, `NextSkill.Checks`
- ☑ **`net8.0` target compiles** — verified; `bin/Debug/net8.0` and `net10.0` both produced. The plugin can reference the wire DTOs, so [004](decisions/004-stack.md) holds
- ☑ **Design system CSS** — tokens, 4px scale, 3 widths, 5 archetypes, 18 components
- ☑ **Layout shell** — headbar, footer, OG tags, skip link
- ☑ **Localisation** — 4 locales, locale in the route, `hreflang` alternates. **Verified running**: `/pl` `/en` `/ru` `/de` all 200 with correct `lang` and `og:locale`

## Phase 1 — Data foundation ✅ **COMPLETE**

**Lenses:** `dotnet-core-expert` (EF Core, DI, config) · `postgres-pro` · `sql-pro` · `secure-code-guardian` (tenancy is a breach surface, not a feature)

> **Verified against a real PostgreSQL 18**, not asserted:
>
> - migration applied; all 7 tables present
> - **the partial unique index was tested by trying to break it** — a second `Live` match on
>   the same server is rejected by the database, while a `Completed` one on that server is
>   correctly allowed. The race condition from [008](decisions/008-database.md) is closed
>   regardless of application code
> - `jsonb` + GIN, `inet`, `timestamptz` all confirmed by querying `information_schema`
> - tenancy proven by four checks running real queries through the filters

- ☑ **First migration written; SQL verified to contain:** These are locked by earlier decisions and are painful to add later:
  - `matches.game` — game discriminator ([001](decisions/001-game-scope.md))
  - `matches.integrity` — `confirmed` / `suspect` / `disputed` ([035](decisions/035-match-result-integrity.md))
  - `organisers.trusted` — per-organiser trust flag (CONTEXT §3b)
  - organiser scoping column on every tenant-owned table ([037](decisions/037-platform-hosting.md))
  - `matches.payload jsonb` + GIN index ([008](decisions/008-database.md))
  - **partial unique index** — one live match per server ([007](decisions/007-server-hosting-model.md))
  - `inet` for IPs, `timestamptz` everywhere, never naive ([008](decisions/008-database.md))
- ☑ **Migration applied** to the local dev database
- ☑ **Partial index proven** — second `Live` match on a server rejected; `Completed` allowed
- ☑ **Tenancy verified** ([036](decisions/036-authorization.md)) — 4 checks against real PostgreSQL:
  - ☑ B cannot see A's tournament in a list
  - ☑ B fetching A's tournament **by ID** gets nothing → **404, not 403** (a 403 confirms it exists, which is itself a leak)
  - ☑ **No tenant in scope throws** rather than returning everything. Returning an empty set would have felt safer and been worse: a forgotten middleware would fail *silently* instead of loudly
  - ☑ Accounts stay global — cross-organiser ratings are the point of a platform
- ➡ **Authorization** — resource-based handlers moved to **Phase 4**. They protect endpoints, and there are no endpoints yet; building them now would be guessing at their shape ([036](decisions/036-authorization.md))

## Phase 2 — Pure engines ✅ **COMPLETE — 25/25 checks passing**

*Built while Phase 1 was blocked: these engines are pure functions and need no database at
all. Run them with `dotnet run --project tests/NextSkill.Checks`.*

**Lenses:** `test-master` · `tdd` · `csharp-developer`

*(`dotnet-core-expert` not needed here — the engines are pure C# with no framework surface.)*

Built before any UI, because they are pure functions and they hide the silent bugs.

- ☑ **Bracket engine** ([022](decisions/022-tournament-formats.md), [023](decisions/023-bracket.md)) — **8/8 checks passing**
  - ☑ Seed order keeps 1 and 2 in opposite halves
  - ☑ 8-team single elim → correct champion, 7 matches
  - ☑ 13-team → byes to top seeds, byes **not rated** (12 played, not 15)
  - ☑ 8-team double elim → structure correct, no dangling links
  - ☑ **Bracket reset** — LB team wins grand final → a second grand final is created
  - ☑ UB team wins grand final → tournament ends, no extra match
  - ☑ 16-team double elim terminates, 30 matches (2n−2)

  > **The checks earned their keep immediately.** The first run failed with
  > `upper match 5 drops its loser nowhere`: the formula for which upper round feeds each
  > lower round was off by one, so **upper round 2's losers were routed nowhere and upper
  > round 1's were routed twice**. The bracket would have rendered perfectly and silently
  > dropped a whole round of teams out of the tournament. Exactly the failure
  > [023](decisions/023-bracket.md) predicted.
- ☑ **Rating engine — Glicko-2 for teams** ([025](decisions/025-rating-system.md)) — **9/9 checks passing**
  - ☑ **Glickman's published worked example reproduces exactly** (1464.06 / 151.52) — the check that validates the maths independently of our expectations
  - ☑ Inactive team → rating **unchanged**, RD grows (ratings never decay)
  - ☑ RD capped at 350, so an absent team never becomes less certain than an unrated one
  - ☑ Anti-farming → beating a much weaker team gains almost nothing
  - ☑ An upset loss costs far more than the win would have gained
  - ☑ RD controls swing size — an uncertain rating moves more
  - ☑ Provisional until RD settles → no "played two matches, ranked #1"
  - ☑ **Deterministic** — identical replay gives an identical rating, which is what makes recomputation after a reversed result trustworthy
  - ☑ Policy: byes, `suspect`/`disputed`, open challenge windows and forfeits are **not rated**, and the reason is surfaced rather than a bare boolean

  > **One check was wrong, not the code.** It demanded an upset loss cost "more than 20
  > points" and failed at −9.51. But RD 40 means a *settled* rating, and small movements are
  > exactly what low RD is meant to produce. Glickman's example passing is what proves the
  > maths; the check was rewritten to assert the **asymmetry** (a loss costs far more than
  > the win gains) rather than a magnitude pulled from thin air.

- ☑ **Player rating — OpenSkill (Bradley–Terry full)** ([025](decisions/025-rating-system.md)) — **8/8 checks passing**
  - ☑ Winners gain, losers lose, everyone becomes more certain
  - ☑ An upset is worth far more than an even win
  - ☑ Farming a much weaker team gains almost nothing
  - ☑ **The least certain player on a team moves the most** — the result says most about who we knew least about
  - ☑ Displayed ordinal is conservative (μ − 3σ), so a newcomer ranks below a proven equal
  - ☑ Win probability symmetric; equal teams are a coin flip
  - ☑ **Deterministic**
  - ☑ **Rates from roster + outcome ALONE** — no kills, damage or utility required, which is exactly why CS 1.6 players get real ratings despite the game exposing far less stat data than CS2

## Phase 3 — Wire contract & simulator ✅ **COMPLETE — 63/63 checks passing**

**Lenses:** `api-designer` · `dotnet-core-expert` (minimal APIs, model binding, `HttpClient` resilience) · `secure-code-guardian` (HMAC, replay window) · `test-master`

- ☑ **Contracts DTOs** ([009](decisions/009-api-contract.md), [035](decisions/035-match-result-integrity.md)) — **8/8 checks passing**, and **verified compiling for `net8.0`**, the plugin's runtime
  - ☑ Shared `NextSkillJson` options — panel and plugin serialise identically by construction
  - ☑ snake_case wire format; enums as readable strings
  - ☑ **Unknown field → rejected**, never silently ignored
  - ☑ **Missing score → rejected, NOT defaulted to zero** — the single most likely form of the silent-failure bug
  - ☑ Strictness reaches *inside* the payload, not just the envelope
  - ☑ Unknown contract version is detectable before parsing further
  - ☑ `MatchConfig` carries **decided** maps and sides and **no veto rules** — a check that fails if [028](decisions/028-veto-and-lobby.md) is ever quietly reversed
- ☑ **Request signing** ([012](decisions/012-security.md)) — **6/6 checks**: valid accepted, tampered body rejected, wrong secret rejected, replay rejected, **future-dated rejected**, malformed distinguished from invalid. Constant-time comparison; timestamp inside the signed payload
- ☑ **Integrity rules as pure functions** ([035](decisions/035-match-result-integrity.md)) — **7/7 checks**:
  - ☑ Sequence: in-order accepted, repeat is an idempotent no-op, gap detected
  - ☑ **Duplicates are expected**, because the plugin retries until acknowledged — a retry that double-counted would corrupt a score on every network hiccup
  - ☑ Reconciliation: agreement → `confirmed`; **mismatch → `disputed`**; agreement-despite-a-gap → `suspect`, because that agreement is luck rather than evidence
  - ☑ Bracket blocked unless `confirmed`
  - ☑ **A trusted organiser skips team confirmation but NOT the integrity gate** — trust is about intent, integrity is about whether we received the result correctly
  - ☑ Referee override advances anything, and is audited — without it a genuine dispute could deadlock a tournament
- ☑ **Plugin endpoints** ([009](decisions/009-api-contract.md)) — config pull, event ingest, heartbeat. HMAC filter establishes the tenant **from the verified server**, so a plugin request authenticates and scopes itself in one step
- ☑ **Simulator** ([038](decisions/038-build-order.md)) — a real HTTP client that **misbehaves on demand**, driving the real pipeline in-process. **11/11 end-to-end checks:**
  - ☑ Signed heartbeat accepted; plugin version recorded ([007](decisions/007-server-hosting-model.md))
  - ☑ Bad signature → 401 · no signature → 401
  - ☑ **Replayed request** (genuine signature, two-hour-old timestamp) → 401
  - ☑ Unsupported contract version → 400, rejected loudly rather than best-effort parsed
  - ☑ Config carries decided maps and **no veto rules** ([028](decisions/028-veto-and-lobby.md))
  - ☑ **Duplicate event → 200 `duplicate_ignored`** — it *must* be 200, or the plugin retries forever
  - ☑ Gap in `seq` → match `suspect`, but the event is still accepted (refusing it would lose the rest of the match too)
  - ☑ `map_end` agreeing with events → `confirmed`
  - ☑ **`map_end` disagreeing → `disputed`, bracket blocked** — the silent failure, made loud, end to end
  - ☑ Malformed body → 400, never silently defaulted
- ☑ **OpenAPI 3.1 spec** generated from the real endpoint signatures — `AddOpenApi()`/`MapOpenApi()`, in the box since .NET 9 and emitting 3.1.1. Verified at `/openapi/v1.json`. **Development only for now**: the document holds no secrets, but publishing a machine-readable map of the API belongs to [039](decisions/039-public-api.md)'s work, not to generating types
- ➡ **503-retry check** moves to Phase 7 with maintenance mode ([033](decisions/033-maintenance-mode.md))

## Phase 4 — Web platform, slice by slice

> ### ⚠ Phase 4 requires further grilling before it starts
>
> [021](decisions/021-site-structure.md) settled **which pages exist** and
> [041](decisions/041-layout-system.md) settled **how a page is composed**. Neither settled
> **what is actually on most pages.**
>
> Grilled: ranking, match detail, bracket, lobby/veto, profile, login, footer, admin panel —
> plus home, tournament detail, notifications, search and onboarding, which
> [043](decisions/043-page-contents.md) closed.
>
> **Not yet grilled — each needs a pass before its slice is built:**
>
> | Page | Open questions |
> | --- | --- |
> | **Team page `/teams/{id}`** | Roster and roles are **built and decided**. Still open: match history, rating trend, trophies, open slots, team logos |
> | **LFT board `/lft`** | Filters, listing shape, expiry rules, how a team contacts a player, abuse controls |
> | **News `/news`** | Index and post layout, pinning, localisation fallback presentation |
> | **Seasons `/seasons/{n}`** | What does a frozen archive look like — standings only, or full browsable history? |
> | **Error pages** | 404, 500, banned, maintenance ([033](decisions/033-maintenance-mode.md)) |

> ### ⚠ The palette is reopened — build monochrome until [044](decisions/044-visual-direction.md) closes
>
> [019](decisions/019-visual-identity.md) chose CT blue and T gold in a single pass, while
> the discussion was really about *whether colour carries meaning*. Its behavioural rules
> hold; its hues were never argued against, and the whole layout system was then built on
> tokens nobody interrogated.
>
> **Every Phase 4 slice ships in warm greys.** Colour spreads — a page built on a
> provisional palette is a page to revisit — so the cost of deciding late is one token block
> instead of every screen. It also audits 019's own rule for free: anything ambiguous
> without hue was leaning on hue.

**Phases 0–3 do not depend on any of this** — foundation, engines and the wire contract are
settled.

**Lenses:** `grilling` (**first — the pages above are undecided**) · `dotnet-core-expert` (Razor Pages, filters, localisation) · `fullstack-guardian` · `frontend-design` · `secure-code-guardian` · `code-reviewer`

Vertical slices ([006](decisions/006-architecture.md)). Each slice is a folder containing
endpoint, request, handler and validator together.

- ☑ **Ranking** — index archetype, the workhorse page. Server-rendered on live data;
  provisional teams listed separately, `±RD` and confidence bar shown openly
- ◐ **Accounts** — register, login, logout done: Argon2id, session row + signed cookie, rate limiting, identical answers for unknown address and wrong password. **`setinfo` token rotation still open** ([011](decisions/011-accounts-and-login.md))
- ◐ **Teams** — roster mechanics done, presentation not
  - ☑ Create (founder becomes captain in one transaction), invite by nickname, accept/decline, remove, leave, promote to captain
  - ☑ A pending invite grants nothing and is never counted as roster
  - ☑ The last captain cannot leave — **and can hand over**, which is what makes that rule a safeguard rather than a trap
  - ☑ Edit (rename, re-tag, city) and **disband** — soft, tag stays reserved, confirmed by typing the tag ([045](decisions/045-teams-and-rosters.md))
  - ☑ **Roster is 5 players + 3 substitutes + 2 coaches = 10 places**, capped *per slot*. Coaches are **never fieldable**; pending invites occupy a place; moving between slots respects the destination cap ([045](decisions/045-teams-and-rosters.md))
  - ☐ Team logos — a [044](decisions/044-visual-direction.md) question, and it brings image validation and moderation with it
  - ☐ **Team page needs grilling**: match history, rating trend, trophies, open slots
- ◐ **Tournaments** — lifecycle, registration and seeding done ([046](decisions/046-tournament-lifecycle.md))
  - ☑ State machine as a pure function; **`Draft → Running` refused**, `Completed`/`Cancelled` terminal
  - ☑ **Drafts are private** — invisible to the list, to strangers and by direct id
  - ☑ Entry requires enough **fieldable** members for the format (coaches excluded)
  - ☑ Seeding on close: strongest first, **provisional teams last**, deterministic, written to the database
  - ☑ `PublicReads` — the one sanctioned cross-tenant read seam ([036](decisions/036-authorization.md))
  - ☐ Templates and post-creation editing ([015](decisions/015-admin-panel.md))
  - ☐ Check-in before start, and **forfeit** as the honest exit from a running event
- ☐ **Brackets** — persist the generated bracket to `matches` (routing links included), render, advance (integrity-gated). Seeds already exist ([046](decisions/046-tournament-lifecycle.md)); the engine is tested but has never run on live data
- ☐ **Matches** — detail, config endpoint, event ingest, bind, demo upload
- ☐ **Veto & lobby** — web veto over SSE ([028](decisions/028-veto-and-lobby.md))
- ☐ **Organiser panel** — servers, disputes, staff
- ☐ **Staff panel** — users, orgs, audit, maintenance mode

### Security checks that ship with the slices ([040](decisions/040-threat-model.md))

- ☐ Tenant isolation — B cannot see A's tournament; by-ID returns **404 not 403**
- ☐ No tenant context → **throws**, returns nothing
- ☐ SSRF — `127.0.0.1`, `169.254.169.254`, and a hostname resolving to a private IP all rejected
- ☑ Open redirect — `//evil.com`, absolute URLs, `javascript:` and `/\` all rejected
- ☐ Sort allowlist — `?sort=id;DROP` rejected, never interpolated
- ☐ Zip bomb aborted; archive filenames never trusted
- ☐ No secret appears in any log

## Phase 5 — Frontend: make it look like something

> ### ⚠ Blocked on grilling — [044](decisions/044-visual-direction.md) must close first
>
> This phase cannot start from "make it prettier". It starts from a decided palette, a
> decided type stack and a named style, none of which exist yet — see
> [044](decisions/044-visual-direction.md) for the twenty questions on the table.

**Why this is its own phase and not a slice.** Phase 4 builds pages one at a time, and every
one of them ships in monochrome with whatever markup the feature needed. That is the right
way to get *behaviour* right, and the wrong way to get a *look* right: consistency is not
something you can add a page at a time, because the thing being made consistent only exists
once all the pages do. The same is true of everything else here — you cannot audit keyboard
navigation, contrast, mobile layout or page weight across a product that is half-built.

There is also work in [018](decisions/018-frontend-architecture.md) that no Phase 4 slice
owns: **the TypeScript islands.** Every page so far is pure server-rendered HTML. The lobby
needs live updates, the bracket needs panning on a phone, and the whole site has to keep
working with JavaScript disabled — that is a build step, a budget and a progressive-enhancement
rule, not a detail of any one feature.

**Lenses:** `grilling` (**first — [044](decisions/044-visual-direction.md) is open**) ·
`frontend-design` · `ui-ux-pro-max:design-system` · `ui-ux-pro-max:ui-styling` ·
`typescript-pro` (the islands) · `javascript-pro` · `dotnet-core-expert` (asset pipeline,
`MapStaticAssets`, tag helpers) · `playwright-expert` (visual and keyboard checks) ·
`monitoring-expert` (page-weight budget)

### 5a — Close the visual direction

- ☐ Grill and close [044](decisions/044-visual-direction.md): palette, type, named style, light theme yes/no
- ☐ Replace the monochrome token block in `nextskill.css` — **one edit, by construction**
- ☐ Re-audit the components that leaned on hue once it returns, veto attribution first
- ☐ Contrast **measured**, not assumed, including `--text-dim` on `--surface`
- ☐ Logo/wordmark, and the favicon that is currently a placeholder

### 5b — The three screens that can still surprise us

Named in [041](decisions/041-layout-system.md) as the ones a design system does not settle.

- ☐ **Double-elimination bracket** — wide canvas, horizontal scroll, the losers' bracket legible at 16+ teams
- ☐ **Organiser panel** — dense mode, sidebar, tables that stay readable at 40 rows
- ☐ **Empty states everywhere** — day one has no matches, and looking *early* beats looking *abandoned* ([020](decisions/020-motion-and-ux.md))

### 5c — TypeScript islands ([018](decisions/018-frontend-architecture.md))

- ☐ Build step decided and wired — **no framework**, per [018](decisions/018-frontend-architecture.md)
- ☐ **Every page still works with JS disabled** — checked, not asserted. Link unfurling is the growth mechanism and crawlers do not run JS
- ☐ Lobby live updates over SSE ([028](decisions/028-veto-and-lobby.md))
- ☐ Bracket pan/zoom on touch
- ☐ Notification bell polling ([043](decisions/043-page-contents.md))

### 5d — The cross-cutting audits

- ☐ **Mobile pass at 360px** with real Polish strings — longer than English, and the headbar is the first thing to break
- ☐ **Keyboard-only pass**: every action reachable, focus never lost, focus never invisible
- ☐ **Screen-reader pass** on the ranking table and the bracket
- ☐ **Page-weight budget** agreed and enforced — a webfont is the single biggest cost, and the audience is on Polish mobile
- ☐ **Open Graph card designed** — quite possibly the most-viewed surface in the product, and so far it is a text tag ([018](decisions/018-frontend-architecture.md))
- ☐ 404 / 500 / banned / maintenance pages ([033](decisions/033-maintenance-mode.md))

## Phase 6 — Plugins

**Lenses:** `csharp-developer` · `dotnet-core-expert` (the plugin is a .NET 8 assembly) · `test-master` · `debugging-wizard`
**No lens fits Pawn/AMXX** — CS 1.6 plugin work is unassisted by the available skills, which is itself a reason to sequence it last

Concretely, one at a time. **No shared abstraction until both exist** ([001](decisions/001-game-scope.md)).

- ☐ **CS2 plugin** ([002](decisions/002-cs2-plugin.md)) — CounterStrikeSharp, original code
- ☐ Verify **V5** — is RCON needed at all? ([009](decisions/009-api-contract.md) suggests not)
- ☐ **Verify V2b / V3 / V4** on a real CS 1.6 server before writing the AMXX plugin
- ☐ **CS 1.6 plugin** ([005](decisions/005-cs16-plugin.md)) — Pawn/AMXX
- ☐ **Round backup/restore verified by actually killing a server mid-match** ([034](decisions/034-test-strategy.md))

## Phase 7 — Ship

**Lenses:** `devops-engineer` · `dotnet-core-expert` (publish, AOT/trimming, health checks) · `monitoring-expert` · `security-reviewer` (full pass before launch) · `sre-engineer` (backups, restore drill)

- ☐ Docker Compose + reverse proxy + CDN in front ([017](decisions/017-deployment-and-cicd.md))
- ☐ **Backups with a tested restore** — an untested backup is a hope
- ☐ GitHub Actions on push to `main`
- ☐ Maintenance mode with a live-match count ([033](decisions/033-maintenance-mode.md))
- ☐ Domain registered, EU hosting region ([037](decisions/037-platform-hosting.md))

---

## Open verification items

Carried from CONTEXT §7. **Must be resolved before the code that depends on them.**

| # | Question | Blocks |
| --- | --- | --- |
| V2b | Is `setinfo` readable at connect time, early enough to gate team joining? | [005](decisions/005-cs16-plugin.md) |
| **V3** | Does `setinfo` behave identically on **non-Steam clients**? | [005](decisions/005-cs16-plugin.md) — **the one that could hurt**, since most active 1.6 servers admit non-Steam clients |
| V4 | Does the info-key length limit allow a high-entropy token? | [005](decisions/005-cs16-plugin.md) |
| V5 | Is RCON needed for the normal CS2 match lifecycle? | [007](decisions/007-server-hosting-model.md) — if not, we never store the credential |
| V7 | Steam OpenID 2.0 still available? | [011](decisions/011-accounts-and-login.md) |
| — | Do **Unbounded** and **IBM Plex** actually cover Cyrillic? | [019](decisions/019-visual-identity.md) |

## Design note

**Two passes, on purpose.**

*Section by section, during Phase 4:* each slice gets the layout and the states it needs, in
monochrome. This is where a screen earns its structure — what is on it, what happens when it
is empty, what a keyboard does.

*All at once, in [Phase 5](#phase-5--frontend-make-it-look-like-something):* palette, type,
style, motion and the cross-cutting audits. These are **consistency** problems, and
consistency cannot be added a page at a time — it needs all the pages to exist before it can
be judged.

[041](decisions/041-layout-system.md) set the layout system and it has held up.
[019](decisions/019-visual-identity.md) set the palette in a single pass and it has not been
argued with, which is why it is reopened as
[044](decisions/044-visual-direction.md) and why the product ships grey until then.
