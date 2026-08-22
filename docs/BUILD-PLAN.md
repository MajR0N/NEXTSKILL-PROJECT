# NEXTSKILL — Build plan

The design phase is complete (**52 decision records** in `docs/decisions/`, four of them
deliberately reopened). This is the implementation sequence, derived from
[038 — build order](decisions/038-build-order.md).

**Currently: Phase 4, nearly complete. 358 checks passing.** The marks below are audited
rather than asserted — see the note at the end of Phase 4 about what "◐" means here.

**Status legend:** ☐ pending · ◐ in progress · ☑ done · ⊘ blocked — nothing to test until the slice it belongs to exists · ➡ deliberately moved

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

## Phase 3 — Wire contract & simulator ✅ **COMPLETE**

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

## Phase 4 — Web platform, slice by slice ◐ **IN PROGRESS**

> ### v1 = this phase, complete (decided 2026-08-11)
>
> **Definition of done, in one testable sentence:**
>
> > A tournament created by one Polish network, played on their server by teams from at least two
> > others, produces a bracket that advanced itself from a result **the server reported**.
>
> That sentence contains the entire differentiator — *wyniki z serwera, nie ze zrzutu ekranu*.
>
> **The date is not a build estimate.** Measured velocity is roughly a phase per session; getting
> three Polish networks to agree on a Saturday evening is weeks. Set the cup date with a partner
> and work backwards — and not in August, since Starter League's season began 2026-08-08 and teams
> have just committed ([strategy](MARKETING-AND-SALES-STRATEGY.md) §2).
>
> **One consequence to hold:** ratings ship before there is volume to settle them, so the first
> cup's teams appear in the **provisional** section with no rank ([025](decisions/025-rating-system.md)
> already separates them). Do not promise "get ranked" until a season exists — promise the record.

> ### ✅ Phase 4 grilling is complete (updated 2026-08-12)
>
> [021](decisions/021-site-structure.md) settled **which pages exist** and
> [041](decisions/041-layout-system.md) settled **how a page is composed**. Neither settled
> **what is actually on most pages.** That gap is now closed.
>
> | Page | Closed by |
> | --- | --- |
> | Ranking, match detail, bracket, lobby/veto, profile, login, footer, admin panel | earlier grilling |
> | Home, tournament detail, notifications, search, onboarding | [043](decisions/043-page-contents.md) |
> | **Team page `/teams/{id}`** — match history, rating trend, trophies, open slots | [049](decisions/049-team-page.md) |
> | **How a result becomes a rating** — participants, lineup, ledger, dispute, confirmation | **[050](decisions/050-match-results-and-ratings.md)** |
> | **LFT board `/lft` and `/me/recruitment`** | **[056](decisions/056-lft-board.md)** |
> | **News `/news`**, **Seasons `/seasons/{n}`**, search, audit log | **[051](decisions/051-phase-4-remainder.md)** |
>
> **Still open and deliberately so:** team logos, blocked on
> [044](decisions/044-visual-direction.md) — [049](decisions/049-team-page.md) refuses to let one
> page slice quietly decide the visual direction. Error pages (404/500/banned) are layout rather
> than a decision; maintenance mode is [033](decisions/033-maintenance-mode.md) in Phase 7.
>
> ### ⚠ Three contradictions the grilling found between *decided* documents
>
> Recorded because "decided" did not mean "consistent":
>
> 1. **Bracket advancement vs. the challenge window** — [035](decisions/035-match-result-integrity.md)
>    advances on `confirmed`; [007](decisions/007-server-hosting-model.md) says a result is final
>    only after the window closes. Resolved in [050](decisions/050-match-results-and-ratings.md):
>    human judgement holds the *reversible* thing (the rating) and never the irreversible one
>    (the bracket).
> 2. **Frozen seasons vs. derived ratings** — [021](decisions/021-site-structure.md):87 freezes
>    standings forever; [025](decisions/025-rating-system.md):30 derives them at read time.
>    Resolved in [051](decisions/051-phase-4-remainder.md) §4 by filtering the append-only ledger
>    on `AppliedAt <= Season.ClosedAt`.
> 3. **Challenge-response vs. outbound-only** — [007](decisions/007-server-hosting-model.md) proves
>    a server by dialling it; [009](decisions/009-api-contract.md) forbids ever talking to it.
>    Resolved in [051](decisions/051-phase-4-remainder.md) §2: the plugin dials us, which
>    **deletes** [040](decisions/040-threat-model.md)'s top risk instead of defending against it.

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

- ◐ **Ranking** — team and player rankings work end to end
  - ☑ Server-rendered on live data; provisional teams listed separately; `±RD` and confidence bar shown openly
  - ☑ **Current season comes from live data** — a future or closed season is ignored, and no active season means the page invents no label ([021](decisions/021-site-structure.md))
  - ☑ **Player ranking** — OpenSkill's conservative `μ − 3σ` score is derived at read time; provisional players are visible but receive no rank ([025](decisions/025-rating-system.md))
  - ☑ Movement arrows (▲▼) and form strips (W/L) — two queries per page, never one per row. The
    arrow is **last rating movement, not rank movement**, and the label says so: real rank movement
    needs the rank as it stood on a past date, which nothing stores. An honest cheaper fact beats an
    expensive one that is quietly approximate. Never the glyph alone — the direction survives without
    shape or colour ([019](decisions/019-visual-identity.md))
  - ☑ **Protected cursor pagination and literal search** — 20 rows per page across ranked/provisional sections; cursor state is tamper-evident, filters remain shareable, and `%`/`_` are text rather than wildcard operators ([039](decisions/039-public-api.md))
- ◐ **Accounts** — authentication, profile and destructive account mutations work; export and production messaging remain
  - ☑ Argon2id, session row + signed cookie, revocation, rate limiting, identical answers for unknown address and wrong password
  - ☑ **Password reset** — single-use link, **5-minute expiry**, token stored hashed, mailbomb throttle, and **every session dies on redemption** ([047](decisions/047-two-factor.md))
  - ☑ **Two-factor** — TOTP checked against the RFC 6238 vectors (Authy, Google Authenticator, 1Password, Aegis), SMS behind a provider seam and named as weaker, single-use recovery codes ([047](decisions/047-two-factor.md))
  - ☑ Outbound messaging seam that **fails closed** — production refuses to start without a real sender
  - ⊘ A real email/SMS provider, and deliverability (SPF/DKIM/DMARC) — blocked on the production provider, sending domain and credentials ([043](decisions/043-page-contents.md))
  - ☑ **Profile pages** ([029](decisions/029-profile-login-footer.md)) — public `/players/{id}` and private `/me`; ratings remain split by game, provisional state is explicit, team history and live LFT state are shown, and the public query selects an allow-list that cannot expose email, IP, token hashes or provider IDs
    - ☑ Recent player matches — built on `match_participants`, the immutable history 029 was waiting for. Each row states **how** it is known: *potwierdzony w grze* for a bind the server proved, *zgłoszony przez kapitana* for a declaration. Nothing is inferred from today's roster. Achievements still wait on placement standings
  - ☑ **`setinfo` token rotation** — 256-bit console-safe token, only SHA-256 stored; the copy-ready `setinfo _ns "…"` command is shown once under `no-store`, every rotation immediately invalidates the previous token, and the POST is CSRF-protected ([011](decisions/011-accounts-and-login.md))
  - ☑ **Steam / Discord linking** — Steam OpenID assertions are verified server-to-server; Discord uses authorization code + the minimum `identify` scope and never stores provider tokens. Correlation state is protected, tied to the account and live session, and backed by an HttpOnly one-use cookie; link/unlink starts are CSRF-protected; unique database indexes ensure one provider identity belongs to only one NEXTSKILL account even under concurrent callbacks. Discord activates when its application ID/secret are supplied ([011](decisions/011-accounts-and-login.md))
  - ☑ **Log out everywhere** — the CSRF-protected `/me` action deletes every server-side session for the account in one database statement, clears the initiating cookie and confirms completion; two-browser coverage proves both sessions lose private-page access immediately
  - ☑ **Account deletion and IP minimisation** — password + exact-nickname confirmation and CSRF protection; refuses to orphan the last team captain, organiser owner or platform owner; destroys contact data, credentials, provider links, transient community data and every session in one transaction; accepted roster/rating history survives behind an anonymous tombstone and disappears from live rankings. PostgreSQL stores no IP column; the in-memory auth limiter partitions on a per-process HMAC that becomes unlinkable on restart ([011](decisions/011-accounts-and-login.md), [012](decisions/012-security.md))
