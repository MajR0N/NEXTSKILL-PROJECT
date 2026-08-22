# 058 — What we sell

**Status:** **decided** — all five lines, §6 promotion and §6b modularity included. **V12 (disclosure law) blocks promotion shipping, nothing else**
**Date:** 2026-08-22
**Decision:** Five revenue lines. **The platform stays free where it feeds the rating or the
verified catalogue; everything built on top of those is sellable.**
**Supersedes:** [MARKETING §9](../MARKETING-AND-SALES-STRATEGY.md) — which had one revenue line
**Overturns:** [057](057-server-browser-and-seo.md) §5 — money may now buy position *inside* the list, under §6's four rules
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
| **1** | **Promotion inside the server list**, sold per period (§6) | Server owner | [057](057-server-browser-and-seo.md) shipping, and **V12** answered |
| **2** | **Anti-cheat** — ours, or integration with the one they already run (§6b) | Organiser / server owner | [052](052-cheating-and-rating-integrity.md) tier 1; a labelled corpus |
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

## 3a. Licensing — withhold data, never permission

**The principle, because everything below follows from it:**

> **A licence check the binary evaluates is a branch, and a branch can be patched.
> Do not ship one. Ship a plugin that does not have what it needs until we send it.**

The match config — maps, sides, rosters, SteamIDs, series state — **is not in the binary and is
never bundled with it.** The plugin asks for it at match start over the channel that already
exists ([009](009-api-contract.md)), signed with the tenant secret from
[048](048-organiser-server-configuration.md). No valid licence → the endpoint returns a refusal →
there is no config → no match starts. Deleting the check from a decompiled build changes nothing,
because the thing withheld is **data the pirate does not have**, not a permission they were denied.

### The mechanism, in full

| Concern | Decision |
| --- | --- |
| **What the "licence key" is** | **Nothing new.** It is the per-server HMAC secret [048](048-organiser-server-configuration.md) already issues and the organiser already pastes into the plugin config. Licensing is a subscription row on the tenant, checked at config time. **No key format, no activation flow, no new artefact to lose or leak** |
| **Where it is enforced** | The config endpoint, server-side. Never in the plugin |
| **Refusal shape** | An explicit, typed refusal — **distinguishable from a network failure**, or a lapsed licence makes the plugin retry forever and a blip looks like non-payment ([009](009-api-contract.md)) |
| **Seat counting** | Concurrent **live matches**, not installs. That is the unit being sold and the only one that is honest to meter |
| **Sharing one secret across servers** | [048](048-organiser-server-configuration.md) binds a secret to a registered `host:port`. A secret heartbeating from somewhere it was never registered is **a flag for a human, never an automatic cut-off** — the same rule as [052](052-cheating-and-rating-integrity.md) §3. Servers move, DNS changes, and a false accusation costs more than the licence |
| **Offline / our panel is down** | **Config is fetched once, at match start. Once fetched, the match runs to completion with no further contact.** See below |

### The failure mode that must never happen

> **A live match is never killed for a licensing reason.** Not a lapsed subscription, not a seat
> overage, not our own outage.

A kill-switch that stops a final at 12–3 turns a paying customer into a public incident, and it
would contradict [033](033-maintenance-mode.md)'s *degrade, don't stop* — which already accepts
in-flight matches while blocking new ones. **Licensing gates the start of a match and nothing
else.** Overage and expiry are collected at the next config request, where the cost of being
wrong is a delayed match rather than a destroyed one.

### What we deliberately do not build

| Rejected | Why |
| --- | --- |
| **Hardware fingerprinting / IP locking** | Game servers migrate, get re-imaged and sit behind changing addresses. It generates support tickets from paying customers and stops nobody who can edit a config |
| **Obfuscators, packers, native rewrites** | §5. Obfuscation, not protection, paid for in [002](002-cs2-plugin.md) §Tier 4 fragility |
| **A phone-home heartbeat that can disable a running plugin** | The failure mode above. Also makes our uptime a dependency of their match |
| **Per-key activation servers, floating licence daemons** | An entire subsystem to protect a tier we have not sold once yet |

