# 052 — Cheating, and the integrity of the rating

**Status:** decided
**Date:** 2026-08-11
**Relates to:** [007](007-server-hosting-model.md) (BYO servers), [009](009-api-contract.md) (outbound-only
contract), [011](011-accounts-and-login.md) (identity), [024](024-demos.md) (demos),
[025](025-rating-system.md) (ratings), [035](035-match-result-integrity.md) (result integrity),
[036](036-authorization.md) (referees), [040](040-threat-model.md) (threat model),
[050](050-match-results-and-ratings.md) (dispute and reversal),
[MARKETING-AND-SALES-STRATEGY](../MARKETING-AND-SALES-STRATEGY.md) §10

---

## Why this document exists

[040](040-threat-model.md) covers SSRF, IDOR, tenant leakage and decompression bombs. **It says
nothing about cheating** — while the asset it exists to protect is a cross-community rating, and
that rating is the entire commercial moat.

A poisoned rating is worth less than no rating at all. This is the only risk on the board that
destroys the thing the strategy is built on, so it gets a decision rather than an omission.

## The trade we already made without writing it down

FACEIT's anti-cheat **is a kernel driver** — it boots with the operating system, inventories
running drivers and executables, and has no clean uninstall. That is also the reason players must
install a client to play there.

NEXTSKILL requires **no client installation at all**, because [009](009-api-contract.md) made the
contract plugin-initiated and the panel never speaks to a game client. A player clicks connect and
is in a rated match.

> **Zero-install and kernel anti-cheat are the same decision seen from two ends.**
> The property that makes NEXTSKILL frictionless is precisely the property that forecloses the
> strongest available defence. This was decided implicitly in Phase 3; it is recorded here as a
> deliberate trade rather than an oversight.

And for CS 1.6 the question does not even arise: there is no modern anti-cheat, the clients are
frequently non-Steam, and [010](010-identity-binding.md) already treats the in-game identity as
spoofable by default.

> **Amended 2026-08-22 by [058](058-what-we-sell.md) line 2: the anti-cheat is a product we sell.**
> This removes the *"nothing to charge for"* half of the third-party-licence rejection below, but
> changes no technical decision here — tier 1 stays user-mode, tier 2 stays behind its
> preconditions, and nothing kernel-mode is sold. Two obligations follow. **First**, a sold
> anti-cheat makes a promise a free one does not: §"Optional client anti-cheat" already requires
> stating what it does not stop, and that statement now carries commercial weight. **Second**, the
> three games are not one product — CS 1.6's non-Steam client population (Reunion/dproto) has no
> VAC underneath it and is a materially harder target than CS2, so it is priced and promised
> separately.
>
> **[058](058-what-we-sell.md) §6b also admits third-party client anti-cheats.** CS 1.6 servers
> already run **fungun / EasyCheatDetector**, **Wargods** and others; requiring them to drop what
> they run for an unproven NEXTSKILL client asks them to take a risk for our benefit. We therefore
> **integrate as well as compete**: record which anti-cheat was active and at which version, and
> ingest verdicts where a vendor exposes them.
>
> **This changes nothing about how the record is read.** A third-party verdict reaches us through
> a game server [007](007-server-hosting-model.md) already treats as untrusted, so it is
> **provenance-tagged evidence, never truth, never a gate, and never a confidence tier** — the same
> rule §"The AC state belongs on the record" states below. Several of these tools are old, closed
> and publicly bypassed: *"an AC was running"* is not a security claim and must never be sold as one.
> We bundle no third-party binary and warrant nobody else's detection quality.
> **And we do not say *"powered by"* about a server we do not own.** It claims a relationship we do not have and points the wrong
> way — a server owner chose to run it. A recorded anti-cheat renders as an observation
> (`Anty-cheat: Wargods (zgłoszony)`), never as a guarantee ([058](058-what-we-sell.md) §6b).
> **Two exceptions:** on **our own managed servers** ([058](058-what-we-sell.md) line 4) running a
> vendor is a fact about our infrastructure, and a **signed partnership** removes the objection
> altogether. Neither changes what a verdict *means* — only what we may say about who produced it.
>
> **[058](058-what-we-sell.md) §6c settles the shape of our own anti-cheat: two components.** A
> **sold server-side AC**, and a **free on-demand client scanner** — the scanner is a *verification
> tool*, not a live client anti-cheat. It never runs during a match and is never required to play,
> so §"The trade we already made" holds unchanged and zero-install remains the default. A flag from
> the server side (or an admin) sends a player to verification; he scans; the report reaches our
> panel **and him**, with the verdict.
>
> **Three constraints this document imposes on that flow.** (1) The scanner runs on the machine it
> is investigating, so **a dirty report is strong evidence, a clean one is weak, and a tampered or
> missing report counts as refusal — never as clean.** (2) The ~65% the server side is expected to
> decide alone is **an estimate, not a measurement** (**V14**); the residual 35% is not a failure
> but the corpus this document calls the moat. (3) **HWID bans key on a salted hash and only widen
> a ban already decided** — a user-mode identifier from a suspect's own machine could otherwise be
> forged to aim a ban at a rival, and an HWID ban hits a shared machine, so appeal to a human is
> mandatory (**V13**).

