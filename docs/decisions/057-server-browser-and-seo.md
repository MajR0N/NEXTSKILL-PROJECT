# 057 — Server browser, verified activity and the SEO engine

**Status:** decided
**Date:** 2026-08-17
**Relates to:** [001](001-game-scope.md), [005](005-cs16-plugin.md),
[011](011-accounts-and-login.md), [012](012-security.md),
[018](018-frontend-architecture.md), [021](021-site-structure.md),
[040](040-threat-model.md), [042](042-hosting.md),
[051](051-phase-4-remainder.md), [055](055-multi-game-scope.md),
[056](056-lft-board.md), [MARKETING](../MARKETING-AND-SALES-STRATEGY.md)

---

## Why this document exists

The growth mechanism in the strategy is *shared links into Discord*. That is a real channel
and a thin one: it reaches people who already know somebody who already knows NEXTSKILL. It
has no answer for the person typing **"serwery cs 1.6 deathrun"** into Google, who is the
single largest population of Polish community-CS players and who currently lands on
[CS-Setti](https://cssetti.pl/lista) or [GO-Setti](https://gosetti.pl/serwery).

Those sites monetise a server list: free monitoring earns traffic, and server owners pay for
visibility. GO-Setti's **MasterBoost** sells list position directly through a bid-and-budget
panel funded from prepaid account credit.

This decision adds that surface to NEXTSKILL, and — more importantly — settles the four
places where copying it would have broken something the platform has already committed to.

---

## 1. We still never dial a game server

[051](051-phase-4-remainder.md) §2 did not merely defend against
[040](040-threat-model.md)'s top risk; it **deleted** it, by making every exchange
plugin-initiated. The risk register anticipates this exact proposal:

> *"A future feature reintroduces outbound game-server control … Any RCON proposal must
> reopen that decision explicitly."*

A server browser needs live counts for servers we do not own, which points straight at
outbound A2S queries to a user-supplied `host:port`. **Refused.** A2S over UDP is not
ordinary SSRF — it is the classic Source-engine **reflection and amplification** vector, and
an endpoint where anyone can submit a target turns our host into a DDoS amplifier aimed by
strangers.

Data comes from two sources that require no packet from us to anyone's server:

| Game | Source |
| --- | --- |
| **CS 1.6** | **Our plugin**, reporting on a five-minute window, separating humans from bots |
| **CS2** (app 730) | Steam Web API server list |
| **CS:GO** (app 4465480) | Steam Web API server list |

**What was given up:** the CS 1.6 catalogue is limited to servers that install the plugin,
while the competition lists thousands. That is accepted deliberately — see §2.

---

## 2. A small verified list beats a large unverified one

Every competing list dials a server and believes the answer. That is why fake-bot inflation
is endemic: a server reporting 24 players when 18 are bots climbs the list, and nobody can
tell.

Our CS 1.6 counts come from inside the server, from a plugin that **distinguishes humans from
bots**. This is the tournament product's thesis one layer down:

> **Player counts from the server, not from a spoofable query.**

It is also the only claim a competitor cannot copy without building our plugin and convincing
owners to run it.

**The plugin is not a limitation here, it is the distribution mechanism.** Phase 6 needs
plugin adoption regardless; being listed — and being *rankable*, see §4 — is the reason an
owner installs it. The listing is the carrot, the plugin is the platform's entry point, and
the tournaments are what the plugin unlocks.

### What the five-minute window actually buys

Not a number — a **time series**. Occupancy history, peak hours, weekday-versus-weekend
patterns, and human-versus-bot ratio *over time*. Three consequences:

1. A single verified server produces a **richer page than a hundred scraped listings**, which
   is what makes per-server pages substantial enough to index (§6).
2. It is unscrapeable. Nobody can reconstruct it from Steam, from A2S, or from CS-Setti.
3. It makes the honesty claim demonstrable rather than rhetorical: a rival's count can be
   shown as 24 with 18 bots, graphed, over weeks.

---

## 3. CS:GO is listed, and 001 is reopened for it

**Corrected fact, 2026-08-17.** [CONTEXT §2.5](../../CONTEXT.md) described CS:GO as reachable
through a `-csgo_legacy` branch. That framing is out of date. On **3–4 March 2026** Valve put
up a **separate, unlisted** CS:GO store page — a frozen build with **no official
matchmaking**, playable only against bots or on **community servers**. App ID reported as
**4465480**; single-sourced, so it is a verification item below, not yet a fact to code
against.

[055](055-multi-game-scope.md) left CS:GO out *pending a population count*. The situation now
argues the other way: a game whose only online option is community servers is precisely the
audience a server list serves.

**Decided: CS:GO is listed, and [001](001-game-scope.md) is reopened to admit CS:GO
tournaments.**

The supporting evidence, which is stronger than it first appears:

- CS:GO is the **only one of the three games where plugin risk is permanently zero**.
  [CONTEXT's](../../CONTEXT.md) top open risk is *"Valve CS2 update breaks
  CounterStrikeSharp — all CS2 matches stop."* A frozen build cannot break a plugin, ever.
- It runs **SourceMod/Metamod**, a decade more mature than CounterStrikeSharp, with Get5 as
  proven prior art ([002](002-cs2-plugin.md) already reads Get5 as reference).

**What was given up:** [006](006-architecture.md)'s rule of two now has *three* games and
zero shipped plugins. The sequencing risk is real and is accepted with eyes open; the rule
still holds, so nothing is generalised across games until at least two run end to end
concretely.

---

## 4. Votes rank servers, and only players who were there may vote

Voting is the most gamed mechanic in this market. Owners reward votes with in-game VIP,
points or skins, so a vote-ranked list measures **how much an owner pays players to vote** —
bid-for-position with extra steps, which is the thing being rejected. Vote-botting and
multi-accounting sit on top.

**A vote counts only from a NEXTSKILL account the server itself confirmed was playing there
inside the voting window.** The plugin already knows who connected and can tell a human from
a bot; [050](050-match-results-and-ratings.md)'s participant provenance is the same shape.

This converts the mechanic instead of adopting it:

- **Vote-botting becomes impossible**, not merely forbidden.
- Buying votes means buying real players who really play there, which is indistinguishable
  from deserving the position.
- Only a plugin-running server can be ranked at all, which is §2's flywheel again.

**Consequence, stated plainly:** Steam-API-listed servers are **visible but not rankable**.
The catalogue and the ranking come from different places on purpose.

### Vote window: rolling month, archived permanently

Only votes cast in the current month decide the current ranking. Each closed month **freezes
into its own permanent page**.

- Self-cleaning: a server that stops being good stops ranking, with nobody intervening.
- Consistent with [056](056-lft-board.md)'s freshness-over-entrenchment rule and
  [021](021-site-structure.md)'s frozen seasons — and it reuses
  [051](051-phase-4-remainder.md) §4's read-as-at-a-timestamp trick rather than snapshotting.
- It manufactures a durable indexable page every month instead of one page that churns.

**Rejected:** permanent accumulation, which entrenches incumbents so a dead server outranks a
thriving one — precisely the *"outdated CS-Setti"* problem being positioned against. Also
rejected: an invisible decay curve, which is the unfalsifiable ranking
[056](056-lft-board.md) already refused.

---

## 5. Two surfaces, because they answer different questions

A votes-ranked list and a *useful* list are not the same list. A visitor arrives to answer
**"where can I play right now?"** If verified servers sorted above unverified ones, an empty
plugin server would outrank a packed one and the page would stop answering the only question
it was opened for.

| Surface | Ordered by | Question it answers |
| --- | --- | --- |
| **The list** `/serwery` | Live **human** count | *Where do I play now?* |
| **The ranking** `/ranking/serwery/{month}` | Votes from confirmed players | *Which server was best this month?* |

Verification is a **badge and a filter on the list, never a position boost**. Both surfaces
are indexable; neither is an opaque blend of the two signals.

### Money, and the line it does not cross

Sponsored slots and advertising are in scope and are how this pays for itself. The boundary:

> **Money buys clearly-labelled placement above or beside a list. It never buys a position
> inside one.**

Bid-for-position was rejected because it forfeits the entire differentiator: if budget moves
rows, a visitor cannot tell which part of the ordering is evidence and which is spend, and
verified counts stop meaning anything. This is [056](056-lft-board.md)'s *rating never buys
prominence*, applied to servers.

**Deferred:** pricing, ad formats, credit model and go-to-market are a separate grilling
against the [strategy](../MARKETING-AND-SALES-STRATEGY.md).

---

## 6. The SEO engine, and the rule that keeps it safe

[056](056-lft-board.md) put `noindex, follow` on the LFT board. A server list is *also*
volatile, so the rule needs stating properly. **Volatility is not the test — durability of
the page's subject is:**

- `/lft?view=Players&role=awp&skip=40` — no stable thing the page is *about*. `noindex`.
- `/serwery/cs16/deathrun` — *"CS 1.6 deathrun servers"* is a search intent that exists
  forever. Contents churn; the subject does not. **Index it.**

### The indexable set

| Indexed | Not indexed |
| --- | --- |
| One page per server | Pagination past page 1 |
| `game × server-type` (deathrun, surf, awp, cod, ffa, retake…) | Sort orders |
| `game × region` | Facet combinations below the content floor |
| `/ranking/serwery/{month}`, permanently | Rare or empty filter combinations |
| Guides and editorial | |

**The content floor is the whole safety mechanism.** A facet page is indexable only when it
holds enough live servers to be worth the click. Google's spam policies name *scaled content
abuse* and *doorway pages* directly: a page listing forty real deathrun servers is useful; one
listing zero, differing from its neighbour by a word in the title, is a doorway, and enough of
them demote **the entire domain** — ranking, LFT and match pages included. Starting strict
costs nothing, because the floor is one number that can be relaxed later. A demotion cannot be
undone on the same schedule.

**Build order:** per-server pages first — that is where the unmatchable time-series data
lives, and they are strong from the first partner server. Facets follow naturally as
onboarding fills them past the floor. **Guides run in parallel from day one**, because they
rank with an empty catalogue and reach exactly the owner who makes it non-empty.

### One domain: `nextskill.pl/serwery`

Not a subdomain, not a separate site. The deciding argument is not SEO authority — it is that
**vote integrity depends on the account system**. Accounts, `setinfo` binding, the plugin and
participant provenance are all one mechanism; splitting the origin would mean cross-origin
SSO, a second trust boundary [011](011-accounts-and-login.md) never budgeted for, and a vote
flow that bounces people between two sites.

Separate domains make sense when two products share an audience but not a mechanism. Here the
mechanism is shared.

**What was given up:** a shared blast radius, and the option to sell the listing business
separately. The content floor is what contains the first; the second is a bet on selling
something that does not exist.

---

## 7. The server page, and the new privacy surface

Activity charts are pure upside and involve no personal data: occupancy over time, human
versus bot, peak hours, weekday-versus-weekend.

**Named player presence is a different thing entirely.** CS-Setti shows unverified nickname
strings scraped from a query. NEXTSKILL would be showing *verified identities* — "this
account is on this server now" — which is real-time presence tracking of identified people,
in a product that has so far persisted **no IP addresses at all** and anonymises on erasure
([011](011-accounts-and-login.md), [012](012-security.md)). Nobody registered for a tournament
platform expecting it to broadcast where they play.

**Decided:**

- **Aggregate always** — counts, human/bot split, occupancy history, peak times. No
  identities required, and this is the differentiating data anyway.
- **Named presence is opt-in, default off** — a profile setting somebody switches on because
  they *want* to be findable, the same mutual-consent shape [056](056-lft-board.md) uses for
  contact details.

**Rejected:** showing everyone connected. It is what the competition does, it would make the
page maximally alive, and it is a harassment vector and a GDPR exposure in an EU-first product
that has been careful to the point of not storing an IP column.

---

## 8. Sequencing

**Built after [Phase 5](../BUILD-PLAN.md#phase-5--frontend-make-it-look-like-something)**, so
the most-visited public surface in the product launches with a decided palette, real
typography and a designed Open Graph card rather than in monochrome.

**The cost, recorded honestly:** the CS2/CS:GO half needs no plugin and could ship today, and
search rankings mature on a clock that cannot be shortened — every month the listing is not
live is authority that does not accrue. That is accepted.

**The dependency to watch:** Phase 5 is blocked on
[044](044-visual-direction.md), which is **open and not yet grilled**. Until 044 closes,
"after Phase 5" has no date. If 044 stays open, this decision should be revisited rather than
allowed to drift — the sequencing was chosen for a finished look, not for delay.

---

## The runnable checks

Per the project's engineering principles, in `tests/NextSkill.Checks`:

1. **No outbound packet is ever sent to a listed server's address.** The listing path has no
   socket to a game server at all — the check that [051](051-phase-4-remainder.md) §2 still
   holds after this feature exists.
2. A Steam-API-listed server **cannot be voted for**; a plugin-verified one can.
3. A vote from an account with **no confirmed play on that server** in the window is refused,
   and one from a confirmed player is accepted.
4. Bots reported by the plugin are **excluded from the human count** that orders the list.
5. The list orders by live human count; **the ranking orders by votes**; neither blends them.
6. A sponsored placement never appears **inside** the organic ordering.
7. A closed month's ranking is **read as at its closing instant** and does not change when a
   later vote arrives — the same property [051](051-phase-4-remainder.md) §4 proves for
   seasons.
8. A facet page **below the content floor returns `noindex`**; above it, `index`. Pagination
   past page 1, and sort orders, are always `noindex`.
9. A per-server page renders its occupancy history **without naming anybody**.
10. Named presence is **absent unless the account opted in**, and disappears when the setting
    is turned off.
11. Account erasure removes votes and presence data, while the anonymous
    roster/rating history rules remain unchanged.

---

## Verification backlog

| # | Assumption | Blocks | How to verify |
| --- | --- | --- | --- |
| **V8** | CS:GO legacy's App ID is **4465480** | this document, §3 | Single-sourced. Confirm directly against the Steam store page or `ISteamApps` before writing it into code |
| **V9** | `IGameServersService/GetServerList` returns community servers for app 730, and for the CS:GO app, with usable filters and rate limits | §1 | Obtain a Web API key and call it; record the rate ceiling |
| **V10** | The CS:GO community-server population is large enough to justify reopening [001](001-game-scope.md) | §3's tournament half | The listing itself measures it — ship the listing first, count, then commit plugin work |

---

## Consequences

The platform gains a second front door — one that answers a query millions of people type and
that Discord links never reach. It also gains a reason for a server owner to install the
plugin *before* they care about tournaments, which is the adoption problem Phase 6 would
otherwise face cold.

What it does not gain is a shortcut. The catalogue advantage stays with the incumbents for as
long as CS 1.6 listing requires the plugin, and the honest claim — verified counts, unbought
order, consented presence — has to be worth more than their ten-year head start. That is the
same bet the tournament product already makes, which is at least a consistent one.