### The ceiling, stated honestly

`ponytail:` this is one column on the tenant and one branch in the config endpoint. **It is not
uncrackable and must not be sold internally as if it were.** The binary still contains the match
orchestration — ready-up, knife round, overtime, backup/restore — so a determined attacker can
hardcode a config and run matches without us. What withholding config actually buys is a change in
the *cost curve*: from "patch one branch" to "reimplement the config layer and permanently forgo
brackets, ratings, disputes, Discord and every future panel feature."

**So the real anti-piracy measure is line 3's own pricing.** The free connected tier is a *better
product* than a cracked standalone one, and it is free. Piracy here is not defeated by making the
plugin hard to copy; it is defeated by making the honest path the one a rational organiser wants.
Anyone still cracking it was never going to pay, and they end up with the weaker half.

**Upgrade path, if a real loss is ever measured:** signed, expiring config responses so a captured
config cannot be replayed next month. Not before — it adds key rotation to a product with no
customers yet.

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

**Anti-cheat on our own hardware is our choice to make.** ECD is free to use, so a NEXTSKILL-run
server can ship with it at no licence cost, and *"our servers run ECD"* is a factual statement about
our own infrastructure rather than a claim about anyone else's (§6b). Which vendor, and whether it
becomes a mutual promotion, is a go-to-market question deferred to the marketing strategy.

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

## 6. Boost — a verified server pays to sit higher for a while

**Decided 2026-08-22. This overturns [057](057-server-browser-and-seo.md) §5's hard line**, which
said money never buys a position inside a list. It was reopened deliberately, with the cost priced
first, and the answer is:

> **A server or community that wants to be higher in the list for a period can buy that. That is
> the entire product, and it is why anyone pays for it.** We earn; they earn players. It is priced
> above a beside-the-list slot, because it is worth more.

**What makes it defensible is rule 4: only a verified server may buy one.** The moat objection was
never an objection to *paid position* — it was an objection to paid position that cannot be told
apart from evidence. Under these four rules a boosted row is still a proven row; it has simply paid
to be seen first.

### Rule 1 — a boosted row is unmistakable, and that is the law, not a scruple

Every boosted row carries a visible, permanent label. Not a subtle tint, not a small icon: a word.