- ◐ **Teams** — roster mechanics done, presentation not
  - ☑ Create (founder becomes captain in one transaction), invite by nickname, accept/decline, remove, leave, promote to captain
  - ☑ A pending invite grants nothing and is never counted as roster
  - ☑ The last captain cannot leave — **and can hand over**, which is what makes that rule a safeguard rather than a trap
  - ☑ Edit (rename, re-tag, city) and **disband** — soft, tag stays reserved, confirmed by typing the tag ([045](decisions/045-teams-and-rosters.md))
  - ☑ **Roster is 5 players + 3 substitutes + 2 coaches = 10 places**, capped *per slot*. Coaches are **never fieldable**; pending invites occupy a place; moving between slots respects the destination cap ([045](decisions/045-teams-and-rosters.md))
  - ⊘ Team logos — blocked on [044](decisions/044-visual-direction.md); uploads also require image validation, storage and moderation decisions
  - ◐ **Team page expansion** ([049](decisions/049-team-page.md)) — **history and trend built**
    - One shareable per-game view, with the game in the URL so a pasted link shows what the sender saw
    - Integrity-aware record: only `Confirmed` results count, and the page **states how many it is holding back**. A record that quietly omits matches is one nobody can reconcile against their own memory
    - The trend **is a table**, reading `rating_changes` directly — 049 asked for an accessible trend, so a chart over it is a 5c enhancement and a chart instead of it would be a number only sighted mouse users can read
    - ⊘ **Trophies** still blocked: a top-three placement needs final standings, and nothing computes placement beyond the bracket winner. Openings/applications still wait on the LFT recruitment model
  - ☑ **Un-disbanding** — only a captain active at the exact disband moment may restore; exact-tag confirmation keeps the same team id and tag, leaves every old tenure closed, and starts a new roster with only the restoring captain. A transaction and row lock make retries and competing restores safe ([045](decisions/045-teams-and-rosters.md))
- ◐ **Tournaments** — lifecycle, registration and seeding done ([046](decisions/046-tournament-lifecycle.md))
  - ☑ State machine as a pure function; **`Draft → Running` refused**, `Completed`/`Cancelled` terminal
  - ☑ **Drafts are private** — invisible to the list, to strangers and by direct id
  - ☑ Entry requires enough **fieldable** members for the format (coaches excluded)
  - ☑ Seeding on close: strongest first, **provisional teams last**, deterministic, written to the database
  - ☑ `PublicReads` — the one sanctioned cross-tenant read seam ([036](decisions/036-authorization.md))
  - ☑ **Templates and post-creation editing** — organiser-owned, case-insensitively named snapshots clone every currently executable setup field into an independent private draft; tournament identity and absolute date are deliberately not copied. Drafts may edit structure; opening registration freezes game/format/team size; all setup freezes on start. Capacity cannot shrink below the active field, and entry/withdraw/edit/status mutations serialize on the tournament row ([015](decisions/015-admin-panel.md), [046](decisions/046-tournament-lifecycle.md))
  - ☑ **Check-in before start, and forfeit as an event exit** — every captain confirms after registration closes; start revalidates every roster under the tournament lock and creates no partial bracket on refusal. A captain may forfeit only their own team; organiser staff may record either side with exact-tag confirmation and a reason. The audited, scoreless result advances the opponent, never changes rating, and propagates a resolved vacancy so the exiting team cannot reappear in a double-elimination lower bracket. Late plugin events serialize on the match and cannot overwrite it
  - ☑ **Season assignment** — create/edit offers every non-closed platform season plus an explicit independent-event option; active season is the default on a fresh form, and templates deliberately copy neither season nor date. Submitted ids are checked against live rows, scheduled dates must fall inside the season window, the database enforces the relationship with a foreign key, and public tournament surfaces show the assignment. Closed-season membership is frozen and a tournament assigned to one cannot start ([021](decisions/021-site-structure.md), [046](decisions/046-tournament-lifecycle.md))
  - ☑ **Discord announcements on registration open / match start / confirmed results** — each event writes a deduplicated fact to a transactional PostgreSQL outbox, then a background worker sends a rich embed with bounded backoff. The per-tournament webhook is accepted only at the exact Discord HTTPS boundary, encrypted through Data Protection, never returned or logged, and destroyed on removal; redirects and private/reserved DNS destinations are refused. Delivery state and abandoned jobs are visible on the tournament editor. Plugin retries cannot duplicate start/results, disputed results stay silent, and scoreless confirmed forfeits use the same seam ([014](decisions/014-discord.md))
- ☑ **Brackets** — the engine runs on live data, the reset arms itself, and a phone gets both mobile views ([023](decisions/023-bracket.md))
  - ☑ Schema: bracket position + routing as **real foreign keys**, so a dangling link is impossible
  - ☑ `winner_team_id` stored rather than derived from the score
  - ☑ Generation on start, in one transaction — a tournament cannot be `Running` without a bracket
  - ☑ Byes resolved at generation, never rated, advanced into the correct slot
  - ☑ Advancement in **one** place, integrity-gated ([035](decisions/035-match-result-integrity.md))
  - ☑ `/t/{id}/bracket` — wide canvas, its own horizontal scroll ([041](decisions/041-layout-system.md))
  - ☑ Advancing on a **reported result** — `map_end` now stores the winner and calls
    `AdvanceAsync`, gated on integrity, on not-already-decided, and on a real winner
  - ☑ **Bracket reset, live** ([022](decisions/022-tournament-formats.md), [023](decisions/023-bracket.md)) —
    the row has existed since generation *specifically so the rule could not be forgotten*, and
    nothing ever armed it. The engine has triggered it correctly since Phase 2, so a real double
    elimination would have ended with an undefeated team eliminated on its first defeat: the exact
    failure 022 named the most-forgotten rule in tournament software, one layer down from where it
    was checked
  - **Only from a played result.** A forfeited or vacated grand final reaches its winner by other
    paths on purpose — a team that exited the event must not be summoned back into a second final
    it cannot play. Three checks: armed when the lower-bracket team wins, *not* invented when the
    upper-bracket team wins, and *not* armed by a forfeit
  - ☑ **Mobile round-by-round** ([023](decisions/023-bracket.md)) — the same columns render; all but
    the chosen one are hidden under 720px, and the arrows are **ordinary links** carrying `?round=`.
    No second markup, no canvas, no JavaScript — so the view survives a reload and travels in a URL
    pasted into Discord. It defaults to the round in play, because that is the question a phone
    arrives with, and a hand-edited round number clamps rather than throwing
  - ☑ **Follow-team** ([023](decisions/023-bracket.md)) — the view 023 calls the more important of
    the two, because a link shared into Discord arrives with a question the tree cannot answer: did
    we advance, and when do we play next. One team's route across both brackets as a **sequence**,
    read in the order it was played, with `?team=` in the URL so the answer is shareable in turn
  - A resolved empty opponent reads *wolny los* and an unresolved one *czeka na rywala* — collapsing
    both to a dash would hide a free win. Outcomes are words, never a glyph alone
    ([019](decisions/019-visual-identity.md))