## Decision: detect and make consequential, do not attempt to prevent

**No client is ever required to play.** We defend the **record**, not the machine — and an
*optional* downloadable client sits on top of that as a tier an organiser may require for a
specific event (see "Optional client anti-cheat" below). The default path installs nothing.

Four layers, in order of how much work they do:

### 1. The format is the first defence

**Team-based, scheduled, recorded play is structurally harder to cheat in than an anonymous
queue**, and this is not a consolation prize — it is the main reason this is survivable:

| Solo queue | NEXTSKILL |
| --- | --- |
| Opponents are strangers you never meet again | Opponents are a **named team with a public roster and history** |
| A burner account costs nothing | An account carries a rating, a team, a community affiliation, and [050](050-match-results-and-ratings.md)'s participant history |
| Nobody reviews anything | A demo exists (CS2), a dispute path exists, a referee exists |
| **Smurfing is FACEIT's top complaint** | A team cannot smurf — its identity is persistent and public |

Cheating here means doing it in front of people who know you, on a recording, in a league you
want to keep playing in. That changes the payoff, which is what actually deters most of it.

### 2. Cheap signals we are not currently reading

- **Steam ban status on link and periodically after.** The Steam Web API exposes VAC and game
  bans. An account linking a Steam identity with a recent ban is flagged; an existing link that
  acquires one is surfaced to organisers. This is close to free and we are simply not doing it.
- **Demos, already decided** ([024](024-demos.md)) — CS2 only, kept 90 days, and **finals and
  disputes kept indefinitely**. That retention rule exists for exactly this.
- **Rating anomaly as a flag, never as a verdict.** Glicko-2 already models uncertainty; a result
  far outside what the model predicts is a reason to *look*, not a reason to act. Automated
  accusation from a statistical outlier is how innocent players get banned.

### 3. Humans decide, and the machinery already exists

[036](036-authorization.md) has `Operation.ResolveDispute` and a `Referee` role.
[050](050-match-results-and-ratings.md) built captain dispute, referee reversal, and a ledger that
makes reversal a **recompute rather than a guess**. Cheating resolution reuses all of it — a
reversed result invalidates its ledger rows and re-runs the sweep in deterministic order.

**No new mechanism is required to undo a cheated result.** That is worth stating plainly, because
it is the difference between a scandal that is recoverable and one that is not.

### 4. Bans propagate; trust does not

A ban issued by one organiser is **visible platform-wide but enforced per tenant** by default.
Platform-wide exclusion is a staff action, audited under [051](051-phase-4-remainder.md) §7 with
actor, reason and outcome recorded.

The asymmetry is deliberate and mirrors `organiser.trusted`: **evidence of bad faith should
travel; the authority to exclude everyone should not.** An organiser who could ban a rival
community's players from the whole platform would be a worse problem than the cheater.

## What we accept, explicitly

- **A determined cheater in a public community match will not be stopped by us.** Server-side
  plugins and demos raise the cost; they do not prevent it.
