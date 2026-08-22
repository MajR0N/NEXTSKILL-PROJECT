# 058 — What we sell

**Status:** decided (the five lines), **two parts open** — see §6
**Date:** 2026-08-22
**Decision:** Five revenue lines. **The platform stays free where it feeds the rating or the
verified catalogue; everything built on top of those is sellable.**
**Supersedes:** [MARKETING §9](../MARKETING-AND-SALES-STRATEGY.md) — which had one revenue line
**Reopens:** [007](007-server-hosting-model.md) and [042](042-hosting.md) — we may own game servers

---

## Why this document exists

§9 recorded exactly one revenue line — a labelled slot beside the server list — and recorded
tournaments, brackets and ratings as **free**. That is now too narrow in one direction and, in one
place, contradicted outright. This replaces it rather than patching it, because a monetisation
model that has to be assembled from three amendments is one nobody will follow.

It also settles the question that opened the session: **plugin decompilation is not the threat
model.** See §5.

## 1. The five lines

| # | Line | Paid by | Depends on |
| --- | --- | --- | --- |
| **1** | **Server-list placement**, sold as options/tiers | Server owner | [057](057-server-browser-and-seo.md) shipping and getting traffic |
| **2** | **NEXTSKILL Anti-Cheat** — CS 1.6, CS:GO, CS2 | Organiser, per tournament | [052](052-cheating-and-rating-integrity.md) tier 1; a labelled corpus |
| **3** | **Tournament / bracket plugins**, standalone licence | Organiser | Phase 6 |
| **4** | **Managed game servers on our VPS** — CS 1.6 first | Organiser / community | Money, and demand that already exists |
| **5** | **Sponsored slots** beside the list | Advertiser / server owner | Traffic |

## 2. What stays free, and why it has to

This is the load-bearing half. Each of these is free because **charging for it destroys the asset
the paid lines are priced against.**

| Free | Why charging breaks it |
| --- | --- |
| **The activity-reporting plugin** ([057](057-server-browser-and-seo.md) §2) | It is the instrument that produces verified human counts. A paid plugin means a small catalogue, and a small catalogue means **data tier 5 — the same public numbers GO-Setti already has.** The proprietary tier only exists at volume |
| **Ratings, brackets, LFT, tournaments on nextskill.pl** | The rating only works at scale. Charging for the moat is charging for the thing that has no value until everyone is inside it |
| **The verified badge itself** | A badge you can buy is not evidence. [057](057-server-browser-and-seo.md) §5 already forbids it: verification is *a badge and a filter, never a position boost* |

> **The rule, in one line:** give away what makes the catalogue and the rating bigger; sell
> everything that becomes possible *because* they are big.

Line 1 is the clean statement of it: **the owner pays for placement, and we hand them the activity
plugin for nothing.** They get real numbers on their page; we get another proprietary row.

## 3. Line 3 — free when it feeds the rating, paid when it does not

The seam that resolves "brackets are free" against "we sell tournament plugins":

| Mode | Price | Rationale |
| --- | --- | --- |
| **Connected** — match created on nextskill.pl, result reported to us, rating settles | **Free** | It feeds the moat. This is the path we want most organisers on |
| **Standalone** — organiser runs matches on their own server, no NEXTSKILL account, no rating | **Paid licence** | It consumes the engineering and returns nothing to the corpus |

**The licence check lives in the panel, never in the binary.** The plugin heartbeats with its
tenant secret ([007](007-server-hosting-model.md),
[048](048-organiser-server-configuration.md)); no valid licence → the config endpoint returns
nothing → no match. That is the heartbeat which already exists and already passes checks, plus a
subscription lookup.

`ponytail:` licensing is one column on the tenant and one branch in the config endpoint. Ceiling:
an offline-capable standalone build would need real key signing and revocation. Upgrade path is
only worth walking if someone actually asks to run air-gapped.

## 4. Line 4 — owning servers, and what it costs to reopen 042

[007](007-server-hosting-model.md) decided BYO, and [042](042-hosting.md) priced the entire
platform at ~€5/month *because* of it. **This line ends that**, and the cost belongs here rather
than being discovered later:

| Changes | From | To |
| --- | --- | --- |
| Marginal cost per organiser | **Zero** | A real monthly line per game server |
| *"We can stay free far longer than a platform hosting its own infrastructure"* (§9) | True | **No longer true** |
| Support | *"my server won't start a match"* | *"**your** server won't start a match"* |

**Accepted anyway, for one specific reason that is not capacity: CS 1.6 hitboxes.**

Vanilla HLDS hitbox and lag-compensation behaviour is the scene's oldest complaint. **ReHLDS +
ReGameDLL_CS fix it**, and running the server ourselves is the only way to *guarantee* that the
fix, our plugin set and our anti-cheat are all present at once. That is a product — *"play where
the hitboxes are right"* — not rented capacity. Rented capacity was rejected in §9 and stays
rejected; this is a different offer wearing the same hardware.

