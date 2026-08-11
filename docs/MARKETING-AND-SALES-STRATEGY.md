# NEXTSKILL — Marketing & Solution Sale Strategy

**Status:** living document
**Date:** 2026-08-10 · **revised 2026-08-12** after research, strategy grilling and the LFT boundary
**Scope:** **Poland first.** The product is global-capable by construction; the go-to-market is not.

How NEXTSKILL reaches people, why they stay, and what we sell. Grounded in decisions already
made — most of the strategy is a consequence of the architecture rather than something bolted
on top of it.

> ### What the 2026-08-11 revision changed, and why
>
> The first version was written from assumption. A watering-hole research pass contradicted it
> in three places, and a grilling session resolved five open strategic questions.
>
> | Was | Is now | Why |
> | --- | --- | --- |
> | *"Nobody else serves CS 1.6"* | **False.** [FastCup](https://cs.fastcup.net/?hl=en) runs free CS 1.6 **and** CS2 matchmaking, leagues, daily tournaments, >$15 000 prizes, its own anti-cheat and its own server cloud in Moscow and Kyiv | CIS is already served. The moat is narrower and differently shaped than claimed |
> | Competitors were Challonge / Toornament / FACEIT | Add **[G5API/G5V](https://github.com/PhlexPlexico/G5API)** (free, self-hosted, game-integrated), **[Starter League](https://starterleague.pl/)** (free Polish CS2 amateur league, new season 8 Aug 2026), **[arrmy.gg](https://arrmy.gg/pl/home)**, **[CYBERSHOKE](https://cybershoke.net/cs2)** (600k players/month) | The CS2 half competes with free and with two live Polish products |
> | Organiser pain = "running a tournament is a spreadsheet" | Organiser pain = **players**. A tournament is the tactic they already reach for | An entire industry sells server visibility ([CS-Boost](https://www.csboost.eu/cs-boost-counter-strike-1-6-servers.php), [tsarvar](https://tsarvar.com/), [game-state](https://game-state.com/)), and [CS:Legacy](https://cslegacy.pl/) advertises weekend tournaments *as player acquisition* |
> | *"Do not sign many partners early"* | **The second partner is when the value proposition becomes true at all** | A shared rating and a traffic promise are both mathematically zero with one community |

---

## 1. Who this is for

Three audiences. Only one decides, and the third is why the other two show up.

| | **Organisers** | **Teams (tier 2–5)** | **Players** |
| --- | --- | --- | --- |
| Who | Polish community server networks — Pukawka is the first; CS:Legacy, CSCO, NGNW, 1shot2kill, CS-Maliver are the findable rest | Amateur and semi-serious teams with no pathway | CS 1.6 and CS2 players, Poland first |
| Their problem | **Players.** Forums are dying, discovery is decaying, and every network competes for a shrinking pool | **Nowhere to be acknowledged.** [Open qualifiers are effectively dead and CS2 is a closed circuit](https://www.hltv.org/news/40785/how-tier-2-cs-descended-into-chaos-in-the-games-new-era) | Nowhere to play competitively without a full team; no record of whether you are any good |
| What they get | Organised teams arriving on a schedule, their brand on the event, a standings table every network's players read | A rating other communities recognise, and a league that exists | A rating that means something and matches that are recorded |
| Decide adoption? | **Yes** | No — they follow the competition | No — they follow the tournaments |

**Organisers are the customer. Teams are the demand. Players are the reason it exists.**

---

## 2. The gap — stated precisely

Every alternative forces a community to choose between **owning the event** and **having a rating
anyone recognises.** Nobody offers both.

| Alternative | What it forces |
| --- | --- |
| **FACEIT / ESEA** | Join *their* circuit on *their* servers — and the open-qualifier pathway has closed |
| **[CYBERSHOKE](https://cybershoke.net/cs2)** | Their servers, their community, their brand. 600 000 players/month that belong to them |
| **[FastCup](https://cs.fastcup.net/?hl=en)** | Their cloud, their matchmaking, CIS-centred |
| **[Starter League](https://starterleague.pl/) / [arrmy.gg](https://arrmy.gg/pl/home)** | Your *team* joins *their* league. The community isn't in the picture |
| **[Gampre.pl](https://www.gampre.pl/)** — 178 614 users, 3 862 tournaments | Their platform, their tournaments — and **results are proven by a screenshot "or a photo taken with a phone"**, adjudicated by an admin |
| **[G5API / G5V](https://github.com/PhlexPlexico/G5API)** | You keep servers and brand — and your rating means nothing outside your own box. Docker, MariaDB, reverse proxy |
| **Challonge / Toornament** | Brackets with no game integration; every score typed by hand |
| **Discord + a spreadsheet** | The real status quo. Free, familiar, terrible |

### Market intelligence — hard numbers, verified 2026-08-11

Community server populations, one snapshot each (varq/tsarvar, corroborated by game-state for PL):

| Market | Servers | Concurrent players | Character |
| --- | --- | --- | --- |
| **CS2 — Poland** | **223** | **4 188** | **The v1 market.** OneTap.pl, UtopiaFPS.pl, Katujemy.eu, NGNW.pl, 1shot2kill.pl — and **they already run 5v5 servers** |
| CS 1.6 — **Brazil** | 207 | **1 306** | **The 1.6 market.** BRXGames, HeadGames.net.br, NOSTALGIA CS — and crucially they run **PUG/Mix competitive servers** |
| CS 1.6 — **Turkey** | **315** | 1 088 | GEZEGEN, prooyun.net, HLPlayer, paladyum — mostly zombie plague, DM, surf/bhop |
| CS 1.6 — Poland | 72 | 189 | Casual: COD mod, FFA, jailbreak. **Laboratory, not market** |

**Two conclusions the numbers force:**

1. **Polish CS2 is 22× Polish CS 1.6**, and those networks *already host 5v5*. The format and the
   infrastructure exist; the competitive layer on top does not. That is the whole product, and it
   is the market at home.
2. **Brazil, not Turkey, is the first 1.6 export.** Turkey has more servers; Brazil has more
   players on fewer servers **and an organised PUG/Mix culture**. Behaviour matters more than
   headcount — Brazilian 1.6 players already play structured 5v5, Turkish ones mostly play zombie
   and surf.

| | |
| --- | --- |
| **Reachable Polish CS2 audience** | **~48 000** across the two largest Discords — CS2 Polska \| CaseHug **37 448**, CS2 TAG **10 160**. Small enough to reach by hand, which is what a launch with no budget requires |
| **Largest Polish amateur platform** | Gampre.pl — **178 614 registered users**. Demand for amateur tournaments in Poland is proven, and it is currently served by screenshots |
| **The ladder lost both ends** | **ESL Mistrzostwa Polski discontinued end of 2023** (with all ESL National Championships); open qualifiers effectively dead. A Polish tier 3–5 team lost its ceiling and its entrance in the same period |

> The three independent figures triangulate: 4 188 concurrent community players, ~48 000 reachable
> in Discord, 178 614 registered on the incumbent. The Polish amateur CS market is real, measurable
> and currently served by screenshots.

### Primary evidence — first organiser interviews, 2026-08-11

**Six Polish networks contacted. Only two — Katujemy and Fragujemy — had ever hosted a tournament.**
The first non-inferred evidence in this project. Confidence: *medium* on the labour finding
(consistent, unprompted, emotionally loaded); *low* on counts.

> ## ⚠ The headline number is 2 of 6 — and we asked why
>
> **Two thirds of Polish community networks have never run a tournament at all.** Asked directly,
> the four non-hosting networks gave two barriers:
>
> | Barrier | Does automation fix it? |
> | --- | --- |
> | **No budget, no attractive rewards** | **No.** Automation makes an event cheaper in *time*, not in *money* |
> | **Low player engagement — and the admins do not know why** | **No** — but the platform exposes the cause, since they currently have no measurement of their own community |
>
> Katujemy, the one active organiser, said *"a little automation could help."* That is the right
> kind of lukewarm: the answer of someone who already does it and would do it more easily.
>
> > **So automation is an expansion play for the few who already organise. It does not, on its own,
> > convert the many who do not.**
>
> ### The prize barrier has an answer, and the evidence contains it
>
> Katujemy pays in **skins, discount codes and PLN**. Fragujemy paid in **virtual currency** — no
> cash cost at all. So the barrier is not "prizes", it is *"prizes we have to buy"*.
>
> [053](decisions/053-rewards-and-prize-pools.md) tier 1 answers exactly that objection:
>
> > **A community with no budget can still offer the one thing money cannot buy them: a rating and
> > a standings place that other communities recognise.**
>
> A skin costs money. A ranking point that counts across Poland costs nothing and **can only be
> issued by a platform that spans communities** — which is precisely what they cannot build alone.
> Add tier 2's **stacked pools**: five networks each contributing a little is a prize none of them
> could afford by themselves, with every contributor named on it.
>
> This is the "Valve removed progression from community servers" thesis arriving at the exact
> objection that blocks four of six networks.

**What the two hosting networks reported:**

| Finding | Evidence |
| --- | --- |
| **The wound is labour, not results** | *"organization — a lot of delays, manual work, no free time"*; **3–5 people per event** |
| **Registration is the worst surface** | It happens on a forum — **"tedious and unprofessional"** |
| Result disputes are real but bounded | **~6 cases** where two teams submitted genuinely different screenshots and an admin had to rule |
| **Communities churn out of running events** | **Katujemy: 3–4 events/month. Fragujemy: last tournament a year ago** |
| Prizes are already paid | **Skins, game discount codes, PLN** (Katujemy); **virtual currency** (Fragujemy) |

> ### The redirect this forces
>
> The premise was *"verified results are the differentiator."* The interviews say verified results
> are **real but secondary** — the primary pain is the manual labour of running an event at all,
> and the primary emotion is not wanting to look unprofessional.
>
> **Fragujemy is the case study.** A community that ran tournaments and stopped, for exactly the
> reasons stated in the same interview. The difference between Katujemy and Fragujemy is whether
> running events stays cheap enough to keep doing — and that is the product.
>
> **Pitch order inverts.** Labour first, verification as the close:
>
> > **Turniej bez 5 osób przy nim. Zapisy, drabinka, wyniki — same się robią.**
> > *(A tournament without five people on it. Registration, bracket, results — they run themselves.)*
> >
> > …and then: *wyniki z serwera, nie ze zrzutu ekranu.*
>
> **Two consequences for the build:** the feature that solves the *primary* pain — tournament
> creation, registration, seeding, check-in, brackets — is **already built and passing checks**,
> while matches and ratings are not. And *"unprofessional"* is a status complaint, which makes the
> shared link and its Open Graph card part of the value rather than part of the marketing.
>
> [053](decisions/053-rewards-and-prize-pools.md) is validated by behaviour: these communities
> already pay prizes in skins, discount codes and złoty. The **2000 PLN tax line is therefore live,
> not theoretical.**

> ### Most communities are venues, not organisers
>
> If only a third of networks ever host, then §1's model — *organisers are the customer* — is
> describing a rare species. The four non-hosting networks are not failed organisers; they are a
> **different role the product should serve directly**:
>
> | Role | What they do | How many |
> | --- | --- | --- |
> | **Organiser** | Creates and runs the event | **Rare** — Katujemy-like, high value |
> | **Venue** | Lends a server. Nothing else | Common |
> | **Audience** | Sends teams, appears in the standings table | Common |
> | **NEXTSKILL** | Permanent first-party organiser filling the gap | Always |
>
> **Participation must not require organising.** Lending a server and sending teams is a far lower
> bar than running an event, and it is the only bar most networks will ever clear. This is what
> §3.1's inter-community cup already does by accident — one host, everyone else sends teams — and
> it should now be the deliberate model rather than a launch tactic.
>
> It also promotes first-party cups from a cold-start fix to **permanent core supply**: if most
> communities will never organise, someone has to, forever. And it makes the community standings
> table load-bearing — **it is how a network that organises nothing still gets promoted.**

> ### ⚠ Poland-first and the CS 1.6 moat do not overlap
>
> 189 concurrent players cannot sustain a league whose ratings converge. **Polish CS 1.6 is a
> laboratory, not a market**: 72 servers running dproto and non-Steam clients is more than enough
> to discharge **V3** — the biggest product risk on the board — and nowhere near enough to be a
> business. The 1.6 *product* ships to Turkey, Brazil and CIS. Stages 4 and 5 below therefore
> collapse into one.

> ### ⚠ Timing: do not launch a competing Polish CS2 league in August 2026
>
> Starter League's season began **8 August 2026** — free, weekly rounds published every Friday,
> Starter Open feeding Starter Masters. Teams have just committed. Recruiting them away with a
> product that does not exist yet is the worst possible opening move. **A first inter-community
> cup asks a team to abandon nothing**, which is the entry that does not collide with them.

**This is a structural gap, not a positioning choice.** FACEIT and CYBERSHOKE cannot close it
without dismantling their own supply chain — which is the only kind of gap worth building on.

### The one-line pitch

> **Twój turniej, na Twoim serwerze, z rankingiem, który uznają inni.**
> *(Your tournament, on your server, with a rating others recognise.)*

And the six-word version of the trade: **Twój turniej, Twoja marka, wspólny ranking.**

Against the largest Polish amateur platform specifically, the difference has a shorter form —
Gampre adjudicates 3 862 tournaments by screenshot and phone photo:

> **Wyniki z serwera, nie ze zrzutu ekranu.**
> *(Results from the server, not from a screenshot.)*

---

## 3. Five decisions that define the product

Resolved by grilling on 2026-08-11. Each names what was given up.

### 3.1 The first event is inter-community, not a pilot

Pukawka hosts the servers; **entry is open and teams are personally recruited from other Polish
networks.** The unit of adoption is not "an organiser" — it is *a cup that crosses communities*.

**Why:** with one community, the shared rating and the traffic promise are both worth zero. This
tests the actual value proposition in a weekend rather than after the CS 1.6 plugin.
**Given up:** the tidiness of a single-partner pilot.

### 3.2 Both layers, one rating

A **continuous first-party ladder** runs alongside **community-run cups**, and both feed the same
global rating.

**Why:** this is arithmetic, not preference. Glicko-2 keeps a team `provisional` until RD settles,
and [025](decisions/025-rating-system.md) gives provisional teams no rank. Sporadic weekend cups
never converge — **so the acknowledgment promised to tier 3–5 teams would never arrive.** A
continuous ladder generates the volume; community cups keep the brand where it belongs.
**Given up:** the simplicity of being only one thing.

### 3.3 A layer, not a destination

The community owns the player. NEXTSKILL owns only the record. **No NEXTSKILL community, no
funnel pointed at our own Discord.**

**Why:** you cannot run a destination on borrowed servers — the moment it succeeds, your
suppliers realise they are suppliers. This is also the one claim the architecture can *prove*:
tournaments are tenant-scoped, ratings are global ([036](decisions/036-authorization.md)). A
sceptical admin can be shown the data model.
**Given up:** CYBERSHOKE's business model, deliberately. See §7.

The LFT board follows the same boundary rather than becoming an exception. [056](decisions/056-lft-board.md)
attaches public recruitment to verified player/team records, reveals a chosen verified contact only
after mutual acceptance, and then moves the conversation back to Discord or Steam. There is no
NEXTSKILL recruitment chat or social feed. The product can prove that it helped form a roster only
when the ordinary invitation is accepted; it does not call a contact reveal a conversion.

**Marketing consequence:** LFT cards are shared as durable player/team links, not disposable advert
URLs. The volatile `/lft` inventory is `noindex`; Discord remains the discovery channel and the
community retains the relationship.

### 3.4 Teams compete; communities are the story

Open entry for any team. Affiliation is displayed, and a **derived community standings table**
aggregates results.

**Why:** it answers *"promote all servers at the same time"* literally — one table with every
network's name in it, which no single community could build alone. It also converts the
prisoner's dilemma into a derby: a rival's participation becomes *desirable*, because both sides
promote a fixture they want to win. And an unaffiliated tier-4 team can still enter, which
matters because those are the teams with nowhere else to go.
**Given up:** nothing structural — teams are already global and unfiltered in the schema. Cost is
one nullable column and a `GROUP BY`.

> A team picks its own affiliation and a community may **disown** one. Otherwise the first troll
> team calling itself Pukawka's representative becomes the community's problem and our support ticket.

### 3.5 Launch CS2-only, with a partner who also runs 1.6

CS 1.6 is Phase 6 — V2b, V3 and V4 unverified, the AMXX plugin unwritten. So v1 competes in the
one segment where Starter League, arrmy.gg and free G5API already operate.

**Why this partner shape:** it validates the software where a plugin already exists (MatchZy),
puts us in front of an organiser who *cannot* be served by Starter League or FACEIT for half
their audience, and turns **V3 verification into a partner conversation** — they have the 1.6
servers and the non-Steam clients. Pukawka [runs both](https://serwery.pukawka.pl/gra,cs16.html).
**Given up:** launching behind the moat. Rejected pulling 1.6 forward — it would delay every
proof behind two unverified assumptions and an unwritten Pawn plugin.

---

## 4. The cold-start problem, and the first-party fix

**The trap:** on day one no organiser is running anything, so a player finds an empty tournament
list and leaves, and an organiser evaluating the platform concludes it is abandoned.

**The fix: NEXTSKILL is its own first organiser**, running open cups anyone can enter.

| Solves | How |
| --- | --- |
| Empty platform | There is always a cup open or a result fresh |
| Ratings never settling | The first-party ladder is the volume source (§3.2) |
| Organiser scepticism | They see a working tournament, not a demo |
| No proof the software works | We run real events on it before asking anyone else to |

**It cost almost nothing to build** — [037](decisions/037-platform-hosting.md) models organisers as
tenants, so this is an `organiser` row with `trusted = true`. **And it does not break
[007](decisions/007-server-hosting-model.md):** first-party cups run on **servers partners lend**.
We still host zero game servers.

---

## 5. The growth loop

Growth is **not** advertising. It is links — and since **community forums are dying, the channel
is now Discord.**

```
someone shares a link          →  Discord (not forums, not any more)
   ↓
the link unfurls into a card   →  team names, score, map (018)
   ↓
someone clicks                 →  lands on a real match, not a marketing page
   ↓
they look up their own team    →  finds a rating, a history, a standings table
   ↓
they share their own link      →  loop
```

| Decision | Marketing consequence |
| --- | --- |
| Server-rendered pages ([018](decisions/018-frontend-architecture.md)) | Links unfurl into preview cards. **A single-page app would have killed the growth channel outright** |
| Open Graph cards ([019](decisions/019-visual-identity.md)) | The shared card *is* the advert, at zero cost per impression |
| Permanent URLs ([021](decisions/021-site-structure.md)) | A link pasted in 2026 still works in 2029 |
| The veto strip ([019](decisions/019-visual-identity.md)) | The most screenshot-able artefact in the product |

> ### ⚠ Build-order consequence
>
> If forums are dying and Discord is the replacement, **the Open Graph card is the entire
> top-of-funnel** — and [BUILD-PLAN](BUILD-PLAN.md) still has it as an unbuilt Phase 5d item, in
> its own words *"quite possibly the most-viewed surface in the product, and so far it is a text
> tag."* **Move it forward.** It is the cheapest marketing asset in the project.

**Locales:** Poland-first makes the pl/en/ru/de vs PL/TR/BR/CIS mismatch moot for now. Ship
Polish, keep the routing, write the other `.resx` files when a market asks — which is exactly
what [051](decisions/051-phase-4-remainder.md) §3's neutral-resource-is-Polish decision enables.

---

## 6. The partner model

Pukawka is partner #1 and trusted. The template:

| Partner gives | Partner gets |
| --- | --- |
| Server capacity for open cups | Organised teams arriving on a schedule — not random traffic |
| Their community as the first teams | Free tournament infrastructure worth real admin hours |
| Feedback that shapes the product | Direct influence on what gets built |
| Credibility by association | Their name in a standings table every network's players read |

**Revised from v1:** the original said *"do not sign many partners early."* That was wrong, and
specifically wrong: the value proposition **is** cross-community, so partner #2 is not a scaling
decision — it is the moment the product's central claim becomes true. Recruit teams from three to
five Polish networks for the first cup.

The caution that survives: each new organiser costs support time, and support time — not hosting —
is the binding constraint.

> **Trust is modelled per organiser** (`organiser.trusted`) — technical and commercial at once. It
> lets real trust be extended to partners who earn it without weakening the defaults.

---

## 7. The fork, and why it is an ordering rather than a choice

Two futures were on the table:

1. **A Polish competitive platform** — FACEIT-for-Poland, national angle, eventually own servers like CYBERSHOKE.
2. **A tournament host** that scales by depth.

**Decision: path 2 now. Path 1 is its second stage, not its alternative.**

The deciding asymmetry is liquidity:

| | Needs | Viable at stage 0? |
| --- | --- | --- |
| **A queue** | **Simultaneous** players. 200 Poles spread over a day is a dead queue | ❌ Hardest cold start in software — and exactly why FACEIT is hard to attack |
| **A tournament** | **Scheduled** players. Eight teams who agreed on Saturday 20:00 | ✅ Forty people is enough, and one community can produce that next weekend |

**Path 2 also delivers path 1's best feature for free.** The strongest part of the national angle
is Poles playing Poles — and a Polish community-vs-community league *is* that, with a better hook:
a derby has a story, a queue has a loading screen.

**What path 1 would cost**, recorded so it is chosen rather than drifted into:

| Requirement | Conflict |
| --- | --- |
| Skill-based queue allocating players to servers | Breaks *"receive, don't orchestrate"* — CONTEXT §3.1, the first engineering principle |
| Anti-cheat | Existential for ranked queue play. See §9 |
| Owning servers | [007](decisions/007-server-hosting-model.md) and [042](decisions/042-hosting.md) **deliberately** avoided becoming a hosting company. €5/month becomes hundreds and growth stops being free |
| Being a destination | Contradicts §3.3 |

**Path 2 manufactures the asset path 1 needs:** a population of ranked Polish teams with settled
ratings playing on a known schedule.

```
cups  →  weekly league  →  continuous ladder  →  queue (only if the tripwire trips)
```

> **Tripwire:** when a single Saturday evening reliably puts **100+ Polish players into rated
> matches without us organising it**, a queue becomes the better product and the right to build it
> has been earned — with the anti-cheat decision made *before* launch rather than after the first
> scandal. Below that number, a schedule beats a queue.

---

## 8. Launch sequence

| Stage | Goal | Done when |
| --- | --- | --- |
| **0. Private** | The software works | A first-party cup runs end to end on a partner server with real players |
| **1. The derby** | Cross-community value is demonstrated, not promised | **One cup with entrants from three or more Polish networks**, and a standings table nobody argues with |
| **2. Pukawka unassisted** | One partner runs a real event alone | An organiser runs a tournament **without us watching** |
| **3. Polish scene** | Word of mouth in PL | Organisers arrive unprompted, having seen a shared link |
| **4. CS 1.6 = Brazil** | The moat opens **outside Poland** | V3 verified on a Polish 1.6 server (the laboratory), AMXX plugin shipped, and the first **Brazilian** organiser registered — 1 306 concurrent players and an existing PUG/Mix culture |

**Stages 4 and 5 merged on 2026-08-11.** They were listed separately on the assumption that CS 1.6
was a Polish market. It is not — 72 servers and ~189 concurrent players, mostly casual public
modes. **The 1.6 product and the international expansion are the same move**, and treating them as
two stages would have scheduled a launch into a market that cannot support it.

**Brazil before Turkey**, despite Turkey having 50% more servers: Brazilian 1.6 already runs
organised PUG/Mix competition, which is the behaviour the product needs. Turkey's population plays
zombie plague and surf — a larger audience, a longer conversion.

**The gate between 1 and 2 changed.** v1 gated on "an organiser runs an event unassisted", which
tests whether the *software* is finished. Stage 1 now tests whether the *offer* is real —
because software that works for one community is G5API with extra steps.

---

## 9. Monetisation — deliberately undecided

**Nothing is charged for now, and that is a decision rather than an omission.**

| Model | Fit | Concern |
| --- | --- | --- |
| **Free forever** | Maximum reach; strongest goodwill in a scene that resents monetised community tools | Hosting is ~€5/mo but time is not |
| **Free for organisers, paid extras** | Prize handling, extended demo retention, custom branding | Must never make core tournaments feel crippled |
| **Premium subscription** | **CYBERSHOKE proves it works** — skin drops, events, missions, 600k players/month | Requires a destination and an audience. Contradicts §3.3 until path 1 |
| **Server rental** | We rent servers to organisers who lack them | Turns us into a hosting company — [042](decisions/042-hosting.md) avoided that |
| **Sponsorship** | A brand sponsors an open cup | Only viable once there is an audience |

Running costs are ~€5/month because BYO servers mean each new organiser costs **nothing
marginal**. We can stay free far longer than a platform that hosts its own infrastructure.

Revisit when a partner asks to pay, or when support time becomes the binding constraint.

---

## 10. What would make this fail

| Risk | Signal to watch | Mitigation |
| --- | --- | --- |
| 🔴 **Cheating poisons the rating** | One disputed match everyone in the scene talks about | **Nothing yet — and this is not on [040](decisions/040-threat-model.md), which covers SSRF, IDOR and zip bombs but not cheating.** The rating *is* the moat; a poisoned rating is worth less than none. Demos, disputes and the referee flow are *detection and appeal*, not prevention. **Needs a recorded decision, not an omission** |
| **"You want my players for your community"** | A partner hesitates on lending servers | §3.3, said out loud and provable in the data model. The marketing must never contradict the architecture |
| **Communities see each other as rivals, not opponents** | Nobody promotes the derby | Frame as fixture, not cooperation. A rival's entry is desirable when both sides want to win |
| **Ratings never settle** | Every team permanently `provisional`, nobody ranked | The continuous first-party ladder (§3.2) exists for exactly this |
| **Looking dead** | Home page with no live match and no recent result | First-party cups ([043](decisions/043-page-contents.md)) |
| **Organiser needs hand-holding** | Every event requires us in the Discord | Gate stage 2 on an unassisted event |
| **A Valve update breaks CS2 plugins** | Matches stop mid-tournament | Pinned CSSharp version, staging server, documented rollback ([002](decisions/002-cs2-plugin.md)) |
| **Ratings feel unfair** | Players argue the number is wrong | Uncertainty shown openly (±RD) ([025](decisions/025-rating-system.md)) |
| **CS 1.6 identity does not work** | `setinfo` behaves differently on non-Steam clients | **V3**, still open. §3.5 turns it into a partner conversation |
| Building for years without launching | The decision stack keeps growing while user count stays zero | Ship the first inter-community cup early, even ugly |

---

## 11. The summary

- **Sell to organisers.** They decide; teams are the demand; players follow.
- **The gap is structural:** everyone else makes you choose between owning your event and having a rating that counts. FACEIT and CYBERSHOKE cannot close it without dismantling their own supply.
- **Beat a spreadsheet and a free self-hosted panel** — by being the thing neither can be: recognised outside your own server.
- **Rivalry is the product.** Community-vs-community, with one standings table promoting every network at once.
- **Be a layer, not a destination** — and say so, because your suppliers are not naïve.
- **Volume is not optional.** Without continuous play, tier 2–5 acknowledgment is a promise that silently never arrives.
- **Poland first, in depth.** The product is global; the relationships are not.
- **Path 1 has a tripwire, not a date.**

---

## Bring-your-own servers as the pitch (see [048](decisions/048-organiser-server-configuration.md))

Organisers already own the machines and the community. A platform that rents them capacity is
asking them to pay for something they have — so **"your servers, your community, your rules"** is
a position the hosted incumbents structurally cannot take.

- **Our cost does not scale with their events.** That keeps monetisation open rather than forcing
  a per-slot fee before there is anything to charge for.
- **The plugins are the product surface** — more than one, ours, versioned. Precisely the piece an
  organiser cannot assemble themselves in an evening.
- **Support is the hidden cost.** When a match fails on someone else's machine the organiser
  blames us regardless. Failures must surface *before* the event — an engineering position
  ([007](decisions/007-server-hosting-model.md)) taken for a commercial reason.
