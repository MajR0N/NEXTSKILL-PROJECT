# NEXTSKILL

**A tournament, rating and recruitment platform for the grassroots Counter-Strike scene — CS2 and CS 1.6.**

> A portfolio project. It is built as a real product would be: researched first, decided in writing,
> then implemented against a real database with an automated check suite. Nothing here is a mock-up.

---

## What problem it solves

Competitive Counter-Strike has two tiers. FACEIT and ESEA serve the top: paid infrastructure,
centralised servers, anti-cheat clients. Below that sits a large **community scene** — server
owners and volunteer organisers in places like Poland, Turkey, Brazil and Russia/CIS — who run
their own cups on their own servers, usually with a spreadsheet, a Discord channel and trust.

That scene has no good tooling. Brackets are drawn by hand, results are reported by screenshot,
disputes are settled by argument, and nobody's rating follows them from one cup to the next.

NEXTSKILL gives an organiser what a FACEIT-tier platform gives, without FACEIT-tier cost or
infrastructure:

- run a real bracket on **their own game server**,
- have results **reported by the server itself**, not by the players,
- have those results become a **rating** that travels with the player and team,
- have disputes go to a **referee with real authority** to reverse a result.

**First partner:** Pukawka, a Polish CS community.

## The core idea: the server tells the platform what happened

The most important design decision is the **direction of control**. The platform never reaches into
a game server. Instead:

1. The platform serves one signed JSON match config.
2. A plugin on the game server runs the match (veto, knife, ready-up, series).
3. The plugin reports events back — HMAC-signed, sequenced, reconciled.
4. The platform treats those events as the *only* authority on who won.

A result that no server reported cannot move a bracket or a rating. That one rule is what makes the
rating trustworthy, and most of the system's integrity machinery exists to protect it.

```
 organiser ──creates──▶  tournament ──starts──▶  bracket (real FK-routed rows)
                                                     │  match config (signed)
                                                     ▼
                                              game server + plugin
                                                     │  signed events (map_end, player_bound, …)
                                                     ▼
   rating ledger ◀──settles── result ◀──integrity gates── plugin ingest API
   (Glicko-2 teams,                │
    OpenSkill players)             └──▶ bracket advances itself
                                   └──▶ captain confirms / disputes ──▶ referee desk
```

## What is built and running

The project is in **Phase 4 (web platform)**; Phases 0–3 (design, rating engines, wire contract,
integrity rules) are complete. **364 automated checks pass against a real PostgreSQL**, including
in-process end-to-end runs of the plugin pipeline, the bracket flow and the whole authentication
surface.

| Area | What it does |
| --- | --- |
| **Accounts & security** | Register/login, account-wide session revocation, password reset, two-factor (TOTP + SMS), recovery codes, verified Steam/Discord links, GDPR erasure that keeps anonymous match history, Argon2 password hashing, secrets encrypted at rest with a key held outside the database |
| **Teams** | Create, invite, accept, roster slots (5 + 3 + 2), promote, disband and captain-authorised restore |
| **Tournaments** | Private drafts, reusable templates, lifecycle-safe editing, seasons, registration, seeding, captain check-in, audited forfeits, durable Discord announcements |
| **Brackets** | Generated into the database on start; routing as real foreign keys; byes and forfeits resolved; **grand-final reset armed live**; phones get one round at a time instead of a shrunken tree |
| **Match flow** | Captain readiness, web **veto** with durable legal turns and lazy 30-second expiry, BO1/BO3/BO5 map-and-side output |
| **Plugin API** | HMAC-signed ingest, sequencing, reconciliation, integrity gates; a simulator project stands in for a real plugin |
| **Ratings** | Teams on **Glicko-2**, players on **OpenSkill**; an append-only ledger and a background sweeper settle matches in `(EndedAt, Id)` order |
| **Referee desk** | A reversal supersedes the ledger and **replays every rating from history** |
| **Audit log** | Append-only *enforced by PostgreSQL*; a **denied** privileged attempt still leaves a row |
| **LFT board** | Recruitment marketplace: freshness-ordered (rating never buys prominence), weekly-availability overlap across time zones, mutual consent before any contact is revealed, moderation queue |
| **Home, search, news, seasons, notifications** | Live matches and results with designed empty states; accent-insensitive trigram search that works with JavaScript off; scheduled markdown news with raw HTML disabled; archived seasons read as at their closing instant |
| **Staff & organiser panels** | Bans that end every live session, news CMS, advert moderation, owner-only account merges, per-organiser staff roles |

Open items are listed honestly in [`docs/BUILD-PLAN.md`](docs/BUILD-PLAN.md) — what remains is
blocked on things outside the build (a real email/SMS provider, team-logo policy, the Phase 6 game
plugins), not on unwritten code.