- **CS 1.6 has almost no defence.** No demo pipeline ([024](024-demos.md)), spoofable identity,
  non-Steam clients. Rated 1.6 play rests on the format defence in §1 and on human judgement.

  > **Softened 2026-08-22 by [058](058-what-we-sell.md) §6c — but only for the appeal.** A
  > **client-side POV demo**, armed by the server when the suspicion meter flags a player and
  > uploaded when he appeals, gives 1.6 the video evidence this bullet says it lacks. It is not GOTV,
  > not routine retention, and it is not *demos for 1.6*. **Rated 1.6 play still rests on the
  > format defence and human judgement** — this adds one artefact to the referee's desk in the case
  > that matters most (⊘ **V15**).

  > **For 1.6 we specify a baseline, we do not write one.** The community already solved this
  > server-side, which is exactly our shape — the stack admins actually run is Metamod + AMXX +
  > dproto plus detection plugins (**Ultimate Anti-Cheat (UAC)**, `csf_anticheat.amxx` —
  > explicitly *"server anti-cheat, without client side"* — `simple_wh_blocker.amxx`,
  > `hpp hack block`). Rated 1.6 matches require a declared plugin baseline, and whatever those
  > plugins report is ingested as a **flag**, never a verdict. The list itself belongs to
  > [048](048-organiser-server-configuration.md), which already owes a plugin list.
- **We will be slower than FACEIT to catch a novel cheat**, because we do not see the client.

These are the price of zero-install and BYO servers, both of which are load-bearing elsewhere.
They are acceptable *because the unit of competition is a known team in a recorded, scheduled
match* — and they would not be acceptable for an anonymous public queue.

> **This is a second reason the path-1 tripwire in the strategy matters.** A matchmaking queue
> removes every defence in §1 at once: strangers, disposable accounts, no scheduled fixture, no
> reputation. **If NEXTSKILL ever builds a queue, anti-cheat stops being a policy and becomes a
> prerequisite** — and that conversation must happen before the queue ships, not after the first
> scandal.

## The market, and why we do not fork one

Surveyed 2026-08-11.

| Anti-cheat | Source | Forkable? |
| --- | --- | --- |
| FACEIT AC, EAC, BattlEye, Vanguard, VAC | Closed, proprietary | **No** |
| **CS2AC** — server-side, 17 detection modules | Open, **AGPL-3.0** | Technically, at a cost — see below |
| CS2-Anticheat-Defense (CounterStrikeSharp behavioural plugin) | Open | Yes |
| Little-Anti-Cheat (Source engine) | Open | Yes |
| cstatsentry — Steam profile signals only | **MIT** | Yes |
| yviler/cs2-cheat-detection — LSTM over aim features from demos | Open | Yes (proof of concept) |

**Three reasons forking is rejected**, in increasing severity:

1. **AGPL-3.0 is viral over a network.** The best open server-side anti-cheat available would
   reach the *whole platform* — a hosted service built on AGPL code generally must publish its own
   source. That is a licensing decision about the entire product, taken for one subsystem.
2. **An open anti-cheat publishes its detections to the cheaters.** Not a flaw in those projects;
   it is the structural reason the effective ones are closed. A fork inherits detections that are
   already public and already evaded.
3. **[003](003-build-basis.md) forbids it** — *"No forking, no vendoring, no copy-paste."* The rule
   was written for the game plugins and applies with more force to a security component.

### Why we will never ship a kernel driver

Beyond destroying zero-install: a signed kernel driver is a **liability, not an asset**. Genshin
Impact's anti-cheat driver `mhyprot2.sys` was abused by ransomware operators to disable antivirus
software, and signed anti-cheat drivers are a standard *bring-your-own-vulnerable-driver* primitive
used by LockBit, BlackCat and Cuba.

**One bug in a signed driver hands attackers a Microsoft-signed kernel exploit carrying our
name.** That is a different risk class from everything else in this project, and we are not
equipped to carry it.

## Read, close, then write ours — the permissive path

Demo parsing is mature and permissively licensed: **demoparser2**, **demoinfocs-golang** (FACEIT
run a fork of it), **awpy**. Per [003](003-build-basis.md) these are read for what is hard, then
closed. Server-side and demo-side analysis needs **no client, no driver, no install**, so it is
the only branch compatible with the trade recorded above.

## The corpus is the moat, not the detector

Machine-learning detection needs labelled data, and we have zero matches. That is a timing
problem with an asset hidden inside it:

> [024](024-demos.md) already retains demos 90 days and keeps **finals and disputes
> indefinitely**. This decision routes every cheating accusation through a referee who records a
> verdict. **That is a labelled dataset assembling itself** — demo plus human judgement, the exact
> pair a detector needs, and the pair nobody else in this scene is collecting.

So: build the boring statistical flags now, keep every demo and every verdict from the first
match, and a model becomes possible later *because we were collecting when nobody else was*.
Retention and verdict capture are therefore **load-bearing from day one**, not a Phase 7 nicety.

## Optional client anti-cheat — a downloadable tier, opt-in, never the default

