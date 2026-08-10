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

## Phase 3 — Wire contract & simulator

**Lenses:** `api-designer` · `dotnet-core-expert` (minimal APIs, model binding, `HttpClient` resilience) · `secure-code-guardian` (HMAC, replay window) · `test-master`

- ☐ **OpenAPI 3.1 spec** for the plugin contract ([009](decisions/009-api-contract.md))
- ☐ **Contracts DTOs** — `required` members, unknown fields rejected ([035](decisions/035-match-result-integrity.md))
- ☐ **Simulator** ([038](decisions/038-build-order.md)) — a real HTTP client, not a stub. Must misbehave on demand:
  - ☐ dropped `seq` → match `suspect`, bracket does **not** advance
  - ☐ duplicate `seq` → idempotent no-op
  - ☐ `map_end` disagreeing with events → `disputed`, bracket blocked
  - ☐ missing/invalid HMAC → rejected
  - ☐ replayed old request → rejected on the timestamp window
  - ☐ retry after 503 → succeeds later, **no duplicate result** ([033](decisions/033-maintenance-mode.md))

## Phase 4 — Web platform, slice by slice

> ### ⚠ Phase 4 requires further grilling before it starts
>
> [021](decisions/021-site-structure.md) settled **which pages exist** and
> [041](decisions/041-layout-system.md) settled **how a page is composed**. Neither settled
> **what is actually on most pages.**
>
> Grilled so far: ranking, match detail, bracket, lobby/veto, profile, login, footer,
> admin panel.
>
> **Not yet grilled — each needs a pass before its slice is built:**
>
> | Page | Open questions |
> | --- | --- |
> | **Home `/`** | What does a logged-out visitor see first? Live matches, pinned news, top teams, a signup pitch? This is the page that converts a pasted Discord link into a user — and it has had no discussion at all |
> | **Tournament detail `/t/{id}`** | Overview, teams, bracket, rules, schedule — as tabs or one page? What does it show before registration opens vs. mid-event vs. after? |
> | **Team page `/teams/{id}`** | Roster with roles, match history, rating trend, trophies, open slots? Who can edit what? |
> | **LFT board `/lft`** | Filters, listing shape, expiry rules, how a team contacts a player, abuse controls |
> | **News `/news`** | Index and post layout, pinning, localisation fallback presentation |
> | **Seasons `/seasons/{n}`** | What does a frozen archive look like — standings only, or full browsable history? |
> | **Registration flow** | Solo player vs. team captain vs. organiser — three different first-runs |
> | **Notifications** | Is there an in-app inbox at all, or only Discord/email? Not yet decided anywhere |
> | **Search** | Is there any? Teams, players, tournaments — or none at v1? |
> | **Error pages** | 404, 500, banned, maintenance ([033](decisions/033-maintenance-mode.md)) |
>
> **Phases 0–3 do not depend on these** — foundation, engines and the wire contract are all
> settled. Build those first; grill Phase 4 pages before their slices.

**Lenses:** `grilling` (**first — the pages above are undecided**) · `dotnet-core-expert` (Razor Pages, filters, localisation) · `fullstack-guardian` · `frontend-design` · `secure-code-guardian` · `code-reviewer`

Vertical slices ([006](decisions/006-architecture.md)). Each slice is a folder containing
endpoint, request, handler and validator together.

- ☐ **Ranking** — index archetype, the workhorse page
- ☐ **Accounts** — register, login, `setinfo` token rotate ([011](decisions/011-accounts-and-login.md))
- ☐ **Teams** — create, invite, roster
- ☐ **Tournaments** — create from template, register, seed ([015](decisions/015-admin-panel.md))
- ☐ **Brackets** — render, advance (integrity-gated)
- ☐ **Matches** — detail, config endpoint, event ingest, bind, demo upload
- ☐ **Veto & lobby** — web veto over SSE ([028](decisions/028-veto-and-lobby.md))
- ☐ **Organiser panel** — servers, disputes, staff
- ☐ **Staff panel** — users, orgs, audit, maintenance mode

### Security checks that ship with the slices ([040](decisions/040-threat-model.md))

- ☐ Tenant isolation — B cannot see A's tournament; by-ID returns **404 not 403**
- ☐ No tenant context → **throws**, returns nothing
- ☐ SSRF — `127.0.0.1`, `169.254.169.254`, and a hostname resolving to a private IP all rejected
- ☐ Open redirect — `//evil.com` and absolute URLs rejected
- ☐ Sort allowlist — `?sort=id;DROP` rejected, never interpolated
- ☐ Zip bomb aborted; archive filenames never trusted
- ☐ No secret appears in any log

## Phase 5 — Plugins

**Lenses:** `csharp-developer` · `dotnet-core-expert` (the plugin is a .NET 8 assembly) · `test-master` · `debugging-wizard`
**No lens fits Pawn/AMXX** — CS 1.6 plugin work is unassisted by the available skills, which is itself a reason to sequence it last

Concretely, one at a time. **No shared abstraction until both exist** ([001](decisions/001-game-scope.md)).

- ☐ **CS2 plugin** ([002](decisions/002-cs2-plugin.md)) — CounterStrikeSharp, original code
- ☐ Verify **V5** — is RCON needed at all? ([009](decisions/009-api-contract.md) suggests not)
- ☐ **Verify V2b / V3 / V4** on a real CS 1.6 server before writing the AMXX plugin
- ☐ **CS 1.6 plugin** ([005](decisions/005-cs16-plugin.md)) — Pawn/AMXX
- ☐ **Round backup/restore verified by actually killing a server mid-match** ([034](decisions/034-test-strategy.md))

## Phase 6 — Ship

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

Per the agreed approach, **design is refined section by section as each slice is built** —
not finalised up front. [019](decisions/019-visual-identity.md) and
[041](decisions/041-layout-system.md) set the system; individual screens improve as they're
implemented.
