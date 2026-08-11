# NEXTSKILL — product marketing context

**This file is a pointer, not a source of truth.** The authoritative documents are:

- [`../CONTEXT.md`](../CONTEXT.md) — what the product is, verified external facts, engineering principles
- [`../docs/MARKETING-AND-SALES-STRATEGY.md`](../docs/MARKETING-AND-SALES-STRATEGY.md) — audiences, positioning, growth loop, launch sequence, monetisation
- [`../docs/BUILD-PLAN.md`](../docs/BUILD-PLAN.md) — what actually exists today vs. what is still a plan
- [`../docs/decisions/`](../docs/decisions/) — 51 decision records, each naming what was given up

Read those. This summary exists only so marketing skills have a starting point.

---

## Product

**NEXTSKILL** — tournament infrastructure for Counter-Strike communities that run their own
servers. CS2 and **CS 1.6**, both first-class. CS:GO explicitly out of scope.

The organiser brings the game server; NEXTSKILL brings brackets, map veto, result ingestion
from a plugin, ratings and demos. **We host zero game servers.**

**Status: pre-launch.** No public users. Phases 0–3 complete, Phase 4 (web platform) about
half built. Accounts, teams, tournaments, brackets and ranking work; matches, home page, veto,
notifications, LFT and both admin panels are not built. Every page is currently hardcoded
Polish and ships monochrome on purpose.

## Audiences

| | Organisers | Players |
| --- | --- | --- |
| Who | Community server owners, clan leaders, admins | CS 1.6 and CS2 players in PL / TR / BR / CIS |
| Problem | Running a tournament is a spreadsheet, a Discord channel, manual scoring and arguments | Nowhere to play competitively without a full team; no record of whether you are any good |
| Decides adoption? | **Yes** | No — they follow the tournaments |

**Organisers are the customer. Players are the reason the product exists.** One converted
organiser brings dozens of players; one converted player brings nobody.

> ⚠️ The organiser-pain claim above is **asserted in the strategy document and has never been
> sourced.** Treat it as a hypothesis until `customer-research` validates it.

## Positioning

**Every alternative forces a community to choose between owning the event and having a rating
anyone recognises. Nobody offers both.** That is the gap, and it is structural — FACEIT and
CYBERSHOKE cannot close it without dismantling their own supply chain.

| Alternative | What it forces |
| --- | --- |
| FACEIT / ESEA | Join *their* circuit on *their* servers; open qualifiers are effectively dead |
| CYBERSHOKE | Their servers, their community, their brand — 600k players/month that belong to them |
| FastCup | Their cloud, their matchmaking, CIS-centred. **Serves CS 1.6 too** |
| Starter League / arrmy.gg | Your *team* joins *their* league; the community isn't in the picture |
| G5API / G5V | Keep your servers and brand — and a rating that means nothing outside your own box |
| Challonge / Toornament | Brackets, no game integration, scores typed by hand |
| Discord + a spreadsheet | The real status quo. Free, familiar, terrible |

**One-line pitch (pl):** Twój turniej, na Twoim serwerze, z rankingiem, który uznają inni.
**Six-word trade:** Twój turniej, Twoja marka, wspólny ranking.

## What is strategically load-bearing

1. **The architecture is the positioning.** Tournaments are tenant-scoped, ratings are global.
   That is what lets a community own its event *and* have a recognised rating — provable in
   the data model to a sceptical admin.
2. **A layer, not a destination.** The community owns the player; NEXTSKILL owns only the
   record. No NEXTSKILL community, no funnel to our own Discord — you cannot run a destination
   on borrowed servers.
3. **Rivalry is the product.** Community-vs-community fixtures, plus one derived standings
   table that promotes every network at once. A rival's entry becomes desirable rather than
   tolerated.
4. **Volume is a product requirement.** Glicko-2 keeps teams provisional until RD settles, so
   sporadic cups mean nobody is ever ranked and the tier 2–5 acknowledgment promise silently
   fails. Hence a continuous first-party ladder alongside community cups.
5. **CS 1.6 is a narrower moat than first claimed** — FastCup already serves 1.6 matchmaking in
   CIS. What is unserved is *community-hosted* 1.6 competition. **V3** unverified.
6. **Nothing is charged for.** Monetisation deliberately undecided; ~€5/month running costs.
7. **Partner #2 is not a scaling decision** — it is when the value proposition becomes true at
   all. Recruit teams from 3–5 Polish networks for the first cup.

## Not on the risk register, and should be

**Cheating.** 040 covers SSRF, IDOR and zip bombs, not cheating — while the rating it protects
is the entire moat.

## Constraints that disqualify most marketing tactics

- No advertising budget. No sales team. No traffic to A/B test. No mobile app.
- Nothing to charge for, so no pricing, offer, paywall or churn work applies yet.
- The audience lives in **Discord servers, Steam groups and 1.6 forums** — not on LinkedIn.

## Known open question

The strategy targets **PL / TR / BR / CIS**, but the product ships locales **pl / en / ru / de**.
German is not in the target market; Turkish and Brazilian Portuguese are not in the product.
Flagged in `docs/decisions/051-phase-4-remainder.md` §3, unresolved.
