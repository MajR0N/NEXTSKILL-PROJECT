# 051 — Phase 4 remainder: sequencing, scope and the slices nobody had grilled

**Status:** decided
**Date:** 2026-08-11
**Relates to:** [007](007-server-hosting-model.md), [009](009-api-contract.md),
[012](012-security.md), [015](015-admin-panel.md), [018](018-frontend-architecture.md),
[020](020-motion-and-ux.md), [021](021-site-structure.md), [025](025-rating-system.md),
[028](028-veto-and-lobby.md), [036](036-authorization.md), [040](040-threat-model.md),
[043](043-page-contents.md), [045](045-teams-and-rosters.md), [048](048-organiser-server-configuration.md),
[049](049-team-page.md), [050](050-match-results-and-ratings.md)

---

[050](050-match-results-and-ratings.md) took the result pipeline. This document takes
everything else Phase 4 still owed: the slices [BUILD-PLAN](../BUILD-PLAN.md) listed as
needing a pass before being built, plus three places where two decided documents
contradicted each other.

---

## 1. Veto ships before matches, not after

[028](028-veto-and-lobby.md) puts map selection on the web. [018](018-frontend-architecture.md):16
puts SSE in Phase 5. Phase 4 therefore contained a slice it had ruled out the means to finish
— and since `MatchConfig` requires decided maps and `BracketBuilder` never sets `Match.Map`,
**no match could be configured at all.**

Resolved by separating the state machine from the transport:

- The veto is **server-rendered with form posts** in Phase 4. Turn order, legality and the
  timer are server-side and always were. [028](028-veto-and-lobby.md)'s own check #5 already
  demands *"full correct state from a plain fetch, no SSE required"*, so this version has to
  exist and be checked regardless.
- SSE in Phase 5 becomes a pure view layer that changes no server logic.
- [020](020-motion-and-ux.md):34 puts live veto in the *may require JavaScript* column, so
  the no-JS floor here is a courtesy rather than an obligation — but it is the floor that
  makes Phase 5 additive.

**Deferring veto was considered and rejected.** [005](005-cs16-plugin.md) gives the CS 1.6
plugin no veto at all, so web veto is not a differentiator there — it is the only map
selection CS 1.6 will ever have. Deferring would ship the moat game with no way to pick a map.

### The turn timer resolves lazily

Expiry is computed from `TurnStartedAt` plus the step duration, applied by any request that
reads veto state. No thread, no job. State stays correct because it is derived from
timestamps rather than accumulated by a ticker, which also means it survives a restart and
replays identically.

```
// ponytail: veto expiry resolves lazily on read, so a veto nobody is watching
// does not advance. Ceiling: if a match must be startable with both captains
// absent, add veto expiry as a second job in the rating sweeper's loop.
```

The timer exists to stop one captain stalling *the other captain* — who is, by definition,
someone looking at the page. A waiting captain with JavaScript disabled gets
`<meta http-equiv="refresh" content="5">` on the veto page only: native, zero JavaScript,
deleted in Phase 5 when SSE replaces it.

---

## 2. We never dial a game server

This closes the half of [048](048-organiser-server-configuration.md) that Phase 4 needs.

[007](007-server-hosting-model.md) said a server is *"proven by challenge-response at
registration"*. [009](009-api-contract.md) made the contract plugin-initiated in every
direction, and CONTEXT §3.1 says *"the panel never talks to the game server."* Both could not
hold.

**The server dials us.** The organiser registers `host:port` and receives a one-time secret to
paste into the plugin config. **The first correctly signed heartbeat sets `Verified`** — an
endpoint that already exists with passing checks behind it.

| | Panel dials out | Plugin dials in |
| --- | --- | --- |
| Proves | something answers at that address | **the plugin holds our secret** |
| SSRF surface | the full checklist: loopback, link-local, private ranges, DNS rebinding, redirects | **none — no user-supplied host is ever fetched** |
| Code to write | a hardened fetch path | one line in an existing endpoint |