**Decided: NEXTSKILL will offer an optional anti-cheat client as a download.** Zero-install
remains the default and the marketing position; the client is a *tier a player or an organiser may
choose*, never a condition of playing.

Making it optional resolves the UX objection cleanly — a casual regular on a community server
installs nothing and is rated normally, while a team playing a final with a prize on it can be
asked for more. It does **not** resolve the supply question, and the two modes differ so sharply
that they must not be discussed as one thing.

| | **User-mode client** | **Kernel-mode driver** |
| --- | --- | --- |
| Detects | Injected DLLs, known cheat processes, overlay hooks, debugger attachment | The above, plus kernel-mode cheats |
| Beaten by | Kernel-mode cheats, DMA hardware | DMA hardware, hypervisor cheats |
| Needs EV certificate, HLK tests, Microsoft attestation | **No** | **Yes** |
| Signed-driver (BYOVD) liability | **None** | **Yes — a signed kernel primitive carrying our name** |
| A bug means | A crashed helper process | **A blue screen, or a privilege-escalation exploit shipped to every user** |
| Feasible for this project | **Yes** | **Not until the preconditions below are met** |

### The tiers

| Tier | Requirement | Where it applies |
| --- | --- | --- |
| **0 — none** | Nothing installed | **Default.** All community play, scrims, ordinary league matches |
| **1 — user-mode client** | Optional download, runs while the match is live | Organiser may *require* it per tournament — finals, prize events, disputes |
| **2 — kernel driver** | Not built. See preconditions | Reserved. Only if a queue is ever built ([path 1 tripwire](../MARKETING-AND-SALES-STRATEGY.md) §7) |

**Build tier 1 first, and possibly only.** It is buildable without a legal entity, without a
certificate, and without the ability to brick a player's machine — and against the cheats
community CS actually sees (injected DLLs and public cheat loaders) it does real work. It will not
stop DMA hardware or kernel cheats, and the product must say so rather than imply protection it
does not provide.

### The AC state belongs on the record, not in a gate

This is the part that fits the rest of the product. **Whether the client was active is a fact
about the match, recorded alongside the result** — the same shape as
[050](050-match-results-and-ratings.md)'s participant `Provenance` and
[035](035-match-result-integrity.md)'s `integrity`.

- An organiser sets the requirement per tournament; the match config carries it.
- The match record shows which participants ran the client, so a result can be *read* rather than
  merely trusted.
- It is **evidence, never an automatic verdict** — the rule from §2 holds. A missing client is a
  reason a referee weighs a dispute differently, not grounds for an automatic ban.

This keeps the promise the whole product rests on: the record explains itself.

### Preconditions before tier 2 is even considered

Recorded so it is a decision rather than a drift:

1. A **legal entity** to hold the EV certificate and the liability.
2. A **security review of the driver by someone who is not us**, because the failure mode is a
   Microsoft-signed kernel exploit distributed to every player.
3. A **revocation and update path** that works when a bug is found — the `mhyprot2.sys` incident is
   what happens without one.
4. A demonstrated need: **tier 1 measurably failing**, not assumed to be failing.

Until all four hold, tier 2 stays unbuilt. If a queue is ever built, tier 2 stops being optional
and becomes a prerequisite — see the note in §"What we accept".

## Rejected

| Option | Why not |
| --- | --- |
| **Kernel anti-cheat as a requirement to play** | Destroys zero-install, the strongest UX asset the product has. Optional tier 2 remains open under the preconditions above |
| **Writing a kernel driver now** | EV certificate, HLK, attestation — and a BYOVD liability this project cannot carry today |
| **Forking an existing anti-cheat** | AGPL contagion, public detections, and [003](003-build-basis.md) |
| **Third-party anti-cheat licence** | Same client-install cost, plus a per-seat fee against ~€5/month running costs and nothing to charge for |
| **Automatic bans from statistical anomalies** | Punishes the tail of a distribution. Glicko-2 is a model of uncertainty, not a detector |
| **Ignoring it** | The status quo, and the reason this document exists |

## The runnable check

1. A match whose result is reversed for cheating **leaves the rating exactly as if the match had
   never been rated** — ledger rows superseded, sweep re-run, identical output to a clean replay.
2. A statistical outlier **raises a flag and changes no rating** without a recorded human decision.
3. A platform-wide exclusion **cannot be issued by an organiser**, only by staff, and writes an
   audit row carrying actor, reason and outcome.
