# NEXTSKILL — Marketing & Solution Sale Strategy

**Status:** living document
**Date:** 2026-08-10

How NEXTSKILL reaches people, why they stay, and what we sell. Grounded in decisions already
made — most of the strategy is a consequence of the architecture rather than something bolted
on top of it.

---

## 1. Who this is for

Two audiences with different problems. Both must be served, but only one of them *decides*.

| | **Organisers** | **Players** |
| --- | --- | --- |
| Who | Community server owners, clan leaders, admins — Pukawka is the first | CS 1.6 and CS2 players in PL / TR / BR / CIS |
| Their problem | Running a tournament is a spreadsheet, a Discord channel, manual scoring, and arguments | Nowhere to play competitively without a full team; no record of whether you are actually any good |
| What they get | Brackets, veto, results, ratings, demos — automatic, on **their own servers** | A rating that means something, a way to find a fifth, and matches that are recorded |
| Decide adoption? | **Yes.** No organiser, no tournament, no players | No — they follow the tournaments |

**Organisers are the customer. Players are the product's reason to exist.** Marketing effort
should follow that asymmetry: one converted organiser brings dozens of players; one converted
player brings nobody.

---

## 2. The cold-start problem, and the first-party fix

**The trap:** on day one no organiser is running anything. A player signs up, picks
"I want to play", and finds an empty tournament list, an empty LFT board and a home page with
no activity. They leave and do not come back. Meanwhile an organiser evaluating the platform
sees the same emptiness and concludes it is abandoned.

Classic chicken-and-egg: players will not come without tournaments, organisers will not come
without players.

### The fix: NEXTSKILL runs its own tournaments

NEXTSKILL is **its own first organiser**, running open cups anyone can enter.

| Solves | How |
| --- | --- |
| Empty platform | There is always a cup open or a result fresh |
| Player dead end | "I want to play" leads somewhere on day one |
| Organiser scepticism | They see a working tournament, not a demo |
| No proof the software works | We run real events on it before asking anyone else to |

**It cost almost nothing to build.** [037](decisions/037-platform-hosting.md) already models
organisers as tenants, so this is **an `organiser` row with `trusted = true`** — no new
concept, no new code path, no special case anywhere
([043](decisions/043-page-contents.md)).

**And it does not break [007](decisions/007-server-hosting-model.md).** First-party cups run
on **servers partners lend**, flagged as available for open events. We still host zero game
servers. The partner gets their name on a tournament that pulls new players into their
community — which is the whole trade.

> This is also the strongest possible sales demo. "Here is a tournament running right now on
> Pukawka's servers" beats any slide deck, and it costs a scheduling conversation rather than
> a marketing budget.

---

## 3. The growth loop

Growth is **not** advertising. It is links.

```
someone shares a link          →  Discord, forum, Steam group
   ↓
the link unfurls into a card   →  team names, score, map (018)
   ↓
someone clicks                 →  lands on a real match, not a marketing page
   ↓
they look up their own team    →  finds a rating, a history, an LFT board
   ↓
they share their own link      →  loop
```

Every technical decision that serves this loop was made for correctness first and turns out
to be the marketing channel:

| Decision | Marketing consequence |
| --- | --- |
| Server-rendered pages ([018](decisions/018-frontend-architecture.md)) | Links unfurl into preview cards. **A single-page app would have made every shared link render as a bare URL** — the growth channel would simply not work |
| Open Graph cards as a design surface ([019](decisions/019-visual-identity.md)) | The shared card *is* the advert. It costs nothing per impression |
| Permanent URLs ([021](decisions/021-site-structure.md)) | A link pasted in 2026 still works in 2029. Archives keep earning |
| Four locales ([018](decisions/018-frontend-architecture.md)) | Reaches PL, plus EN/RU/DE communities without a second product |
| The veto strip ([019](decisions/019-visual-identity.md)) | The most screenshot-able artefact in the product |

**What to actually do:** make sure every page worth sharing has a card worth seeing. That is
the entire top-of-funnel plan, and it is already built.

---

## 4. Positioning — and who we are not

| | What they do | Why we are not competing |
| --- | --- | --- |
| **FACEIT / ESEA** | Matchmaking at scale, anti-cheat, subscriptions, their own servers | They serve individuals queuing solo. **We serve communities running their own events on their own servers.** Different product |
| **Challonge / Toornament** | Brackets and scheduling for any game | **No game integration.** Scores are typed in by hand. We connect to the server, so results are reported not entered |
| **Discord + a spreadsheet** | What most community tournaments actually use today | This is the real competitor. It is free, familiar, and terrible — and beating it means being *easier than a spreadsheet*, not more powerful |

**The honest positioning:** *the tournament infrastructure for communities that run their own
servers.* Not a competitor to FACEIT; a replacement for a spreadsheet and three arguments.

### The one-line pitch

> **Prowadź turniej na swoim serwerze. Drabinka, veto, wyniki i rating — automatycznie.**
> *(Run your tournament on your own server. Bracket, veto, results and rating — automatic.)*

---

## 5. The CS 1.6 moat

**Nobody else serves CS 1.6.** FACEIT, ESEA and every modern platform are CS2-only. The 1.6
scene is real and active — Poland, Turkey, Brazil, Russia/CIS — and has been ignored for a
decade.

