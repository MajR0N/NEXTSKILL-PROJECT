# 053 — Rewards, recognition and prize pools

**Status:** decided
**Date:** 2026-08-11
**Relates to:** [021](021-site-structure.md) (seasons), [025](025-rating-system.md) (ratings),
[043](043-page-contents.md) (page contents), [049](049-team-page.md) (trophies and placements),
[050](050-match-results-and-ratings.md) (the record), [052](052-cheating-and-rating-integrity.md)
(why the record must stay trustworthy),
[MARKETING-AND-SALES-STRATEGY](../MARKETING-AND-SALES-STRATEGY.md) §9

---

## The wound this addresses

Community servers are not shrinking because they got worse. **They are shrinking because Valve
moved everything that counts somewhere else.** Premier is the ranked mode; private servers grant
no XP, move no rank, and produce no record. Every hour a player spends on a Polish community
server produces nothing they can point at.

The product's real job is therefore narrower and clearer than "tournament infrastructure":

> **Make time on a community server count again.**

Rewards are how that promise becomes visible. This document decides which kinds we use.

## Decision: rewards are earned, never drawn

**Deterministic only.** Placement, participation milestones and streaks — outcomes a player can
predict, verify and point at. **No chance mechanics, no random drops of anything with market
value.**

This is not primarily caution. **A deterministic reward and the moat are the same object:** every
one of them is another public, verifiable fact attached to a match record, which is exactly the
asset [050](050-match-results-and-ratings.md) and [052](052-cheating-and-rating-integrity.md)
exist to protect. A random drop attached to the same match adds nothing to the record and imports
a regulatory question that has to be answered forever.

### Where the legal line actually sits

Recorded so this is a decision rather than a vibe:

- **Poland:** loot boxes fall *outside* the Gambling Act — the catalogue of gambling games is
  closed and does not include them (Ministry position, 2024). But the stated reasoning is that
  *virtual items do not constitute monetary or material rewards*, and **that reasoning does not
  hold for CS skins**, which trade for real money on the Steam market. Polish counsel publicly
  warn operators that such activity may be reclassified. Sites offering loot boxes are monitored.
  **Not illegal today; unsettled, monitored, and moving.**
- **Valve is the harder constraint.** The tournament licence bars organisers from displaying
  content that violates the Steam Subscriber Agreement — *"for example, game case opening sites or
  skin trading sites"* — and enforcement against skin-gambling sponsors has been tightening.
  Prize distribution must be transparent and reportable.

**The disqualifying argument is not legality, it is category.** A platform whose entire value is
being the trustworthy record cannot afford to sit on the side of the line Valve is drawing.

## The reward stack

### Tier 1 — Status. Costs nothing, already decided.

Rating and RD shown openly ([025](025-rating-system.md)), rank, form, immutable trophies with
provenance ([049](049-team-page.md)), season placements ([021](021-site-structure.md)), and the
community standings table ([051](051-phase-4-remainder.md) §5 / strategy §3.4).

**This is the replacement for the XP Valve removed**, and it is already specified. Nothing new to
build.

> **A rating is already a variable-reward schedule.** Every match has an uncertain outcome with a
> numeric consequence, and [025](025-rating-system.md) deliberately *shows* the uncertainty
> (`1847 ±32`) instead of hiding it. The compulsive-checking effect that drops are bought for is
> a property of a ladder that means something. We do not need to purchase it twice.

### Tier 2 — Community-funded prize pools. Costs nothing; it is a feature.

The organiser puts up the prize. **NEXTSKILL runs eligibility, verifies the winner against the
match record, and publishes the result.**

Communities already give prizes — Polish networks advertise *nagrody dla aktywnych graczy* — and
what they lack is a way to run it **fairly and publicly**. "Who won what, verifiably, with the
match record attached" is the feature, not the money.