This is **not optional and not a design preference.** EU consumer law (the Omnibus Directive
2019/2161, in force in Poland since 1 January 2023) requires disclosure of paid placement that
affects ranking, and the DSA requires advertising to be identifiable as such. **A boosted row we
failed to label is a regulatory problem before it is a trust problem.** See [V12](#verification).

The commercial consequence is the useful one: a label is exactly what stops boost from eating the
evidence, and we are required to ship it anyway.

#### The label, decided 2026-08-22

> **`PROMOWANE · ZWERYFIKOWANE`** — a bordered band above the organic list, with its own badge.

| Element | Decision |
| --- | --- |
| **Wording** | **`Promowane`** — standard Polish e-commerce usage (Allegro), so it reads as familiar rather than invented, and it **states the commercial nature**, which is the actual legal test |
| **`· Zweryfikowane`** | The second half says the thing **no competitor can say**: everything in this band is plugin-verified (rule 4). The disclosure and the differentiator are the same string |
| **Treatment** | A **bordered band**, visually separated from the organic rows, plus a distinct badge per row. Borders serve the *prominence* half of the requirement, not only aesthetics |
| **Rendering** | Server-side, present with JavaScript off. **No code path may emit a boosted row without it** — runnable check 7 |
| **Language** | Polish on the Polish surface. Any other locale needs the same test applied to *that* jurisdiction's wording, not a translation of ours |

**`MasterBoost` was proposed and rejected.** It is **GO-Setti's product name**
([MARKETING §2](../MARKETING-AND-SALES-STRATEGY.md)), so adopting it carries trademark and
unfair-competition exposure (*ustawa o zwalczaniu nieuczciwej konkurencji*) — and it is
strategically backwards, handing away the differentiator inside the label. It also **fails the
legal test on its own**: "boost" never says *paid*, and a visitor who does not already know the
term learns nothing from it.

> **The internal product name is TBD.** Whatever it becomes, it is a *name*, never the disclosure.
> `Promowane` is the disclosure and it appears regardless. Two constraints on the eventual name:
> **not a competitor's brand**, and **not a word that implies the ranking was earned.**

### Rule 2 — promotion moves a row, it never touches a number

**This is the moat, stated precisely.** *"Liczby z serwera, nie z zapytania"*
([057](057-server-browser-and-seo.md)) is a claim about **counts**, not about ordering. A promoted
server shows its own real, verified human count, in its own real confidence tier, exactly as it
would unpromoted.

> **What is for sale is position. What is never for sale is truth.**

A visitor reading a promoted row still learns whether the server is full and whether the number can
be trusted. Nothing they are shown becomes false. That is the difference between us and
MasterBoost, and it survives this decision intact.

### Rule 3 — the votes ranking stays unbuyable

Promotion applies to **the list** `/serwery` — *"where do I play now?"*. It does **not** apply to
**the ranking** `/ranking/serwery/{month}` — *"which server was best this month?"*
([057](057-server-browser-and-seo.md) §5).

The ranking is earned from votes by players who were actually there. Selling into it would make it
meaningless, and unlike the list it has no browsing job to do that a paid row could help with. **Two
surfaces was already the design; this decision is why that split was worth having.**

### Rule 4 — only a **verified** server may be boosted, and this is the whole design

**Decided 2026-08-22, and it is the rule that resolves the moat objection rather than merely
bounding it.**

> **A boost can only be sold to a server that is already `Verified`** — running the activity
> plugin, with real human counts we can prove ([057](057-server-browser-and-seo.md) §2).
> **Money never buys the badge. The badge is the ticket to buy anything.**

A boosted server therefore **does not vanish from the verified list — it *is* the verified list**,
raised. The promoted row and the honest row are the same row. What the visitor sees at the top is
not an exception to the evidence; it is a member of the evidence set, paid to be seen first.

**This is why the objection [057](057-server-browser-and-seo.md) §5 raised no longer applies.** That
objection was: *if budget moves rows, a visitor cannot tell which part of the order is evidence.*
Under this rule every row a visitor can see — boosted or not — carries a proven count. Order is
influenced by money; **truth is not, and the set of things eligible to be shown is not.**

### The flywheel this creates, which is the real reason to prefer it

| | |
| --- | --- |
| **Want to be boosted?** | Install the activity plugin and get verified first |
| **What that costs the owner** | Nothing. The plugin is free (§2) |
| **What it gets us** | Another proprietary row, whether or not he ever buys |

**The paid product is now the recruiting instrument for the free one.** Every owner who wants
visibility must first hand us the data nobody else can get. §2 said the activity plugin is free
because charging would shrink the catalogue; this rule adds the positive half — **boost gives
owners a commercial reason to install it**, which is a far stronger adoption argument than *"your
numbers will look honest."*

It also closes the gap [057](057-server-browser-and-seo.md) §5a worried about: the CS 1.6 catalogue
starts near zero, and this is the mechanism that fills it.

### Three routes to eligibility — **TBD, and deliberately so**

Verification is the gate. How an eligible owner then buys is open, and the three candidates are
**not mutually exclusive**:

| Route | Shape | Open question |
| --- | --- | --- |
| **1 — Conditions + payment** | Meets a published bar (uptime, minimum real population, no padding flag, age), then pays per period | **What the conditions are.** Set them too high and there is nothing to sell; too low and the top of the list stops being worth looking at |
| **2 — Subscription + eligibility** | Recurring, and the boost applies whenever the server currently qualifies. Lapses automatically if it stops qualifying | Whether a lapse refunds, pauses or simply stops. **Pausing is kindest and easiest to explain** |
| **3 — Partnership** | Negotiated. A network that brings teams, hosts events or supplies servers is boosted as part of a wider arrangement | Whether partnership boosts are labelled *differently* from paid ones. **They must still be labelled** (rule 1) |

**Not decided here.** Prices, the condition bar, and whether all three ship or only one need a
market with traffic in it. What *is* decided is the gate above them: **no verification, no boost,
by any of the three routes — partnership included.** A partner who will not install the plugin
cannot be boosted, because that is the one exception that would reintroduce exactly the problem
this rule solves.

### And the integrity rules still bind

- A server flagged for padded counts (**V11**) cannot buy or hold a boost; raising the flag ends an
  active one, with the remainder refunded.
- Boost never alters a count, a human/bot split, or a confidence tier (rule 2).
- Losing verification — the plugin is removed, or heartbeats stop — **ends the boost**, because the
  ticket to buy it has expired.

### The inventory

| | |
| --- | --- |
| **Sold as** | `server × game × period` — a boost window, by the week or month |
| **Eligibility** | **`Verified` only**, via one of the three routes above |
| **Priced** | **Above** the labelled beside-the-list slot (§1 line 5). It is the more valuable placement and should cost like it |
| **Capped** | A hard maximum of boosted rows per page and per facet. Uncapped promotion turns the list into a directory of whoever paid — the product we are competing against |
| **Ordering within the boosted band** | By purchase, never by bid. **Auctions stay rejected**: they convert the cap into an escalating price for the same thing and reinstate *"the richest server is always first"* |
| **Deferred** | Prices, the cap number, the condition bar, prepaid credit, per-slot versus per-period |

### What it costs, recorded honestly

[057](057-server-browser-and-seo.md) §5's argument was that a visitor must be able to read the
*entire* ordering as evidence. After this decision they read the **unboosted** ordering as
evidence and must take the boosted band as advertising. That is a real reduction and it is the
price of the revenue line.

**Rule 4 makes it much smaller than it first looks.** Because only verified servers are eligible,
the boosted band is a *reordering of the evidence set*, not an intrusion into it — every row up
there still carries a count we can prove. A visitor misled about *order* is still not misled about
*fact*, and fact is what [057](057-server-browser-and-seo.md) built the differentiator on.

**What must never be weakened, or this stops being defensible:** rule 1 (the label), rule 2 (no
number ever moves) and rule 4 (verified only). Each is one line to erode and none of them can be
restored after the list has lost its reputation.


<a name="verification"></a>
### Verification

| # | Question | Blocks |
| --- | --- | --- |
| **V12** | What exactly must a paid placement disclose under the Polish implementation of the Omnibus Directive and under the DSA — wording, prominence, and whether the ranking criteria themselves must be published? | **Promotion cannot ship until answered.** Single-sourced here from general knowledge; **confirm with the current statute or counsel before it reaches code** |

## 6b. The offer is modular — the owner picks what he wants

**Decided 2026-08-22.** "Options to choose from" (§1 line 1) does not mean presentation tiers on a
listing. It means **the owner assembles his own package**: promotion, yes or no. Anti-cheat, yes or
no. Activity reporting, yes — it is free and it is how he gets the badge. Tournament hosting,
match plugins, a managed server, later.

| | |
| --- | --- |
| **Shape** | A catalogue of independent components, not a bronze/silver/gold ladder |
| **Why** | A 1.6 owner who wants only anti-cheat should not have to buy promotion to get it, and a CS2 owner who wants only promotion should not be sold a plugin he will never install. **The components have genuinely different buyers** |
| **Free tier, always present** | The activity plugin and the verified badge (§2). Every package includes them because every package should grow the catalogue |
| **Priced** | Per component, per period. Bundling is a later optimisation and needs prices we do not have yet |

`ponytail:` this is a subscription row per `tenant × component`, not a plan system. Ceiling: no
proration, no mid-period upgrades — the upgrade path is a real billing library, and only if
someone actually asks.

### Anti-cheat: ours, theirs, or none — the owner chooses

This is the component where the choice matters most, and where the honest answer is **not** "ours
or nothing."

CS 1.6 servers already run client-side anti-cheats — **fungun / EasyCheatDetector**, **Wargods**
and others are long-established in exactly the scene we are entering. Requiring those servers to
drop what they run in favour of an unproven NEXTSKILL client would be asking them to take a risk
for our benefit.

**Decided: we integrate with third-party anti-cheats as well as offering our own.**

| Option | What we do |
| --- | --- |
| **NEXTSKILL AC** (§1 line 2, [052](052-cheating-and-rating-integrity.md) tier 1) | Ours end to end. Detection heuristics stay server-side (§5) |
| **Third-party client AC** — fungun/ECD, Wargods, others | **Integrate**: record that it was active, which one, and which version; ingest its verdicts where it exposes them |
| **None** | Fully supported. Tier 0 is [052](052-cheating-and-rating-integrity.md)'s default and stays the default |

**Why this is worth building:** it converts an installed base into evidence instead of competing
with it. An owner keeps the anti-cheat his players already trust, and we still get a fact worth
recording on every match. Adoption costs him nothing.

### The rule that makes third-party integration safe

> **A third-party verdict is provenance-tagged evidence, never truth, and never a gate.**

This is [052](052-cheating-and-rating-integrity.md) §"The AC state belongs on the record, not in a
gate", applied to software we did not write:

- The game server is **untrusted infrastructure** ([007](007-server-hosting-model.md)) and a
  client-side AC's output reaches us through it. A verdict is therefore a claim by a third party
  relayed by an unverified host — the same shape as
  [050](050-match-results-and-ratings.md)'s participant `Provenance`.
- **It never bans, and never settles a rating by itself.** It is something a referee weighs.
- **It never buys a confidence tier.** Running Wargods does not make a server `Verified`; only the
  activity plugin does that (§6 rule 4).
- **We record which AC and which version**, because several of these are old, closed and
  publicly bypassed. *"An AC was running"* is not a security claim, and the product must never
  present it as one.
- We ship **no bundled third-party binary** and make no representation about anyone else's
  detection quality. We record what ran; they own what it does.

`ponytail:` integration is a nullable `(vendor, version, verdict)` on the match participant record
plus whatever ingest each vendor already offers. Ceiling: no live enforcement, no vendor whose
output cannot be read without reverse-engineering it. Upgrade path is a real vendor agreement, and
only for one that a paying organiser actually asks for.

### How a third-party anti-cheat is named on the page

*"Powered by ECD / fungun"* was proposed and **rejected**, for three reasons that stack:

| | |
| --- | --- |
| **It claims a relationship we do not have** | *"Powered by"* is the standard phrasing for a licensed integration or an endorsement. We have neither, and implying one is a misrepresentation before it is a trademark question |
| **It points the wrong way** | Nothing of ours is powered by their anti-cheat. **A server owner chose to run it.** The fact belongs to the server, not to NEXTSKILL |
| **It reads as a security guarantee** | Which is exactly what [052](052-cheating-and-rating-integrity.md) forbids. Several of these tools are old, closed and publicly bypassed; *"an AC was running"* is an observation, not protection |

**Decided: neutral factual attribution, worded as an observation, attached to the server or the
match — never to us.**

| Surface | Wording |
| --- | --- |
| Server page / match record | **`Anty-cheat: Wargods (zgłoszony)`** — names the vendor and the version, and **`zgłoszony`** carries the provenance: it was *reported* to us through infrastructure [007](007-server-hosting-model.md) treats as untrusted |
| Our own marketing | **`Współpracuje z ECD, fungun i Wargods`** — a compatibility claim about *our* software, which is ours to make. **Never their logos, never "official", never "partner"** unless one actually signs something |

**The distinction that keeps this safe:** *"our product works with theirs"* is a factual statement
about what we built. *"Powered by theirs"* is a statement about them. **We only ever make the
first.**

`ponytail:` this is a label and a nullable vendor string, not an integration programme. Ceiling: it
says what ran, never how well. Upgrade path is a signed agreement with a vendor, and only if one
ever wants their name used more strongly than this.
### Two cases where naming a vendor *is* legitimate

The rule above forbids claiming someone else's relationship or endorsement. It does **not** forbid
stating a true one. Two cases where the claim is ours to make:

| Case | Why it is fine | Wording |
| --- | --- | --- |
| **We run it on our own managed servers** (line 4) | On hardware we own, running ECD is a fact about **our** infrastructure, not a claim about theirs. *"Our servers run ECD"* is simply true | **`Nasze serwery: ECD`** — and here *powered by* is defensible, because the thing being powered is genuinely ours |
| **An actual signed partnership** | The objection was *"claims a relationship we do not have."* **Signing one removes the objection.** Mutual promotion is an ordinary commercial arrangement | Whatever the agreement permits. Logos and *"official"* become available **only then** |

**ECD is free to use**, which makes both cases cheap: line 4 carries no anti-cheat licence cost,
and a partnership can trade exposure rather than money — attractive to a free project that gains
distribution, and to us for the credibility of shipping something CS 1.6 owners already trust.

> **Deferred to the marketing strategy, deliberately.** Whether to approach ECD or Wargods for a
> partnership, what each side gives, and whether NEXTSKILL-run servers standardise on one vendor
> are **go-to-market questions, not decisions this document should settle.** What 058 fixes is only
> the constraint: **until something is signed, we describe, we do not endorse** — and even after,
> §6b's record rules hold, because a partnership changes what we may *say*, never what a verdict
> *means*.


## 7. Sequencing

Forced by dependency, not preference. **Rule 4 changed this order**, and the change is worth
seeing rather than discovering:

| Order | Line | Gate |
| --- | --- | --- |
| **1st** | **Line 5 — sponsored slots beside the list** | Only [057](057-server-browser-and-seo.md) and traffic. **The one revenue line needing no plugin at all**, so it ships first and measures **V10** for free |
| **2nd** | **The activity plugin** *(free, not a revenue line)* | Phase 6. **It is now on the critical path to revenue**, because nothing can be boosted until something can be verified |
| **3rd** | **Line 1 — boost** | Verified servers existing, **and V12 answered** |
| **4th** | **Line 3 — standalone licence** | The match plugins, Phase 6 |
| **5th** | **Line 2 — anti-cheat** | [052](052-cheating-and-rating-integrity.md)'s corpus, which needs matches, which needs line 3. **Research runs in parallel from now; shipping does not.** Third-party integration (§6b) can land earlier — it is a record, not a detector |
| **6th** | **Line 4 — managed servers** | Money. Last, because it is the only line with a downside |

> **The cost of rule 4, stated plainly:** gating boost on verification **delays boost revenue until
> the activity plugin ships.** Before this decision, line 1 was going to be the first thing sold.
> Now the first thing sold is the beside-the-list slot, and boost waits.
>
> Accepted, because the alternative is selling position on unverified servers — which is
> [057](057-server-browser-and-seo.md) §5's objection with the money taken anyway, and it would make
> us the thing we are competing against. **The delay buys the only version of this product that is
> defensible.**

## Consequences

- **[MARKETING §9](../MARKETING-AND-SALES-STRATEGY.md) is rewritten** to carry five lines.
- **[057](057-server-browser-and-seo.md) §5's hard line is overturned** — money buys position
  inside the list, bounded by §6's four rules. §5's *two surfaces* split becomes load-bearing
  rather than tidy: it is what keeps the votes ranking unbuyable while the list is sold.
- **[057](057-server-browser-and-seo.md)'s verified tier gains a commercial engine.** §6 rule 4 makes
  the free activity plugin the ticket to the paid boost, so the paid product now *recruits* for the
  free instrument. It also puts that plugin **on the critical path to revenue** (§7), which it was
  not before.
- **[007](007-server-hosting-model.md) is no longer "NEXTSKILL hosts no game servers."** BYO stays
  the default and the architecture; exclusivity is dropped.
- **[042](042-hosting.md)'s ~€5/month is line 4's precondition, not its outcome.**
- **[052](052-cheating-and-rating-integrity.md) gains a revenue line and a second AC path.**
  A *sold* anti-cheat makes a promise a free one does not — 052 already requires saying what it
  does not stop, and that now carries commercial weight. **CS 1.6's non-Steam population is a
  materially harder target than CS2's**, so the three games are priced and promised separately.
  §6b additionally admits **third-party client anti-cheats (fungun/ECD, Wargods) as recorded
  evidence**, which extends 052's *"AC state belongs on the record, not in a gate"* to software we
  did not write.
- **Phase 6 grows**: the licensing seam (§3a), anti-cheat research, and the third-party AC record.
- **[009](009-api-contract.md) gains a typed refusal** on the config endpoint, and
  [033](033-maintenance-mode.md)'s *degrade, don't stop* now also governs licensing:
  **a live match is never killed for a commercial reason**.
- **A new legal dependency, V12.** Paid placement disclosure is regulated. This is the first
  decision in the project whose shipping is gated on a statute rather than on an engineering
  answer.

## The runnable checks

**Licensing** (§3a). These decide whether the paid tier is real:

1. **An unlicensed standalone tenant gets no config** — the config endpoint returns a refusal, not
   a partial config. A partial config is worse than none: it lets a cracked build fill the gaps.
2. **The refusal is distinguishable from a network failure** — a lapsed licence must not make the
   plugin retry forever, and an outage must not look like non-payment
   ([009](009-api-contract.md)).
3. **A connected tenant is never charged** — a match created on nextskill.pl pulls config with no
   licence row present.
4. **An expiring licence does not kill a running match** — a match whose config was issued while
   valid runs to completion after the licence lapses mid-match; the *next* request is refused.
   This is the check that protects a customer's final, and the one most likely to be broken later
   by tidying the licence path.
5. **Seat overage refuses the new match, never an existing one** — at the limit, an in-flight match
   is untouched and the next `match_loaded` is refused.
6. **A secret used from an unregistered `host:port` raises a flag and blocks nothing** — no
   automatic cut-off, and the flag carries actor and reason into the audit log
   ([036](036-authorization.md), [052](052-cheating-and-rating-integrity.md) §3).

**Promotion** (§6). These are the moat, expressed as assertions:

7. **A promoted row is labelled, always** — the label renders with JavaScript off, is present in
   the server-rendered HTML, and no code path can produce a promoted row without it
   (§6 rule 1, and V12).
8. **Promotion changes no number** — player count, human/bot split and confidence tier for a
   promoted server are byte-identical to the same server unpromoted (§6 rule 2). **This is the
   check that keeps *"liczby z serwera, nie z zapytania"* true.**
9. **Promotion never reaches the ranking** — `/ranking/serwery/{month}` is byte-identical with and
   without active promotions over the same dataset (§6 rule 3).
10. **An unverified server cannot buy or hold a boost** — purchase is refused for any server that
    is not `Verified`, **by all three eligibility routes including partnership** (§6 rule 4), and
    a server that loses verification has its active boost ended.
11. **A padding-flagged server cannot hold a boost** — purchase is refused while flagged, and
    raising the flag ends an active boost (**V11**).
12. **The cap holds** — with more purchased promotions than slots, the rendered page never exceeds
    the maximum promoted rows per page or facet.

**Modularity** (§6b):

13. **Components are independent** — a tenant holding only anti-cheat gets anti-cheat and no
    boost; a tenant holding only boost is never asked to install the **match** plugin. The
    *activity* plugin is required for boost by §6 rule 4, and it is free, so this is a gate on
    verification rather than on purchase.
14. **A third-party AC verdict never changes an outcome by itself** — ingesting a
    `(vendor, version, verdict)` record alters no rating and issues no ban, and the match record
    shows which anti-cheat produced it ([052](052-cheating-and-rating-integrity.md) §3,
    [050](050-match-results-and-ratings.md)).
15. **No boosted row renders without the disclosure word** — `Promowane` is present in the
    server-rendered HTML for every boosted row, with JavaScript off, in every locale (§6 rule 1).
16. **No page claims an *unearned* third-party endorsement** — outside our own managed servers and any signed partnership, *"powered by"* appears nowhere near a
    vendor name, and a recorded anti-cheat renders as an observation (`zgłoszony`), never as a
    guarantee (§6b).