## Engineering decisions worth looking at

**Written decisions, not tribal knowledge.** There are **57 decision records** in
[`docs/decisions/`](docs/decisions). Each states the context, the options, the choice and what
would reverse it. Several are deliberately *reopened* when new evidence arrived — the history of
being wrong, and correcting it in writing, is part of the record.

**Research that gets re-verified.** [`CONTEXT.md`](CONTEXT.md) holds every external fact with its
source and the date it was checked. Examples: how MatchZy's remote-config flow inverts the control
direction; why the CS2 plugin layer is fragile across Valve updates; that CS:GO returned to Steam as
a frozen community-server build in 2026.

**Measure, don't argue.** The `tools/` directory holds instruments built to settle questions that
cannot be settled by reading: a CS 1.6 `setinfo` probe, a CS2 POV-recording check, a Steam
server-list probe. Findings have overturned earlier conclusions, and the docs say so.

**Integrity by construction.**
- *Append-only audit enforced in the database*, not in application code.
- *Lineup provenance*: a player is `Declared` until a `player_bound` event carrying **both** identity
  factors promotes them to `Bound`; only trusted participants count toward ratings.
- *Rate limiting behind a proxy* trusts `X-Forwarded-For` only from a configured network.
- *Secrets that must be replayed* (signing credential, TOTP seed) are encrypted, not hashed, with a
  startup backfill closing the migration window.

**Anti-cheat and the law, thought through.** Cheat-detection design is documented together with its
legal footing: GDPR treatment of hardware identifiers (a salted hash is still personal data), Art. 22
rules on automated bans (a human referee who can genuinely overrule), a Legitimate Interests
Assessment and DPIA draft in [`docs/legal/`](docs/legal), and Polish/EU paid-placement disclosure
rules for the server-list promotion product.

**A business model designed alongside the code.** [`058`](docs/decisions/058-what-we-sell.md)
defines five revenue lines and one rule: *give away what makes the catalogue and the rating bigger;
sell what is built on top of it.* Ratings and brackets stay free because charging for them would
destroy the asset everything else is priced against.

## Tech stack

| Layer | Choice |
| --- | --- |
| Backend | **ASP.NET Core (.NET 10)**, C#, Entity Framework Core |
| Frontend | **Razor Pages**, server-rendered, SEO-friendly, usable with JavaScript off; 4-locale ready (Polish base) |
| Database | **PostgreSQL** (trigram + `unaccent` search, DB-level constraints and triggers) |
| Ratings | Glicko-2 (teams), OpenSkill (players) — implemented in the domain layer |
| Plugins (planned, Phase 6) | C# / CounterStrikeSharp for CS2; Pawn / AMX Mod X for CS 1.6 |
| Tests | Dependency-free console check runner against a real database, plus `WebApplicationFactory` end-to-end runs |

## Repository layout

```
src/
  NextSkill.Api/        ASP.NET Core app — pages, features, plugin ingest, panels
  NextSkill.Domain/     Pure domain logic: brackets, ratings, integrity, veto, access
  NextSkill.Contracts/  The wire contract shared with game-server plugins
  NextSkill.Simulator/  A stand-in plugin that drives the real API
tests/NextSkill.Checks/ 364 checks (needs a local PostgreSQL)
tools/                  Measurement probes for open research questions
docs/
  decisions/            57 numbered decision records
  design/               Logo family, visual-direction prototype, contrast checks
  legal/                LIA / DPIA / privacy-notice drafts
  BUILD-PLAN.md         Phased plan, with honest status marks
CONTEXT.md              Shared project context and re-verifiable external facts
```

## Running it

Requires the .NET 10 SDK and a local PostgreSQL.

```bash
dotnet build
dotnet run --project tests/NextSkill.Checks      # the full check suite
dotnet run --project src/NextSkill.Api           # the site
```

## Design direction

Simple and professional — in the register of Cybershoke and FACEIT, never "gaming" styling. The
product ships monochrome on purpose while composition is validated, then adopts a neutral palette
with a single red accent. See [`044`](docs/decisions/044-visual-direction.md) and the logo work in
[`docs/design/`](docs/design).

## What this project demonstrates

- Taking a vague domain ("tooling for community CS") to a **specified, staged, testable system**.
- **Security and integrity** as first-class design inputs: signed ingest, defence against forged
  results, enforced audit trails, encrypted secrets, GDPR erasure.
- **Data modelling** where correctness matters: brackets as relational routing, an append-only
  rating ledger that can be replayed from history.
- **Honest engineering**: claims are measured, corrected publicly, and dated.
- **Product thinking**: monetisation, legal exposure and community incentives designed with the code.

## Status

Active development. Phase 4 is substantially complete; Phase 6 (the game-server plugins) is the next
gate to a first live cup.