> ### The remaining slices are ordered, and the order is load-bearing
>
> [050](decisions/050-match-results-and-ratings.md) and [051](decisions/051-phase-4-remainder.md)
> changed the sequence. **Servers → veto → matches** is a chain, not a preference: a match cannot
> be configured without decided maps, and cannot be played without a verified server.

- ☑ **Localisation habit** ([051](decisions/051-phase-4-remainder.md) §3)
  - `UiText` is the small shared interface; its neutral `.resx` contains Polish and the shared
    shell now reads it through `IStringLocalizer<UiText>`
  - **The neutral resource holds Polish** — so there is no `.pl.resx`, no fallback logic and no
    missing-key path to write. `/de/teams` serves Polish exactly as today, by design rather than by
    accident
  - Every slice below uses `IStringLocalizer` from its first line; existing pages retrofit as touched.
    Three checks cover direct neutral fallback, the rendered-shell seam and a real German route
- ☑ **Server registration** ([051](decisions/051-phase-4-remainder.md) §2, closes half of [048](decisions/048-organiser-server-configuration.md))
  - `/org/servers` lists authorised server health and lets an organiser owner/admin register a
    normalised `host:port`; tenant-id tampering is refused and the database prevents duplicate
    credentials for the same endpoint
  - **We never dial a game server.** Organiser registers `host:port`, gets a one-time secret,
    pastes it into the plugin config; the secret is 256-bit, shown on one `no-store` response and
    omitted from every later health projection; the **first correctly signed heartbeat sets
    `Verified`**, while rejected traffic changes nothing
  - ⊘ Real-organiser verification remains blocked until the real plugin ships in Phase 6; the
    simulator proves the complete registration → signed-heartbeat transition now
  - ☑ `GameServer.HmacSecret` is **encrypted at rest** with a key held outside the database —
    the `ponytail:` ceiling closed 2026-08-17, and proven by signing a real heartbeat with the
    stored credential ([012](decisions/012-security.md))
- ☑ **Veto & lobby** — *before* matches, not after ([028](decisions/028-veto-and-lobby.md), [051](decisions/051-phase-4-remainder.md) §1)
  - **Server-rendered with form posts.** [028](decisions/028-veto-and-lobby.md)'s own check #5 already
    demands full correct state from a plain fetch, so this version must exist regardless; SSE in 5c
    becomes a pure view layer
  - Captains alone confirm presence and take the frozen preset's turns; row locks, append-only
    actions and server-side legality reject out-of-turn and reused-map posts
  - **Turn timer resolves lazily on read**, derived from `TurnStartedAt` — no thread, survives a
    restart, and one read makes at most one random legal move. `ponytail:` ceiling recorded
  - `<meta http-equiv="refresh" content="5">` on the veto page only, deleted when SSE lands
  - BO1/BO3/BO5 rules preserve pick order, let the non-picker choose sides and append the decider;
    completion freezes ordered `MatchMap` rows plus `Match.Map` in the same transaction
  - The plugin config endpoint now refuses an incomplete veto and emits only those frozen maps and
    sides — no dust2/CT fallback and no veto rules on the wire
  - Tournament generation currently freezes the starter seven-map pool and BO1 onto a new veto;
    the already-supported BO3/BO5 choice becomes a per-stage organiser input with the Matches/admin seam
- ☑ **Matches** — the keystone ([050](decisions/050-match-results-and-ratings.md)), **built and
  proven end to end 2026-08-17. 11 new checks.** *The definition-of-done sentence at the top of
  this phase is now true in code: a bracket advances itself from a result the server reported.*
  - ☑ `map_end` sets `WinnerTeamId` and calls `BracketBuilder.AdvanceAsync` — gated three ways
    (integrity, not-already-decided, a real winner), so a BO3's second map cannot re-advance and
    equal scores advance nobody rather than inventing someone. `ponytail:` ceiling recorded — the
    winner comes from *this* map, and series scoring lands with the per-stage `BestOf` input
  - ☑ **`MatchParticipant` + `Provenance`** — `Declared` at `match_loaded`, promoted to `Bound` by
    a `player_bound` event that **carries both factors**. One row per `(match, account)` by unique
    index, so a bind promotes and never inserts. Both provenances count toward a player rating
  - ☑ **Lineup defaults automatically** — starters before substitutes, then seniority, capped at
    the format size; coaches excluded through `IsFieldable` alone. A retried `match_loaded` mints
    no second lineup. **Layer 2 (the check-in picker) deliberately deferred** — it is strictly
    additive, feeds this same writer, and costs nothing extra later
  - ☑ **Rating ledger** — `rating_changes`, append-only, unique on `(MatchId, SubjectId, Game)`.
    Unrated rows carry `ExcludedBecause` verbatim and *no* movement, so "no change" and "changed
    by zero" stay distinguishable. The index is **proven by a direct duplicate insert**, not
    assumed. `AppliedAt` ships from day one, so [051](decisions/051-phase-4-remainder.md) §4's
    season freeze needs no migration
  - ☑ **Rating sweeper** — scoped `RatingSweeper.SweepNextAsync` plus a thin `BackgroundService`,
    same shape as `DiscordAnnouncementWorker`. Split so the checks drive it a step at a time
    instead of racing a timer. Ordered `(EndedAt, Id)`, over a partial index of matches still owed
    a rating. **`db.Ratings` now has a writer** — the ranking pages were live data over a table
    that had never been written to
  - ☑ **Confirmation accelerates, dispute holds** — captain-only, CSRF-protected, refused once the
    sweeper has settled the match. A dispute pauses the sweep, notifies organiser staff and
    **leaves the bracket alone**. `organiser.trusted` skips the wait but **never the integrity
    gate** — trust is about intent, integrity is about receipt
  - ☑ **Challenge window = 2 hours** ([007](decisions/007-server-hosting-model.md) left this open;
    decided 2026-08-17). It is the only branch that ever waits: agreement closes it instantly and
    a trusted organiser skips it
  - ☑ **Bind / per-match link code** — the code lives on the participant row rather than in a table
    of its own, so single-use falls out of `Provenance` and there is no consumed flag to forget.
    The simulator proves `Declared → Bound` **and** proves each factor alone is refused
  - ☑ A dispute was **parked** when this shipped, and the organiser panel below closed that loop
    the same day. An expiry timer was rejected outright — it would silently overrule a human on a
    schedule nobody decided
  - ➡ **Demo upload moves to Phase 6** with the plugin that produces one. UI treats demos as
    nullable from the start ([024](decisions/024-demos.md))