**Second upside, unpriced:** [007](007-server-hosting-model.md)'s trust boundary flips back. On our
own hardware the game server stops being untrusted third-party infrastructure, which strengthens
[035](035-match-result-integrity.md) and [052](052-cheating-and-rating-integrity.md) for every
match played there.

**Sequenced last** (§7). It is the only line that can lose money.

## 5. Decompilation is not the threat model

The session opened on AMXX/SMX/.NET plugins being trivially decompiled. Recorded so it is not
re-litigated:

- **All three are exposed**, worst first: CounterStrikeSharp .NET (ILSpy returns near-original C#
  with names intact), SourcePawn `.smx`, AMXX bytecode. Native Metamod C++ raises the bar to a
  weekend in Ghidra — **obfuscation, not protection** — bought by escalating precisely the
  fragility [002](002-cs2-plugin.md) §Tier 4 names as the project's largest operational risk.
- **We stay on AMXX / SourceMod / CounterStrikeSharp**, dropping to native Metamod only where a
  specific capability requires it.
- **A perfectly decompiled plugin is worth nothing**, because every paid line above is enforced on
  our server: placement (§1), the licence check (§3), the rating pool, the corpus. Nothing
  sellable ships inside the binary.

**The one exception is line 2.** [052](052-cheating-and-rating-integrity.md) already states the
principle: *an open anti-cheat publishes its detections to the cheaters.* Detection logic is the
single asset here whose value **is** its secrecy — so its heuristics belong server-side, on demos
and reported events, with the client shipping collection rather than judgement. That keeps the
valuable half unreadable without a line of obfuscation.

## 6. The two parts still open

### 6a. A MasterBoost successor would break [057](057-server-browser-and-seo.md)'s hard line

[057](057-server-browser-and-seo.md) §5 and §9 both state it: **money buys placement above or
beside a list, never a position inside one.** GO-Setti's MasterBoost sells position *inside* the
ranking. Building its successor as described is the one item here that contradicts a decided
document, and the cost is specific: if budget moves rows, a visitor cannot tell which part of the
ordering is evidence — and **verified counts, the only asset a competitor cannot copy, stop
meaning anything.** Data tier 1 collapses to tier 5 ([057](057-server-browser-and-seo.md) §5a).

**Recommendation — sell what GO-Setti structurally cannot.** They sell rows because rows are all
they have; their counts come from dialling a server and believing the answer. We can sell
*credibility* into the same wallet: labelled, scarce placement **beside** a list nobody can buy
their way into. Same market, same buyer, opposite direction.

**Open:** whether to build a position-selling product anyway. That is a business call, not an
engineering one, and it should be made deliberately rather than by drift.

### 6b. Line 1's tiers versus 057's scarcity argument

[057](057-server-browser-and-seo.md) §5 chose **one or two labelled slots per `game × period`** over
an enhanced-listing upsell, specifically because *inventory is naturally scarce, and scarcity is
what lets the price rise later without changing the product.* "Options to choose from" is the
tiered upsell that argument rejected.

Both can coexist: scarce slots beside the list, plus presentation tiers on a server's own page —
logo, banner, description, gallery. **What must never become a tier is anything that moves a row.**

**Deferred, as before:** prices, prepaid credit, per-slot versus per-period. Those need traffic to
price against; setting them now would be guessing.

## 7. Sequencing

Forced by dependency, not preference:

1. **Lines 1 + 5** — the listing is already designed ([057](057-server-browser-and-seo.md)) and is
   the only line needing no new plugin. It also measures **V10** for free.
2. **Line 3** — needs Phase 6 to exist at all.
3. **Line 2** — needs [052](052-cheating-and-rating-integrity.md)'s corpus, which needs matches,
   which needs line 3. **Research runs in parallel from now; shipping does not.**
4. **Line 4** — last. The only line with a downside.

## Consequences

- **[MARKETING §9](../MARKETING-AND-SALES-STRATEGY.md) is rewritten** to carry five lines.
- **[007](007-server-hosting-model.md) is no longer "NEXTSKILL hosts no game servers."** BYO stays
  the default and the architecture; exclusivity is dropped.
- **[042](042-hosting.md)'s ~€5/month is line 4's precondition, not its outcome.**
- **[052](052-cheating-and-rating-integrity.md) gains a revenue line, and an obligation with it.**
  A *sold* anti-cheat makes a promise a free one does not. 052 already requires saying what it
  does not stop; that requirement now carries commercial weight — and **CS 1.6's non-Steam client
  population is a materially harder target than CS2's**, so the three games must be priced and
  promised separately rather than as one product.
- **Phase 6 grows**: the licence check, and anti-cheat research alongside the plugins.

## The runnable checks

1. **An unlicensed standalone tenant gets no config** — the plugin config endpoint returns a
   refusal, not a partial config, and the refusal is distinguishable from a network failure so the
   plugin does not retry forever ([009](009-api-contract.md)).
2. **A connected tenant is never charged** — a match created on nextskill.pl pulls config with no
   licence row present.
3. **Paid placement changes no row order** — the list's ordering is byte-identical with and without
   an active sponsorship over the same dataset ([057](057-server-browser-and-seo.md) §5).