**Pools are stackable and attributed.** NEXTSKILL may put up a base pool; the hosting community
adds theirs; both are named on the tournament page. That makes sponsorship visible, gives a
network a public reason to contribute, and turns a prize into cross-promotion — which is the same
mechanic as the community standings table (strategy §3.4).

Constraints:

- The pool is **displayed, attributed and immutable once registration opens** — a prize that
  shrinks after teams enter is the fastest way to lose an organiser's audience.
- **NEXTSKILL never holds another party's money.** The organiser pays their own winner; we record
  and publish that they did. Holding funds makes us a payment intermediary, which is a different
  business with different law.
- Eligibility is computed from the record, so an unrated result
  ([050](050-match-results-and-ratings.md)) cannot silently win a prize.

### The 2000 PLN line — Polish tax, and it is a cliff

Verified 2026-08-11:

- Esports prizes won in the **EU/EEA** attract a **10% flat (zryczałtowany) withholding tax**.
- **A single prize of 2000 PLN or less is exempt** (art. 21 ust. 1 pkt 68, ustawa o PIT).
- Above 2000 PLN the 10% applies to the **whole prize, not the excess** — a cliff, not a taper.
- **The organiser is the withholding agent**: verifying value, collecting and remitting the tax
  are their legal obligations.

**Design consequence: keep any single prize at or under 2000 PLN and no tax machinery exists at
all.** Cross the line once and whoever organised the event — including NEXTSKILL for its own
first-party cups — becomes a tax agent with filing duties, which is an operational burden far
larger than the prize.

For **community-funded pools this is the organiser's obligation, not ours**, which is another
reason the "we never hold the money" rule above is load-bearing rather than fastidious. The
platform should nonetheless **warn** an organiser when a configured prize crosses 2000 PLN, since
most community admins will not know this and the liability lands on them.

### Tier 3 — Deterministic perks. Sourcing effort, low risk.

Game-key discounts, partner codes, server-hosting credit, peripheral discounts — **awarded for
placement or a stated milestone, never by chance.** No monetary transfer, no Valve IP surface.

This is a partnership conversation, not a mechanic, and it waits until there is an audience worth
a partner's while.

## What we will not do

| Rejected | Why |
| --- | --- |
| **Random skin drops for playing** | Wrong side of the line Valve is actively drawing, in a product whose value is being trustworthy. Adds nothing to the record |
| **Case-opening / crate mechanics** | Explicitly named in Valve's tournament licence restrictions |
| **Holding organisers' prize money** | Makes us a payment intermediary |
| **Paid entry with a prize pool** | Consideration + prize + competition is a materially different legal question, and it excludes exactly the tier 3–5 teams the product exists for |

## The escape hatch, if engagement ever needs one

**Non-tradeable cosmetics with rarity** — a badge, a profile flourish, a seasonal marker. Variable
reward with **no market value**, therefore no prize in any legal sense and no gambling surface.
This is what Valve's own drop system was before skins became currency.

Deferred, not rejected. Revisit only if Tier 1 demonstrably fails to hold attention, and never as
the first lever.

## Consequence for the strategy

[MARKETING-AND-SALES-STRATEGY](../MARKETING-AND-SALES-STRATEGY.md) §9 assumes the scene resents
monetised community tools. **That assumption is false for teams**: Refrag charges $5.40–11.50/mo,
CYBERSHOKE $2.00–5.50/mo, PRACC $5/mo, and teams pay. It may still hold for organisers, who are
the customer. Tier 2 is the honest first commercial surface — a feature organisers want, priced
later or never.

## The runnable check

1. **An unrated result cannot win a prize** — a forfeit, a bye or a `disputed` match is ineligible
   by the same rule that excludes it from rating.
2. **A prize pool cannot shrink after registration opens**, and every change before that is
   attributed and audited ([051](051-phase-4-remainder.md) §7).
3. **No reward path anywhere in the codebase consumes randomness.** If one ever does, this
   decision has been reversed and should be reversed on purpose.