- ☑ **Audit log** ([051](decisions/051-phase-4-remainder.md) §7) — **built 2026-08-17**
  - `audit_entries`: actor · organiser · action · resource · reason · **outcome** · retention ·
    occurred_at. `Outcome` is the column that forces the table to exist — a **denied** attempt
    changes no row, so per-row attribution has nothing to attach itself to
  - **Append-only is enforced by PostgreSQL**, not by convention: a trigger raises on `UPDATE`.
    `DELETE` is deliberately left open, because `retention_class` exists so security rows can be
    swept later — editing history and expiring it are different acts
  - `Audit.Record` **adds but never saves**, so the row commits in the same transaction as the
    decision it describes. A forfeit cannot succeed without its record, nor a record survive a
    rollback
  - Wired to the two privileged decisions that already existed: **forfeit** (which the plan has
    called "audited" since [046](decisions/046-tournament-lifecycle.md) while meaning three columns
    on the match) and **server registration**, allowed *and* refused
  - Checks fold into the existing forfeit and server suites rather than duplicating their setup:
    the row carries actor/tenant/outcome/reason, a refused cross-tenant attempt is recorded with
    no resource, **an `UPDATE` is refused by the database**, and **no secret reaches the log**
  - ⊘ Authentication events deferred with the staff panel that would read them — an append-only log
    nobody can view is storage rather than security, and a row per login accrues GDPR weight
    against the IP minimisation that already shipped. `Retention` ships now so they need no migration
- ☑ **Home `/`** — the design-system scaffold is gone. Live matches, pinned announcements,
  upcoming events, recent results and the CS2 top five, each server-rendered with its own empty
  state, and a genuinely quiet day says *"nothing is happening yet"* rather than faking activity
  ([043](decisions/043-page-contents.md), [020](decisions/020-motion-and-ux.md))
- ☑ **Notifications** — `/notifications` plus a **server-rendered** unread count in the headbar.
  Rows had been written on every invite and roster change since the teams slice with nothing able
  to read them; invites were findable only because the teams page listed them as a stopgap.
  Reading the inbox is what marks it read. Polling stays a 5c enhancement on a number that is
  already correct without JavaScript ([043](decisions/043-page-contents.md), [018](decisions/018-frontend-architecture.md))
- ☑ **LFT board + recruitment** ([056](decisions/056-lft-board.md)) — **built 2026-08-17, closed 2026-08-18**
  - `/lft`: one server-rendered two-sided board defaulting to **CS2 / teams recruiting**, with game,
    view and every filter in the URL. `noindex, follow`, and it browses without an account
  - **Freshness orders it and rating never buys prominence** — proven by putting a 2400-rated team
    below a newer unrated one. Rating-first would turn evidence into permanent visibility and starve
    exactly the teams the board exists to serve
  - Five distinct objects (`TeamOpening`, `OpeningApplication`, `TeamInterest`, `RecruitmentBlock`,
    `ContactGrant`) plus an expanded `LftListing` — never a polymorphic row whose nullable keys
    change its meaning
  - **A team approach always names a real opening**, enforced server-side, so a context-free
    approach does not exist. Caps of ten pending per side, per *team* not per opening — counting per
    opening would let a team with five vacancies approach fifty people
  - Unrated and already-rostered players stay eligible and are labelled honestly. Capacity bounds
    openings, so a vacancy is never a promise the roster cannot keep
  - Mutual acceptance reveals exactly one chosen verified contact each way, bounded to 30 days.
    **No recruitment chat.** Public projections contain no identifier at all
  - Fourteen-day adverts that never auto-renew; a material change starts a cycle and neutrally
    closes old requests while a **note edit does neither**; seven-day requests; durable blocks;
    a lifecycle worker for expiry, one reminder and retention — and **public reads exclude expired
    rows without it**, so a late worker can never leave a stale advert up
  - Erasure removes listings, applications, interests, grants and blocks immediately
  - ☑ **Reporting and moderation** — a `<details>` control on every card for signed-in visitors,
    one report per person per advert enforced by a partial unique index, and `/admin/reports` for
    the human 056 promised. **Report stays separate from decline and block**: an ordinary decline
    must never be recorded as an abuse allegation, and a block is a private pair-level control that
    needs no moderator
  - ☑ **Availability capture and overlap filtering** — the columns and the UTC hour-of-week
    normaliser had shipped with nothing writing them and nothing reading them. Both publish forms
    now capture a window, and `/lft` gains `day/from/to/tz` alongside every other filter, in the URL
  - **The check that earns the design is cross-zone**: a filter written in London hours must find a
    Warsaw advert an hour later. Comparing the numbers people typed would get that backwards in both
    directions, which is precisely what normalising to UTC on write makes impossible. An unknown
    zone or a backwards range is **refused**, like every other filter value — one that silently
    stops applying shows a broader set than the URL claims
  - ⚠ **The first working version was wrong in three ways no same-zone check could see**, all
    corrected before the slice closed:
    - It pinned a **January reference week**, so every window used winter offsets. Warsaw and London
      shift together and hid it; Moscow has been UTC+3 with no DST since 2014, so all summer a
      Moscow filter missed a Warsaw advert by exactly one hour — both sides wrong together, which is
      why nothing failed. Conversions now anchor to the week they happen in
    - It read any stored set back as a **rectangle** (one set of days, one min/max range). That was
      true only because the form could not produce anything else, so the guarantee lived in a form
      rather than in the function. `FromUtcHours` now decomposes into maximal contiguous runs and is
      exact for any set — checked against a shape the form still cannot make
    - It converted through the **hour that does not exist**. Local 02:00–03:00 on the spring-forward
      Sunday throws, and nudging it would silently move somebody's morning; that hour is now simply
      not available. The check that found this was itself wrong first — it pointed at 2026-03-30, a
      Monday, and so at an ordinary Sunday a week after the switch
  - **A window per weekday, not one for the week** — seven rows, still no script. Weekend mornings
    and midweek evenings are both expressible, which one window could not hold
  - **GIN indexes on both `availability_hours` columns**, and a check asserts the predicate reaches
    SQL as `&&` rather than an `EXISTS` over `unnest()`. An index nothing reaches is storage
  - `ponytail:` remaining ceiling — an advert stores the hours computed in its publication week, so
    one straddling a switch is an hour out for the rest of its life. At most 14 days, twice a year,
    in a discovery filter. Upgrade path: renormalise live adverts from the lifecycle worker that
    already sweeps them
  - Availability **narrows and never gates**: an advert without a window still publishes and is
    still listed — 056 makes only game and roster slot hard requirements. The board labels a window
    in the publisher's own zone, since nothing stores a viewer's yet and a silently shifted
    schedule is worse than a labelled one
  - ☑ **Contact → invitation** — the board's only real success metric, and until now the trail went
    cold at two revealed identifiers. A captain converts a live grant into the **ordinary** roster
    invitation from [045](decisions/045-teams-and-rosters.md), under the same per-slot capacity cap,
    and the request becomes `Converted`
  - **Converting does NOT fill the vacancy.** The invitation is pending; the opening closes when the
    player *accepts*, because a recruiter must not be able to mark their own success — 056 draws
    exactly that line between a conversation and a result. Everyone else waiting on the filled
    vacancy is closed **neutrally** as `OpeningClosed`, which is not a rejection of them
  - `TeamMembership.FromTeamOpeningId` is what keeps the two events **separately measurable**, which
    056 requires and which marketing claims depend on: without it "a player joined a team" and "the
    LFT board filled a vacancy" are the same row
- ☑ **News/CMS** — `/news` and `/news/{slug}`, scheduled posts invisible until their time
  - `DisableHtml()` **plus a scheme allow-list** (http, https, mailto, relative, anchor). Allow-list
    rather than block-list, because the dangerous schemes are open-ended and the useful ones are not
  - **Filtering happens on the parsed document, not on `HtmlRenderer.LinkRewriter` alone.** The hook
    was tried first and did *not* fire for every link shape: `javascript:` was caught while a
    scheme-relative `//evil.example` reached the output untouched. A defence that appears to work
    because the tested case happened to be covered is the worst kind
  - ⚠ **The check caught a real bug in the first version of the filter**: `//evil.example` also
    starts with `/`, so testing "is it relative?" before "is it scheme-relative?" waved it straight
    through ([051](decisions/051-phase-4-remainder.md) §8, [040](decisions/040-threat-model.md))
