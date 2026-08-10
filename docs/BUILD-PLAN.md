# NEXTSKILL — Build plan

The design phase is complete (34 decision docs in `docs/decisions/`). This is the
implementation sequence, derived from [038 — build order](decisions/038-build-order.md).

**Status legend:** ☐ pending · ◐ in progress · ☑ done

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

## Phase 0 — Foundation

- ☑ **Scaffold solution** — `NextSkill.Api`, `NextSkill.Contracts`, `NextSkill.Simulator`, `NextSkill.Checks`
- ☐ **Verify the `net8.0` target actually compiles** — if it doesn't, the plugin contract-sharing plan breaks and 004 needs revisiting
- ☐ **Design system CSS** — tokens and the 18 components from [019](decisions/019-visual-identity.md) + [041](decisions/041-layout-system.md)
- ☐ **Localisation** — 4 locales, locale route segment, `IStringLocalizer` ([018](decisions/018-frontend-architecture.md))

## Phase 1 — Data foundation

- ☐ **First migration.** These are locked by earlier decisions and are painful to add later:
  - `matches.game` — game discriminator ([001](decisions/001-game-scope.md))
  - `matches.integrity` — `confirmed` / `suspect` / `disputed` ([035](decisions/035-match-result-integrity.md))
  - `organisers.trusted` — per-organiser trust flag (CONTEXT §3b)
  - organiser scoping column on every tenant-owned table ([037](decisions/037-platform-hosting.md))
  - `matches.payload jsonb` + GIN index ([008](decisions/008-database.md))
  - **partial unique index** — one live match per server ([007](decisions/007-server-hosting-model.md))
  - `inet` for IPs, `timestamptz` everywhere, never naive ([008](decisions/008-database.md))
- ☐ **Tenancy** — EF Core global query filters; resolver **fails closed** ([036](decisions/036-authorization.md))
- ☐ **Authorization** — resource-based handlers, no ad-hoc role checks ([036](decisions/036-authorization.md))

## Phase 2 — Pure engines (highest value, cheapest to test)

Built before any UI, because they are pure functions and they hide the silent bugs.

- ☐ **Bracket engine** ([022](decisions/022-tournament-formats.md), [023](decisions/023-bracket.md))
  - ☐ 8-team single elim → correct champion, 7 matches
  - ☐ 13-team → byes to top seeds, byes **not rated**
  - ☐ 8-team double elim → every loser lands in the correct LB match
  - ☐ **Bracket reset** — LB team wins grand final → a second grand final is created
  - ☐ UB team wins grand final → tournament ends, no extra match
  - ☐ Generation → no dangling `winner_to` / `loser_to` links
- ☐ **Rating engine** ([025](decisions/025-rating-system.md))
  - ☐ Bye → no rating change
  - ☐ `disputed` → no rating change until confirmed
  - ☐ Beating a much weaker opponent → near-zero gain
  - ☐ Inactive team → rating unchanged, RD grows
  - ☐ **Deterministic** — replaying the same sequence gives identical ratings

## Phase 3 — Wire contract & simulator

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

Concretely, one at a time. **No shared abstraction until both exist** ([001](decisions/001-game-scope.md)).

- ☐ **CS2 plugin** ([002](decisions/002-cs2-plugin.md)) — CounterStrikeSharp, original code
- ☐ Verify **V5** — is RCON needed at all? ([009](decisions/009-api-contract.md) suggests not)
- ☐ **Verify V2b / V3 / V4** on a real CS 1.6 server before writing the AMXX plugin
- ☐ **CS 1.6 plugin** ([005](decisions/005-cs16-plugin.md)) — Pawn/AMXX
- ☐ **Round backup/restore verified by actually killing a server mid-match** ([034](decisions/034-test-strategy.md))

## Phase 6 — Ship

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