| | |
| --- | --- |
| Why others skip it | No Get5, no MatchZy, no demo pipeline. Everything is hand-written in Pawn ([001](decisions/001-game-scope.md)) |
| Why that is a moat | The cost that deters competitors is a **one-time** cost for us. Once the AMXX plugin exists, it does not need rewriting |
| The hard part | Identity: SteamIDs are spoofable on non-Steam servers, which is why [011](decisions/011-accounts-and-login.md) needed two-factor binding. **Anyone copying us has to solve that too** |

**Strategic implication:** CS 1.6 is not a nostalgia feature. It is the part of the product
that cannot be trivially copied, and it reaches an audience with no alternative at all.

---

## 6. The partner model

Pukawka is partner #1. The template it establishes:

| Partner gives | Partner gets |
| --- | --- |
| Server capacity for open cups | Their name on tournaments that bring new players to their community |
| Their community as the first players | Free tournament infrastructure worth real admin hours |
| Feedback that shapes the product | Direct influence on what gets built |
| Credibility by association | Their scene gets a rating system nobody else offers |

**Do not sign many partners early.** One partner running real tournaments proves more than
five who are "interested". Each new organiser costs support time and reveals new edge cases;
the second partner should come only when the first is running events without help.

> **Trust is modelled per organiser** (`organiser.trusted`, CONTEXT §3b) — a technical
> decision, but also a commercial one. It lets us extend real trust to partners who earn it
> without weakening the defaults for everyone else.

---

## 7. Launch sequence

| Stage | Goal | Done when |
| --- | --- | --- |
| **0. Private** | The software works | A first-party cup runs end to end on a partner server with real players |
| **1. Pukawka** | One partner runs a real event | An organiser runs a tournament **without us watching** |
| **2. Polish scene** | Word of mouth in PL | Organisers arrive unprompted, having seen a shared link |
| **3. CS 1.6 abroad** | TR / BR / CIS | Non-Polish organisers register servers |
| **4. Open** | Anyone can organise | Self-service works without support |

**The gate between 1 and 2 is the important one.** If an organiser cannot run an event
without help, the product is not finished — no amount of marketing fixes that, and scaling a
support burden is how small platforms die.

---

## 8. Monetisation — deliberately undecided

**Nothing is charged for now, and that is a decision rather than an omission.** Charging
before the platform is proven optimises the wrong thing.

Options, recorded so they can be evaluated later rather than invented under pressure:

| Model | Fit | Concern |
| --- | --- | --- |
| **Free forever** | Maximum reach; strongest goodwill in a scene that resents monetised community tools | Hosting is cheap (~€5/mo, [042](decisions/042-hosting.md)) but time is not |
| **Free for organisers, paid extras** | Prize handling, extended demo retention, custom branding | Must never make core tournaments feel crippled |
| **Server rental** | We rent servers to organisers who lack them | Turns us into a hosting company — [042](decisions/042-hosting.md) deliberately avoided that |
| **Sponsorship** | A brand sponsors an open cup | Only viable once there is an audience |
| **Donations** | Fits community norms | Never reliable |

**The honest position:** running costs are about €5/month
([042](decisions/042-hosting.md)) because BYO servers mean each new organiser costs us
**nothing marginal** ([007](decisions/007-server-hosting-model.md)). That is unusual, and it
means we can stay free far longer than a platform that hosts its own infrastructure.

Revisit when a partner asks to pay, or when support time becomes the binding constraint.

---

## 9. What would make this fail

Written down so it can be watched for, rather than discovered.

| Risk | Signal to watch | Mitigation |
| --- | --- | --- |
| **Looking dead** | Home page with no live match and no recent result | First-party cups exist for exactly this ([043](decisions/043-page-contents.md)) |
| **Organiser needs hand-holding** | Every event requires us in the Discord | Gate stage 2 on an unassisted event |
| **A Valve update breaks CS2 plugins** | Matches stop mid-tournament | Pinned CSSharp version, staging server, documented rollback ([002](decisions/002-cs2-plugin.md)) |
| **Ratings feel unfair** | Players argue the number is wrong | Uncertainty is shown openly (±RD) so the maths is explainable ([025](decisions/025-rating-system.md)) |
| **A match-fixing scandal** | A disputed final everyone talks about | Corroboration: team confirmation, demo evidence, dispute flow ([007](decisions/007-server-hosting-model.md)) |
| **CS 1.6 identity does not work** | `setinfo` behaves differently on non-Steam clients | **Verification item V3**, still open. This is the single biggest product risk on the board |
| Building for years without launching | 37 decision docs, no users | Ship the first-party cup early, even ugly |

**V3 deserves emphasis.** If `setinfo` behaves differently on Reunion/dproto clients, the
two-factor binding needs redesigning for the majority of the 1.6 audience — and the CS 1.6
moat is the strategic differentiator. **Verify it before building the AMXX plugin, not after.**

---

## 10. The summary

- **Sell to organisers.** They decide; players follow.
- **Beat a spreadsheet, not FACEIT.** Easier, not more powerful.
- **Growth is shared links**, and the architecture already serves that.
- **CS 1.6 is the moat** — a one-time cost for us, a deterrent for everyone else.
- **Run our own cups** so the platform is never empty and never unproven.
- **Stay free while it costs €5/month**, because BYO servers mean growth is free.
- **The gate that matters** is an organiser running an event without our help.