- ☑ **Seasons archive** — `/seasons` and `/seasons/{n}`, frozen by **timestamp, not snapshot**
  - `Season.ClosedAt` ships, and a **database CHECK constraint** refuses to close a season without
    recording when. Nothing in the app closes one yet; the constraint exists so that whatever
    eventually does cannot skip it — and it immediately caught two existing checks doing exactly that
  - The archive is the ledger read as at that instant, which is how
    [021](decisions/021-site-structure.md)'s *"never recomputed"* and
    [025](decisions/025-rating-system.md)'s *"derived at read time"* both become true at once.
    Proven by two seasons around the same results: the one closing before them must not contain them
- ☑ **Search** — headbar form + `GET /search?q=`, teams/players/tournaments, works with JS off
  - **[043](decisions/043-page-contents.md)'s SQL genuinely did not execute.** `unaccent()` is
    `STABLE`, so PostgreSQL refuses to index any expression containing it. An `IMMUTABLE`
    `f_unaccent` wrapper ships with the migration, plus trigram indexes over all three tables
  - Applied to **both sides** of the comparison, so `lodz` finds `Łódź` and the reverse — a one-sided
    fix looks correct in whichever direction it was tested. `%` and `_` stay literal text
  - Drafts stay sealed (the search goes through `PublicTournaments`) and erased accounts are not
    findable, since erasure that leaves you searchable is not erasure
- ☑ **Organiser panel** — servers ☑, disputes ☑, **staff ☑**
  - `/org/disputes` lists contested matches for organisers the actor may referee, separating the
    machine's doubt (`Integrity`) from a captain's claim (`DisputedAt`) rather than merging two
    different facts
  - **Dismiss** releases the parked rating; **reverse** records the corrected winner and rebuilds
    every rating from the ledger. Both are audited, both are refused-and-audited for anyone else,
    and **neither touches the bracket**
  - Reversal supersedes rather than deletes, so the old numbers stay answerable, and the ledger's
    unique index gained a `superseded_at IS NULL` filter so a replayed generation may coexist with
    the one it replaces. `ponytail:` ceiling — the replay is platform-wide per reversal; upgrade
    path is a per-season snapshot, and [051](decisions/051-phase-4-remainder.md) §4 already gives
    seasons a closing timestamp
  - `/org/staff` — **every organiser-scoped permission in the product reads `organiser_staff`, and
    until now nothing could write it.** `Actors.CurrentAsync` has built the organiser half of an
    `Actor` from that table since Phase 1, so a referee, an admin or an owner existed only if
    somebody ran SQL by hand. Grant by nickname, change a role in place, revoke — owner-only,
    because a referee who can appoint referees is not a referee
  - **The last owner cannot be removed, and cannot demote themselves either** — the same safeguard
    as "the last captain cannot leave" ([045](decisions/045-teams-and-rosters.md)), and a safeguard
    rather than a trap because handing over first is allowed. Demotion is the same hole as removal,
    so one rule closes both, under the organiser row lock that stops two owners demoting each other
    on the strength of both having read "there are two of us"
  - `RevokeRole` is a **separate** audit action rather than a `GrantRole` row with a reason string:
    *when did this person lose access* is precisely the question the log exists to answer
- ☑ **Staff panel** — users ☑, orgs ☑, audit ☑, news ☑, merges ☑, reports ☑, maintenance mode ➡ Phase 7
  - `/admin/organisers` — **the `trusted` flag finally has a user interface.** It has been in the
    schema since the first migration and now decides whose results skip the challenge window, while
    the only way to set it was editing the database by hand. Owner-only; staff moderate people, and
    deciding whose results skip corroboration is a different authority
  - `/admin/users` — ban and unban, always with a reason. A ban **deletes every live session** and
    is enforced in `Sessions.ValidateAsync`, so access ends on the next request rather than whenever
    a cookie expires. Staff cannot ban staff or the owner
  - The ban reason is shown **only after the password verified**, so it never becomes an
    account-enumeration oracle ([040](decisions/040-threat-model.md))
  - `/admin/audit` — read-only, with a denied-only filter. **Until this existed the log was storage
    rather than security**: an append-only record nobody can view protects nothing
  - `/admin/news` — **the CMS finally has an author.** The renderer, the scheme allow-list, the
    scheduling rule and pinned-on-home were all built and checked against rows inserted by hand, so
    the one content type the platform publishes had no way in. Create, edit in place, schedule, pin;
    an empty date means draft rather than *now*, and a duplicate slug+locale surfaces as a form
    error instead of a 500
  - **Deliberately a markdown textarea, not a rich-text editor.** The whole XSS defence rests on
    markdown parsed with raw HTML disabled ([051](decisions/051-phase-4-remainder.md) §8) — an
    editor emitting HTML would emit exactly the input the sanitiser exists to refuse. The check that
    matters drives the **author path** end to end: staff post `javascript:` through the real form,
    and the reader's page does not contain it. The seeded rows never proved that
  - `ManageNews` is its own operation rather than a second use of `ModerateUsers`: publishing to
    every visitor and sanctioning a person are the same authority today and need not stay that way
  - `/admin/merge` — **one person, two accounts.** [043](decisions/043-page-contents.md) chose the
    single account model so this would be rare, not so it would never happen: somebody registers
    twice before linking Steam, or gets a `setinfo` token onto the wrong account. Match
    participation, roster places and organiser roles move to the surviving account; Steam, Discord
    and the `setinfo` hash move **only where the survivor has none**, because a merge never
    overwrites a live identity with a dead one
  - **A merge moves history; it never decides which of two roles, rosters or results was the real
    one.** That single rule is why the code has no arbitration in it: where the two accounts
    genuinely coexisted — the same match, the same live roster, the same organiser — the merge is
    *refused* and a human resolves the overlap first. Two identities in one match is not a duplicate
    account, it is one human counted twice in a result
  - **The ratings are restated by the replay a reversal already uses**, not by patching two players'
    numbers. Moving the participants changes who each result was about, and the sweeper then
    rebuilds from `(EndedAt, Id)` order — so `DisputeResolution.ReplayAsync` gained a second caller
    rather than the merge growing arithmetic that looks careful and is wrong
  - The losing account ends as the **same anonymous tombstone erasure produces**: `AccountDeletion`
    kept one copy of that path and the merge calls it, because two hand-written GDPR erasures will
    always drift. Confirmation is the losing nickname typed exactly, like disbanding a team (045);
    superseded ledger rows and audit rows keep the old account id, because both record what was
    true when they were written
  - `/admin/reports` — the reported-advert queue. **A takedown sets `ClosedAt` as well as
    `ModeratedAt`**, which is why not one public query changed: every board read already filters on
    `ClosedAt == null && ExpiresAt > now`. The second column is the one that matters — a listing is
    *one row per account and game*, reused on republication, so without it the author puts the
    moderated advert back with a single click and the whole queue is theatre. That is the check the
    slice is built around
  - One decision answers **every** open report about the same advert, so the queue cannot regrow
    with work already done; the card shows how many other people flagged it, because one complaint
    and six are different facts
  - `ponytail:` ceiling — a takedown removes the advert, not the advertiser. A *team opening* is a
    new row on every publish, so a determined team can post a fresh one; the escalation for that is
    the ban that `/admin/users` already performs, not a weaker per-advert duplicate of it