This does not mitigate [040](040-threat-model.md)'s top risk, it **deletes** it. `host:port`
becomes display data for [013](013-server-connect.md)'s `connect ip:port` string and nothing
else. The only remaining outbound-fetch sink in the product is the Discord webhook, where the
boundary check, redirect refusal and private-DNS refusal already ship
([014](014-discord.md)).

**What was given up:** a server cannot be verified until a real plugin exists in Phase 6. In
Phase 4 the simulator plays that part, which is what [038](038-build-order.md) built it for —
and Phase 4 therefore ends with server registration built and *provably unusable by a real
organiser*. That is marked honestly rather than ☑.

---

## 3. Localisation becomes a habit now, not a sweep later

> **Built 2026-08-12.** `UiText` plus a neutral Polish resource forms the shared interface, the
> layout consumes it, and three checks prove neutral fallback, adapter use and `/de/...` rendering.
> Page-specific copy still moves when its page is touched; no bulk translation was claimed.

Before this slice, the plumbing had been in place since Phase 0 and nobody used it: `Program.cs`
registered `AddLocalization`, wired route → cookie → `Accept-Language` in the right precedence,
ran the middleware, and applied the locale prefix — while **not one `.resx` existed and no page
injected `IStringLocalizer`.** The built note above records the state after closing that gap.

Phase 4 is about to roughly double the page count. Every page built the current way is a page
to revisit — the same argument [BUILD-PLAN](../BUILD-PLAN.md) makes for shipping monochrome
until [044](044-visual-direction.md) closes.

**The neutral resource holds Polish.** .NET falls back to the neutral `.resx` when a key is
missing from a locale-specific one, so there is **no `.pl.resx`, no fallback logic and no
missing-key path to write**. `/de/teams` serves Polish exactly as it does today — by designed
fallback rather than by accident — and the day someone writes `.ru.resx` it simply works.
This matches how [043](043-page-contents.md) already treats news content, so UI strings and
content degrade identically.

New slices use `IStringLocalizer` from their first line. Existing pages are retrofitted as
they are touched.

> **Open, deliberately not decided here:** [018](018-frontend-architecture.md) chose
> **pl / en / ru / de**. CONTEXT §2.4's research says the active CS 1.6 scene is *"Turkey,
> Brazil, Russia/CIS and Poland"* — German is on nobody's list, and Turkish and Brazilian
> Portuguese, which are, were not included. With the fallback shape above the cost of being
> wrong is near zero, so this is flagged rather than resolved.

---

## 4. A closed season is frozen by timestamp, not by snapshot

Two decided documents contradicted each other:

- [021](021-site-structure.md):87 — *"Are old standings recomputed? **Never.** Frozen at
  season close. A later result reversal does not rewrite history."*
- [025](025-rating-system.md):30, :168 — *"store rating only. Ranking and tiers are derived at
  read time… never stored as columns."*

A derived view recomputes by definition, and [050](050-match-results-and-ratings.md) made
referee reversal-with-recompute a live path.

**Resolved by [050](050-match-results-and-ratings.md)'s ledger, which already had what was
needed.** It is append-only: a reversal marks rows superseded and appends new ones, so the
state at any past instant remains recoverable.

- The **archive** filters `AppliedAt <= Season.ClosedAt` and ignores the superseded flag.
- The **live ranking** does the opposite.

Same rows, two readings. "Frozen" and "derived" both become true, which is the tell that this
is a seam rather than a compromise. `Season` gains one column: **`ClosedAt`** — it currently
carries a bare `Closed` bool, which records *that* it closed but not *when*, and is therefore
useless as a filter.

Who won Season 2 is a **placement**, not a rating aggregate, and [049](049-team-page.md)
already specified placements as immutable with provenance.

```
// ponytail: season archive aggregates the rating ledger on every view.
// Ceiling: O(matches x subjects) per page load. Upgrade path: materialise
// SeasonStanding rows at close - the numbers are already immutable, so the
// snapshot can be added later without changing a single decision.
```