- ☑ **Panel navigation** ([021](decisions/021-site-structure.md), [036](decisions/036-authorization.md))
  - **Nine working pages had no link anywhere in the product.** Every slice skipped navigation
    because the pages beside it had none either, which is how a finished panel stays invisible.
    The headbar shows one entry to whoever has somewhere to go; panel paths render a section strip
  - Built in the layout rather than in each page, so a **new** section cannot ship unreachable the
    way every existing one did. Each entry asks an **operation**, never a role, so the nav cannot
    drift from what the page will actually serve — a link that appears and then 403s is worse than
    no link
  - ⚠ **It surfaced a real gap, since fixed.** `/org/disputes` built its list from the actor's *own*
    organiser roles, so platform staff were refused **by the page** — while
    [036](decisions/036-authorization.md) grants them `ResolveDispute` precisely so they can settle
    disputes *between* organisers, which by definition have no referee inside either one. The policy
    had always answered correctly; the page never asked it about anybody else's organiser. The
    candidate set is still reduced to concrete ids and then filtered by the policy, so an
    organiser's referee still sees only their own events. `ponytail:` ceiling — a platform role
    loads every organiser id; upgrade path is to restrict candidates to organisers that currently
    *have* a contested match, which is the query the page runs next anyway
- ☑ **Account merge moved to owner-only** — it shipped on `ModerateUsers` because
  [015](decisions/015-admin-panel.md) lists merges beside bans, which is where they belong on a
  screen. But a ban ends an account's access, while a merge rewrites whose results a finished match
  was about and then replays every rating on the platform. `SetOrganiserTrusted` was already
  owner-only for strictly less, and blast radius is what these rules exist to track

> ### What ◐ means here
>
> **The mechanics work end to end and are covered by checks; the surface around them is not
> built.** A slice is not done when the thing it enables has nowhere to live — an invite that
> writes a notification nobody can read, or a rating with no page showing player ratings.
>
> These marks were audited on 2026-08-11 and found wrong in *both* directions: ranking was
> marked done and was not, and two tenancy checks had been passing since Phase 1 while still
> listed as pending.

### Security checks that ship with the slices ([040](decisions/040-threat-model.md))