[016](017-deployment-and-cicd.md#code-optimisation-016) applied honestly: no optimisation
without measurement, and archives are the least-viewed pages in the product.

---

## 5. Recruitment has three objects, because the two directions cost different things

> **Completed by [056](056-lft-board.md).** This section decides why the persistence objects are
> separate and why cheap team interest exists. Decision 056 owns the public board, structured
> compatibility, contact consent, request lifecycle, private workspace and retention rules.

[049](049-team-page.md) designed team openings and applications and left the rest to "the LFT
slice". Three tables, not one polymorphic listing — the columns match, the *meanings* do not,
and a table where `TeamId IS NULL` changes what every row means is what someone decodes at 3am.

| Object | Direction | Cost to the sender |
| --- | --- | --- |
| `LftListing` *(exists)* | player advertises | none |
| `TeamOpening` | team advertises one game-scoped position | none |
| `OpeningApplication` | player → opening | none |
| `TeamInterest` | team → player | none |
| **roster invitation** *(exists)* | team → player | **occupies a roster place** |

The asymmetry is the reason `TeamInterest` exists. [045](045-teams-and-rosters.md) makes a
pending invite occupy a place, precisely so a full roster cannot spam two hundred people. A
captain with one open substitute slot who wants to talk to five candidates therefore
*cannot* — capacity forces them to commit blind, and the player receives a formal offer when
the honest first message is "are you still around?"

So interest is cheap and reversible, and an invitation stays what it is: a binding offer.

**But cheap interest reopens the hole [045](045-teams-and-rosters.md) closed**, since it
consumes no capacity and the roster cap stops being the rate limit. Paid explicitly:

- **at most 10 open interests per team** — withdrawing or expiring one frees a place;
- **one active interest per (team, player)**, so a captain cannot re-approach daily;
- **7-day expiry** — shorter than the 14-day listing freshness, because interest goes stale
  faster than availability does.

Same mechanism [045](045-teams-and-rosters.md) already proved — a bounded number of
outstanding approaches — measured against its own ceiling rather than the roster's. It
degrades honestly: a captain at the cap is told why, rather than meeting a button that fails.

Openings remain bounded by roster capacity: refuse to publish an opening the roster has no
room for, and close it when the capacity fills ([049](049-team-page.md)).

`NotificationType` gains `OpeningApplicationReceived` and `ApplicationDeclined`. The
completeness check that exists to catch enum drift will fail the build until the notification
rendering handles them, which is the check working as designed.

---

## 6. Search: [043](043-page-contents.md) stands, its SQL does not

The design survives scrutiny. Hand-rolling normalisation in C# was considered and loses on
both size and correctness: **`ł` has no canonical decomposition**, so
`String.Normalize(FormD)` + strip-combining-marks handles ą ć ę ń ó ś ź ż and silently fails
on the one letter in *Szkoła*. `pg_trgm` and `unaccent` ship with PostgreSQL — a native
platform feature, not a dependency to maintain.

**The SQL in [043](043-page-contents.md) does not execute.** `unaccent()` is not `IMMUTABLE`
(it depends on a reloadable dictionary), and index expressions must be:

```sql
CREATE FUNCTION f_unaccent(text) RETURNS text
  LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
  RETURN unaccent('unaccent'::regdictionary, $1);

CREATE INDEX ix_teams_name_trgm ON teams USING gin (f_unaccent(name) gin_trgm_ops);
```

Every query must call `f_unaccent` too, or the index is ignored.

**Phase 4 ships one headbar search** and one server-rendered results page grouped by teams /
players / tournaments — plain `GET /search?q=`, working with JavaScript off. Typeahead is the
enhancement and belongs in 5c, the same relationship veto has with SSE.

On [040](040-threat-model.md)'s *"search must not become a way to enumerate accounts"*:
nicknames are already public and the login identifier is the email, which search never
touches. Search enumerates nothing that is not already enumerable. The controls it needs are
the ordinary ones — rate limiting, a minimum query length so `?q=a` does not return the user
table, and the allow-list projection `/players/{id}` already uses.

> **Unverified, carried forward:** [043](043-page-contents.md):94 claims *"Russian users will
> type Latin approximations."* Diacritic folding and Cyrillic→Latin transliteration are
> different problems and the default `unaccent.rules` should not be assumed to cover the
> second. A five-minute check against the real database, not a design question.

---

## 7. The audit log exists, and starts small

[012](012-security.md):126, [036](036-authorization.md):92 and [015](015-admin-panel.md):107
all require an append-only audit log. There are eighteen `DbSet`s and none of them is one.
Meanwhile the plan describes forfeits as "audited", which today means three columns on the
match row — and [050](050-match-results-and-ratings.md) added a second privileged operation
with the same non-mechanism.

Per-row attribution cannot be stretched to cover it. [036](036-authorization.md):173 is the
proof: *"`staff` requesting a server's RCON credential → denied, and the attempt is audited."*
**A denied attempt has no row to attribute it to.**

One table: `actor / action / resource / reason / occurred_at / outcome` — the last so a
*denial* is representable — plus a `retention_class` column from the start.

**Phase 4 writes privileged decisions only:** forfeits, referee reversals, dispute
resolutions, trusted-flag changes, role grants, bans, and denied attempts.

[012](012-security.md) also asks for authentication events. Deferred with the staff panel that
would read them, for two reasons: an append-only log nobody can view is storage rather than
security, and a row per login accrues GDPR weight from day one against the IP-minimisation
work that already shipped — no IP column in PostgreSQL, an auth limiter partitioned on a
per-process HMAC that becomes unlinkable on restart. `retention_class` exists now so they can
be swept later without a migration.

**Erasure needs no new thinking.** Audit rows reference an account id, and erasure turns that
account into a tombstone. Nothing in the log is edited and nothing personal survives it —
the same trick already used for roster and rating history. Append-only and GDPR erasure stop
competing.

---

## 8. News markdown: `DisableHtml()` is not enough

`NewsPost.Body` is specified as *"sanitised markdown, never raw HTML"*
([040](040-threat-model.md) §5).

**Tested against Markdig 1.3.2, real output, 14 cases.** `DisableHtml()` escapes raw HTML as
promised — and does **not** touch URLs. `[Zapisz się](javascript:…)`, `vbscript:` links and
`data:` images all survive into `href`/`src`. That is pure markdown; the mitigation the schema
comment names does not close the vector it names.

**Decision: `DisableHtml()` plus `HtmlRenderer.LinkRewriter` with an http/https/relative
allow-list.** Markdig ships no sanitiser — 39 pipeline extensions, none security-related —
but `LinkRewriter` is invoked at the point every URL is written.

An AST pass over `LinkInline` was the alternative, and was rejected after testing showed both
approaches pass identically today but fail differently tomorrow:

- `LinkRewriter` sits at the **emission point**, so every URL goes through it whatever node
  produced it — **it fails closed**;
- an AST pass enumerates the node types known on 2026-08-11. Markdig has extensions emitting
  URLs from neither `LinkInline` nor `AutolinkInline` (`UseMediaLinks`, `UseJiraLinks`,
  `UseReferralLinks`, `UseFigures`). Add `UseAdvancedExtensions()` one day for a table and
  part of the document silently stops being covered — no exception, no failing check, just a
  live `href` that should not be there.

Constraint over vigilance, the same principle as [023](023-bracket.md)'s foreign-key routing
and [050](050-match-results-and-ratings.md)'s unique index.

Markdig stays behind a single `NewsMarkdown.Render` entry point and `Markdown.ToHtml` is
never called elsewhere — that is this approach's one weakness, since the protection lives on
a renderer instance.

> The AST pass was written first and **missed `<javascript:alert(1)>` entirely**, because
> `AutolinkInline` is a separate type. It was found by running the cases, not by reading the
> code. So was a second bug — in the *check*: asserting on `output.Contains("javascript:")`
> fails on correctly-escaped text, and a security check that cries wolf on safe input is how
> a real finding gets ignored later. **Only attribute values can execute**, so the check
> extracts `href="…"` / `src="…"` and tests those.

### The runnable check

A post containing `<script>` renders it as visible text; a post containing a `javascript:`,
`vbscript:` or `data:` link renders with the target removed; an `https:`, relative and anchor
link all survive. 14 cases, assert-based, no framework.