- ☑ Tenant isolation — B cannot see A's tournament; by-ID returns **404 not 403** *(proven against real PostgreSQL since Phase 1, and again for drafts in [046](decisions/046-tournament-lifecycle.md))*
- ☑ No tenant context → **throws**, returns nothing
- ☑ SSRF — **re-scoped, and mostly deleted rather than defended** ([051](decisions/051-phase-4-remainder.md) §2). [040](decisions/040-threat-model.md) called this the top risk on the grounds that it was "designed into server registration". It is not: the panel **never dials a game server**, so a user-supplied `host:port` is display data, never a fetch target. The only outbound sink in the product is the Discord webhook, where the exact-boundary check, redirect refusal and private/reserved-DNS refusal already ship ([014](decisions/014-discord.md))
- ☑ Open redirect — `//evil.com`, absolute URLs, `javascript:` and `/\` all rejected
- ⊘ Sort allowlist — `?sort=id;DROP` rejected, never interpolated. **Nothing to test yet**: no page takes a sort parameter
- ➡ Zip bomb aborted; archive filenames never trusted. **Moves to Phase 6** with demo upload — the endpoint shares no state with anything Phase 4 designs, and its specifics (headers, zip layout, size ceiling) are what the real plugin will settle ([024](decisions/024-demos.md), [050](decisions/050-match-results-and-ratings.md) §6)
- ☑ **Stored XSS in news markdown** — closed and proven against Markdig 1.3.2, in the renderer *and* end to end through the real page. `javascript:`, `vbscript:`, `data:` and scheme-relative targets are emptied while the link text survives; `https:`, `mailto:`, relative and anchor links pass. Raw HTML is escaped rather than stripped, so an event handler appears as inert text inside an escaped tag — the check asserts no **tag** is emitted, which is the thing that actually matters ([051](decisions/051-phase-4-remainder.md) §8)
- ☑ **Recruitment spam** — ten pending per side, counted **per team** rather than per opening, and a unique index makes one-approach-per-pair-per-cycle a database rule. Blocks are durable across cycles. Proven by driving the eleventh approach and asserting it is refused ([051](decisions/051-phase-4-remainder.md) §5, [045](decisions/045-teams-and-rosters.md))
- ☑ **No secret appears in any log** — [012](decisions/012-security.md)'s check 5, finally written. A capturing log provider is installed over the real application, the paths that handle secrets are exercised (login, password reset, signed plugin traffic, a deliberately failing request), and the captured output is searched for the HMAC secret, the TOTP seed, the password and the reset token hash. The check first asserts it is receiving output at all, so it cannot pass by seeing nothing
- ☑ **Secrets encrypted at rest** ([012](decisions/012-security.md)) — the three `ponytail:` ceilings are closed. `GameServer.HmacSecret`, `Account.TwoFactorSecret` and `MatchParticipant.LinkCode` are encrypted through Data Protection with a key held **outside the database**; none can be hashed, because each has to be replayed to verify a signature, derive a code or be shown to its owner. A startup backfill encrypts anything written before this existed — a migration cannot, having no access to the key ring — and is idempotent. Proven by registering a server and then **signing a real heartbeat with the stored credential**
- ☑ **Rate limiting behind a proxy** ([011](decisions/011-accounts-and-login.md), [042](decisions/042-hosting.md)) — the limiter partitioned on the socket address, which behind Cloudflare is *Cloudflare's* address: one bucket for the entire internet, and one attacker locking everybody out of logging in. `X-Forwarded-For` is now honoured, but **only for requests that actually arrived from a configured network** — trusting it unconditionally would let anyone mint a fresh bucket per request and remove the limiter entirely, which is strictly worse than the bug. One hop only; the framework's default loopback trust is cleared; empty configuration leaves the middleware inert for a direct-connect deployment
- ☑ CSRF — an untokened POST is refused ([011](decisions/011-accounts-and-login.md))
- ☑ Account enumeration — unknown address and wrong password are indistinguishable by **message and by timing**, on both login and password reset
- ☑ Brute force — **token-bucket** limiter on every auth POST, page views never counted ([047](decisions/047-two-factor.md))
- ☑ Session revocation — ordinary logout kills the current session; password reset and the explicit `/me` control kill **every server-side session** for the account ([047](decisions/047-two-factor.md))

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

## Phase 5b — Server browser & SEO engine ([057](decisions/057-server-browser-and-seo.md))

**Placed after Phase 5 by decision**, so the most-visited public surface in the product
launches with a decided palette and a designed OG card rather than in monochrome.

> ⚠ **The cost, recorded:** the CS2/CS:GO half needs no plugin and could ship today, and
> search rankings mature on a clock that cannot be shortened. **This phase is blocked on
> Phase 5, which is blocked on [044](decisions/044-visual-direction.md) — still open.** Until
> 044 closes this has no date; if it stays open, revisit rather than let it drift.

**Lenses:** `seo-audit` · `site-architecture` · `programmatic-seo` · `dotnet-core-expert` ·
`secure-code-guardian` (a listing is a new public write surface) · `postgres-pro` (time series)

- ☐ **Steam Web API ingestion and polling** — CS2 (app 730) and CS:GO (app 4465480), polled every ~15 min and **stored as a time series**. Nobody else keeps history, so this is differentiation by analysis rather than access. **No outbound packet
  ever reaches a listed server**: [051](decisions/051-phase-4-remainder.md) §2 holds, and A2S
  dialling is refused outright as a reflection/amplification vector
- ☐ **Plugin activity reporting for CS 1.6** — five-minute window, **humans separated from
  bots**, giving a time series nobody can scrape. Depends on Phase 6
- ☐ **Per-server pages first** — occupancy history, peak hours, human-vs-bot over time. The
  unmatchable data, and what makes the page substantial enough to index
- ☐ **Three confidence tiers on every count** — `Verified` (plugin, humans counted inside),
  `Plausible` (public data, behaves like people), `Implausible` (flat at capacity for weeks).
  Third application of the show-your-uncertainty principle behind `±RD` and `Declared`/`Bound`.
  **Show the observation and the graph, never a verdict** — and the remedy for a disputed
  inference is installing the plugin, so the accusation and the call to action are one sentence.
  A tier is displayed and filterable and **never an ordering input** (⊘ blocked on **V11**)
- ☐ **Derived from the same series** — best time to play, uptime, observed map rotation,
  dead-server pruning. All from public data; none of it offered by the incumbents
- ☐ **Facets behind a content floor** — `game × type`, `game × region`. Below the floor,
  `noindex`; pagination past page 1 and sort orders always `noindex`. **The floor is the whole
  safety mechanism** — scaled thin pages demote the entire domain, ranking and match pages included
- ☐ **Guides in parallel from day one** — they rank with an empty catalogue and reach the
  owner who makes it non-empty
- ☐ **Votes** — only from an account the server confirmed was playing there in the window.
  Rolling month, each closed month frozen into a permanent page, read as at its closing instant
- ☐ **Two surfaces** — `/serwery` ordered by live humans; `/ranking/serwery/{month}` by votes.
  Verification is a badge and a filter, never a position boost
- ☐ **Sponsored slots** — labelled, beside or above a list, **never inside the organic order**.
  **The first revenue line to ship** ([058](decisions/058-what-we-sell.md) §7): it needs no plugin
- ☐ **Boost — paid position inside the list** ([058](decisions/058-what-we-sell.md) §6). Overturns
  057's hard line under four rules, and the fourth carries the design:
  - ☐ **`Verified` servers only** — all three eligibility routes, partnership included. Money never
    buys the badge; the badge is the ticket to buy anything. **So this cannot ship before the
    activity plugin does** (Phase 6), which puts a free component on the critical path to revenue
  - ☐ **The band reads `PROMOWANE · ZWERYFIKOWANE`** — bordered, badged, rendered server-side and
    present with JavaScript off. `Promowane` is the **legally required** disclosure;
    `Zweryfikowane` is the half no competitor can say. **`MasterBoost` rejected — GO-Setti's brand,
    and "boost" alone never says *paid*.** Product name TBD; the disclosure ships regardless
    (⊘ blocked on **V12**)
  - ☐ **Boost moves a row and nothing else** — count, human/bot split and confidence tier are
    byte-identical boosted or not. The ranking `/ranking/serwery/{month}` is untouched entirely
  - ☐ **Capped per page and per facet**, sold per period, **never auctioned**. A padding flag
    (**V11**) or lost verification ends an active boost
- ☐ **Aggregate activity always; named presence opt-in, default off** — a verified identity's
  live location is not broadcast without consent ([011](decisions/011-accounts-and-login.md),
  [012](decisions/012-security.md))
- ⊘ **V8 / V9 / V10** — CS:GO app id, Steam API shape and rate limits, CS:GO population.
  The listing itself measures V10 before any CS:GO plugin work is committed

## Phase 6 — Plugins

**Lenses:** `csharp-developer` · `dotnet-core-expert` (the plugin is a .NET 8 assembly) · `test-master` · `debugging-wizard`
**No lens fits Pawn/AMXX** — CS 1.6 plugin work is unassisted by the available skills, which is itself a reason to sequence it last

Concretely, one at a time. **No shared abstraction until both exist** ([001](decisions/001-game-scope.md)).

> **Stack settled 2026-08-22 by [058](decisions/058-what-we-sell.md) §5.** **AMXX / SourceMod /
> CounterStrikeSharp**, dropping to native Metamod only where a specific capability demands it.
> Rewriting in native C++ to resist decompilation was considered and rejected: it buys obfuscation,
> not protection, and pays for it by escalating the exact fragility [002](decisions/002-cs2-plugin.md)
> §Tier 4 calls the project's largest operational risk. **Nothing sellable ships inside the binary** —
> every paid line is enforced on our server.

- ☐ **CS2 plugin** ([002](decisions/002-cs2-plugin.md)) — CounterStrikeSharp, original code
- ⊘ **V5 closed by design** — [051](decisions/051-phase-4-remainder.md) makes the normal contract
  plugin-initiated and forbids the panel from storing or using RCON. Reopen 051 before adding it
- ☐ **Verify V2b / V3 / V4** on a real CS 1.6 server before writing the AMXX plugin
- ☐ **CS 1.6 plugin** ([005](decisions/005-cs16-plugin.md)) — Pawn/AMXX
- ☐ **Round backup/restore verified by actually killing a server mid-match** ([034](decisions/034-test-strategy.md))
- ☐ **Standalone licensing** ([058](decisions/058-what-we-sell.md) §3a) — **connected is free,
  standalone is paid.** No licence check ships in the plugin, because a branch can be patched:
  **the match config is withheld instead.** One column on the tenant and one branch in the config
  endpoint; the secret [048](decisions/048-organiser-server-configuration.md) already issues *is*
  the licence key, so there is no new artefact. The refusal must be **typed and distinguishable
  from a network failure** ([009](decisions/009-api-contract.md))
  - ☐ **A live match is never killed for a commercial reason** — config is fetched once at match
    start and the match completes offline. Licensing gates the *start*, never a running game
    ([033](decisions/033-maintenance-mode.md)). This is check 4 in 058 and the one most likely to
    be broken later by tidying the licence path
  - ☐ **Seat = concurrent live matches**, not installs. Overage refuses the next match, not the
    current one; a secret seen from an unregistered `host:port` **flags for a human and blocks
    nothing** ([052](decisions/052-cheating-and-rating-integrity.md) §3)
- ☐ **Activity-reporting plugin** ([057](decisions/057-server-browser-and-seo.md) §2) — the CS 1.6
  five-minute window with humans separated from bots. **Stays free forever**
  ([058](decisions/058-what-we-sell.md) §2): it is what makes the catalogue proprietary, and a paid
  one would shrink the catalogue back to public data
- ☐ **Anti-cheat — two components** ([058](decisions/058-what-we-sell.md) §6c,
  [052](decisions/052-cheating-and-rating-integrity.md)). **The sold half is the server side; the
  client scanner is free.**
  - ☐ **Client scanner — on-demand, never live** — the player runs it when the server-side AC or an
    admin sends him to verification. **One scanner covers all three games** (it inspects the
    machine, not the game; only signatures differ), and it never runs during a match, so
    zero-install stays the default. **Ships before the sold half** — it produces corpus rows
    rather than consuming them
  - ☐ **The report reaches our panel and the player**, with the verdict, so a finding can be
    disputed specifically rather than a silent ban being argued about
  - ☐ **A tampered, unsigned or missing report is refusal, never clean** — the report travels
    scanner→panel directly, signed; the panel copy is authoritative and is never a file the player
    relays. **A clean report clears nothing by itself**
  - ☐ **HWID: salted hash only, never the raw value.** It **widens a ban already decided** and
    never creates one, or a forged identifier becomes a way to ban a rival. Every automatic ban is
    appealable — an HWID ban hits a machine, and machines are shared (⊘ blocked on **V13**)
  - ☐ **Server-side AC — the sold half.** Decides the unambiguous class alone (aim snapping,
    anti-aim, spinbot). **The 65/35 split is an illustrative estimate, never a measurement** — do
    not publish it, price against it, or pitch it; the ambiguous residue goes to the referee desk
    and *is* the corpus (⊘ blocked on **V14** before any auto-ban or published rate)
  - ☐ **Suspicion is a meter, not a switch** — a flag opens a **monitoring phase** and starts the
    demo; the phase ends when the meter **falls** (silently, storing nothing, notifying nobody —
    **this must be the common case**) or **rises past the threshold**, which is what triggers the
    scan. Fourth application of the show-your-uncertainty principle behind `±RD`,
    `Declared`/`Bound` and the listing confidence tiers
  - ☐ **A meter that collapses the moment recording starts is itself scored** — otherwise a cheater
    toggles off while watched, waits out the phase and toggles back on, and every cycle looks like
    a false positive resolving cleanly
  - ☐ **Appeal answered with the POV demo from the monitoring phase** — recording is armed **by us,
    at the flag**, so its existence is never the player's choice and *"I was not recording"* is not
    a defence. He uploads on appeal, so **nothing is stored unless he appeals**. **An interrupted or
    stopped recording counts as refusal, never clean**
  - ☐ **Per game** ([CONTEXT §2.9](../CONTEXT.md)) — **CS 1.6: proven** via AMXX `client_cmd`.
    **CS:GO / CS2: open (V15).** **GOTV is not a substitute** — snapshot rate and interpolated,
    quantised angles smooth away the per-tick view-angle behaviour an aimbot signal lives in, and
    it is the *expensive* artefact ([024](decisions/024-demos.md) budgets ~156 GB/year) while a POV
    demo costs us nothing until someone appeals
  - ☐ **If forcing fails on CS2, ask instead** — a player told to record either does or does not,
    and a missing recording is already **refusal, never clean**. Weaker, not a dead end
  - ☐ **A GOTV-only verdict is labelled as such** — a referee must never record a decision as if it
    rested on POV evidence that did not exist
  - ☐ **Check `cl_filterstuffcmd` while testing V2b/V3** — the CS 1.6 command filter names `setinfo`
    among the commands it restricts, and `setinfo` is the identity mechanism
    ([005](decisions/005-cs16-plugin.md), [010](decisions/010-identity-binding.md)). A player who
    enables the filter may also refuse `record`, which §6c already treats as refusal, not clean
  - ☐ **Third-party AC integration can land first** ([058](decisions/058-what-we-sell.md) §6b) —
    CS 1.6 servers already run **fungun/ECD** and **Wargods**. We record `(vendor, version,
    verdict)` on the participant rather than asking anyone to switch. It is **a record, not a
    detector**, so it does not wait on the corpus
  - ☐ **A third-party verdict never bans, never rates, never buys a confidence tier** — it reaches
    us through a server [007](decisions/007-server-hosting-model.md) treats as untrusted, so it is
    provenance-tagged evidence a referee weighs ([052](decisions/052-cheating-and-rating-integrity.md) §3).
    Several of these tools are old, closed and publicly bypassed: *"an AC was running"* is not a
    security claim and the product must never present it as one
  - ☐ **Named as an observation, never an endorsement** — `Anty-cheat: Wargods (zgłoszony)` on a
    server page; `Współpracuje z ECD, fungun i Wargods` in our own copy. **`Powered by` rejected**:
    it claims a relationship we do not have and points the wrong way — a server owner chose to run
    it, nothing of ours is powered by it. No vendor logos, no "official", no "partner" — **for servers
    we do not own, and until something is signed**
    ([058](decisions/058-what-we-sell.md) §6b)
  - ☐ **Two exceptions** — on **our own managed servers** (058 line 4) running ECD is a fact about
    our infrastructure, and **ECD is free to use**, so it ships at no licence cost. A **signed
    partnership** lifts the restriction generally. Whether to pursue one with ECD or Wargods is a
    **marketing-strategy question**, deferred there deliberately

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
| V2b | Is `setinfo` readable at connect time, early enough to gate team joining? | [005](decisions/005-cs16-plugin.md) — **and while testing it, check `cl_filterstuffcmd`**: the CS 1.6 command filter names `setinfo` among the commands it restricts ([CONTEXT §2.9](../CONTEXT.md)). The filter blocks the *server* setting it and our design has the *player* set it, so this is probably harmless — but it is too close to the identity mechanism to leave unchecked |
| **V3** | Does `setinfo` behave identically on **non-Steam clients**? | [005](decisions/005-cs16-plugin.md) — **the one that could hurt**, since most active 1.6 servers admit non-Steam clients |
| V4 | Does the info-key length limit allow a high-entropy token? | [005](decisions/005-cs16-plugin.md) |
| V5 | Is RCON needed for the normal CS2 match lifecycle? | **Resolved by [051](decisions/051-phase-4-remainder.md): no.** The plugin dials us; a future outbound-control feature must reopen that boundary |
| **V8** | Is CS:GO legacy's App ID really **4465480**? | [057](decisions/057-server-browser-and-seo.md) — **single-sourced**, confirm against Steam before it reaches code |
| **V9** | Does `GetServerList` return community servers for app 730 and CS:GO, at a rate limit that allows polling the whole Polish catalogue every ~15 min? | [057](decisions/057-server-browser-and-seo.md) — **gates the confidence tiers, not just the listing** |
| **V10** | Is the CS:GO community-server population large enough to justify reopening [001](decisions/001-game-scope.md) for tournaments? | [057](decisions/057-server-browser-and-seo.md) — **the listing measures it for free** before any CS:GO plugin work |
| **V11** | Are padded player counts distinguishable from real ones over a multi-week series, at a publishable false-positive rate? | [057](decisions/057-server-browser-and-seo.md) §5a — calibrate against plugin-verified servers; a false accusation cannot be undone |
| **V12** | Is **`Promowane`** sufficient disclosure wording, and a bordered band sufficient prominence, under the Polish implementation of the Omnibus Directive (2019/2161) and the DSA — and must the ranking criteria themselves be published? | [058](decisions/058-what-we-sell.md) §6 — **boost cannot ship until answered**. The label is decided; whether it satisfies the statute is not. The only item here gated on law rather than an engineering answer; single-sourced, so confirm against the current text or counsel before it reaches code |
| **V13** | On what lawful basis do we collect and retain a hardware identifier, for how long, and how does a permanent ban list coexist with the erasure right [011](decisions/011-accounts-and-login.md) implements? | [058](decisions/058-what-we-sell.md) §6c — **blocks HWID banning**. A hardware identifier is personal data, and [012](decisions/012-security.md) currently advertises **zero persisted application IPs**: this is a deliberate reversal of that posture and must be assessed and written down. Salted hash only |
| **V14** | Does the server-side AC actually decide ~65% of cases, at a false-positive rate low enough to auto-ban on? | [058](decisions/058-what-we-sell.md) §6c — **blocks auto-banning and any published detection rate**. The figure is an estimate made before a single match; calibrate against the corpus. A false ban in a scene this small cannot be undone, the same reasoning as **V11** |
| **V15** | **Can the server force client POV recording on CS:GO / CS2**, and does CS2 `record` still freeze the client in 2026? | [058](decisions/058-what-we-sell.md) §6c — **reopened 2026-08-22.** CS 1.6 proven and closed. **GOTV is not a substitute**: snapshot-rate interpolation smooths away the per-tick view-angle detail that distinguishes an aimbot ([CONTEXT §2.9](../CONTEXT.md)). **Answer by testing a live CS2 client** — the web recycles one 2023 post. If forcing is impossible, asking still yields a signal, since a missing recording is refusal |
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
