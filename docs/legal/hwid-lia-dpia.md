# HWID — Legitimate Interests Assessment & Data Protection Impact Assessment

**Status:** 🟡 **DRAFT — written 2026-08-22, unsigned, not reviewed by counsel**
**Owed by:** [CONTEXT §2.11](../../CONTEXT.md) (V13), [012 §6](../decisions/012-security.md),
[058 §6c](../decisions/058-what-we-sell.md)
**Blocks:** shipping any part of the anti-cheat that persists a hardware identifier

> **This is not legal advice.** It is the accountability record GDPR requires us to *have*, written
> at statute-and-guidance level so counsel reviews a real document rather than a blank page. Every
> figure marked **[PROPOSED]** is a number we chose and must sign off; every **[TBD]** is a fact
> nobody has established yet.

> `ponytail:` **the LIA and the DPIA are one file, not two.** Their balancing arguments are the
> same argument — [CONTEXT §2.11](../../CONTEXT.md) says so — and the ICO's own guidance allows a
> DPIA to incorporate the LIA. Ceiling: if a supervisory authority or counsel asks for them
> separately, split at the Part A / Part B line, which is why they are cleanly separated. The
> public notice is a separate file because it has a different audience and gets published.

---

## 0. What is actually being processed

Before any assessment, the plain description. **If this section is wrong, everything below it is
wrong**, so it is stated first and kept short.

| | |
| --- | --- |
| **Controller** | **[TBD]** — legal entity, address, registration number. NEXTSKILL has no company yet |
| **Data subjects** | Players on a rated NEXTSKILL competitive platform. Primary market Poland. **Some are minors** — grassroots CS 1.6 and CS2 skew young, and this materially raises the balancing bar (§A.3) |
| **The identifier** | A **hardware fingerprint** derived on the player's machine by the free client-side scanner ([058 §6c](../decisions/058-what-we-sell.md)) |
| **Which attributes** | **[TBD]** — the exact component list is undecided. The minimisation rule is fixed: **the fewest attributes that survive a reinstall**, and each one must be justified in this table before it ships |
| **What we persist** | **Only a salted, peppered hash.** The raw value exists in memory for one request and is never written to disk, never logged, never in an error report |
| **Also processed** | Scanner reports (signature matches), suspicion-meter records, POV demos supplied on appeal, referee verdicts |
| **Recipients** | Nobody. No processor receives the hash. Hosting is EU ([037](../decisions/037-platform-hosting.md)) |
| **Transfers outside the EEA** | **None for the hash.** Cloudflare / R2 ([042](../decisions/042-hosting.md)) carries demos and static assets; the HWID hash lives only in the EU-hosted PostgreSQL |

**The reversal being recorded:** [012 §6](../decisions/012-security.md) says *"no persistent
application IP column, IP retention zero."* This is the first time NEXTSKILL persists a stable
identifier for a person. That is a deliberate exception, not a drift, and this document is where it
is justified or refused.

---

# Part A — Legitimate Interests Assessment (Art. 6(1)(f))

The three-part test. **All three limbs must pass**; failing one is failing the basis.

## A.1 Purpose test — is there a legitimate interest?

**The interest:** preventing cheating on a platform whose entire product is a *rating* — a claim
about how good a player is. A cheated result does not merely annoy an opponent; it corrupts the
rating of every player in that match and every player they later face
([025](../decisions/025-rating-system.md), [050](../decisions/050-match-results-and-ratings.md)).

**Whose interest:** ours, and — more strongly — **every other player's**. This is not a case where
the controller is the sole beneficiary. Third-party interests are explicitly admissible under
Art. 6(1)(f), and here they are the larger half.

**The named authority: Recital 47** — *"the processing of personal data strictly necessary for the
purposes of preventing fraud also constitutes a legitimate interest of the data controller
concerned."* Competitive cheating for rank on a rated ladder is fraud in the ordinary sense: gaining
a status advantage by deception.

**Why not another basis:**

| Basis | Why it fails |
| --- | --- |
| **Consent** (Art. 6(1)(a)) | A cheater withdraws it, and withdrawal must be as easy as giving it. A ban mechanism that any banned person can switch off is not a ban mechanism. Consent is also not freely given where refusing means not playing |
| **Contract** (Art. 6(1)(b)) | Arguable — the terms could require it — but *necessary for performance* is a narrow test, and a match can technically be played without a hardware scan. Overstating it is worse than not claiming it |
| **Legal obligation** (Art. 6(1)(c)) | No law requires us to detect cheats |

**Purpose limitation, stated as a bound:** the hash is used to **enforce and evade-proof a ban, and
for nothing else.** Not analytics, not matchmaking, not multi-account detection for any other
reason, not payment fraud, not sold, not shared. **Any new use reopens this document** — that is
what function creep looks like, and it is a named risk (§B.4, R6).

> **Verdict: passes.** Recital 47 names it. The interest is real, present, and largely other
> people's.

## A.2 Necessity test — is the processing necessary?

**This is the sharp limb**, and the one an LIA usually fails. *Necessary* does not mean *useful*,
*cheaper*, or *more effective*. It means: **the purpose cannot reasonably be achieved another way.**

**The question to answer honestly: why is an account-level ban not enough?**

Because a banned cheater re-registers in under a minute. Registration costs an email address. That
is not a hypothesis — it is the observed baseline behaviour of every grassroots CS community, and it
is why 1.6 admins have used hardware bans for twenty years. **An account ban bans an account; it
does not remove the person from the ladder**, and the thing being protected is the ladder.

**The less-intrusive alternatives, and why each is insufficient:**

| Alternative | Why it does not achieve the purpose |
| --- | --- |
| **Account ban only** | Defeated by re-registration in a minute. This is the whole problem |
| **Email / phone verification** | Emails are free and unlimited; SMS raises the cost slightly but is defeated by any virtual number — and it collects a **more** identifying datum (a phone number) than a hardware hash |
| **IP ban** | **More intrusive and less effective.** IPs are dynamic, shared by whole households and CGNAT ranges, and reveal approximate location. [012 §6](../decisions/012-security.md) deliberately retains no IP; reversing *that* would be a bigger intrusion than a hash |
| **Steam ban check only** | Already used ([052](../decisions/052-cheating-and-rating-integrity.md)) and cheap, but it detects only what Valve already caught, months late. It cannot act on what we detect ourselves |
| **Behavioural detection with no persistent key** | This is the server-side AC, and we do it. It identifies *the behaviour*; it cannot stop the same person returning under a new account. The two are complements, not substitutes |
| **Kernel anti-cheat** | Enormously more intrusive. [052](../decisions/052-cheating-and-rating-integrity.md) reserves it behind four hard preconditions. Rejecting it is itself an argument that the hash is the proportionate tier |

**The minimisation that makes it necessary rather than merely convenient:**

- **Salted + peppered hash only.** The raw fingerprint is never persisted. We cannot enumerate a
  person's hardware from our database; we can only ask *"is this the same machine as that ban?"* —
  which is exactly, and only, the question the purpose needs.
- **Server-side hashing.** Client-side hashing was considered and rejected
  ([CONTEXT §2.11](../../CONTEXT.md)): it needs a salt the client knows, a modified client fakes
  either form equally, so it buys no security while making pepper rotation impossible.
- **Collected only when a scan is triggered** — not at registration, not every match, not in the
  background. Most players never generate one.

> **Verdict: passes, narrowly and only with the minimisation above.** Drop the hashing, or start
> collecting at registration, and this limb fails. **The limb is doing real work — do not treat it
> as a formality.**

## A.3 Balancing test — do our interests override theirs?

### What the data subject reasonably expects

**Expectation is a favourable factor here, unusually.** Anti-cheat measures — including hardware
bans — are a known and expected feature of competitive gaming. A player joining a *rated* ladder
expects the ladder to be defended. **But expectation is not a blank cheque**, and it collapses
entirely if the collection is undisclosed — which is why Art. 13 disclosure is a *condition* of this
basis, not a parallel obligation.

### The intrusion, described honestly rather than minimised

| Factor | Assessment |
| --- | --- |
| **Special category data (Art. 9)?** | **No.** A hardware fingerprint reveals nothing about health, beliefs, ethnicity or sexuality |
| **Sensitivity** | **Low-to-moderate.** It is a machine attribute, not a personal one. But it is **stable and hard to change** — a person cannot rotate it like an email, which is the point and also the intrusion |
| **Effect of being wrong** | **Severe.** A wrongful hardware ban excludes a person in a way they cannot escape by making a new account. In a scene this small it is close to unrecoverable reputationally |
| **Children** | **Present, and this raises the bar.** Recital 38 gives children specific protection. A teenager wrongly banned has less capacity to navigate an appeal. **The appeal path must be usable by a 15-year-old** — a design constraint, not a nicety |
| **Shared machines** | **The under-discussed one.** Family PCs, siblings, LAN cafés and second-hand hardware are ordinary in this market. **A hardware ban can hit a person who did nothing** — see §B.4 R3, the risk this assessment takes most seriously |

### The safeguards that carry the balance

The balance does **not** tip our way on the strength of the interest alone. It tips because of what
is built around it:

1. **No auto-ban until the false-positive rate is measured** ([V14](../../CONTEXT.md)). At 2%
   prevalence and 99% specificity a third of bans would be innocent people. **That is not a balance
   that passes**, and it is why the meter screens first.
2. **A human referee who can genuinely overrule the machine** (§B.5).
3. **The evidence is shown to the accused** ([058 §6c](../decisions/058-what-we-sell.md) step 6) —
   he disputes a specific finding, not a silent verdict.
4. **An appeal answered with a POV demo** — the accused holds an artefact that can exonerate him.
5. **Defined retention with review** (§B.6), not indefinite storage.
6. **The right to object is real** (§B.7), and the compelling-grounds answer is written in advance
   rather than improvised at the first request.
7. **Hash only**, never the raw identifier.

> **Verdict: passes, conditionally.** The condition is that safeguards 1–7 exist **before** the
> processing starts, not after. **Ship the hash without the appeal path and this LIA is void** — its
> conclusion rests on safeguards that would not exist.

## A.4 LIA outcome

**Art. 6(1)(f) is available as the lawful basis for persisting a salted HWID hash**, provided:

- [ ] The attribute list (§0) is fixed and each attribute justified
- [ ] Retention periods (§B.6) are signed off
- [ ] The Art. 13 notice ([privacy-notice-hwid.md](privacy-notice-hwid.md)) is published **before** first collection
- [ ] The appeal path is built and usable, including by a minor
- [ ] No automated ban ships before V14's false-positive rate is measured
- [ ] Counsel has reviewed this document

**Signed:** ______________________  **Date:** ____________
**Review due:** [PROPOSED] 12 months, or on any change to purpose, attributes or retention

---

# Part B — Data Protection Impact Assessment (Art. 35)

## B.1 Is a DPIA mandatory?

**Yes, on two independent grounds.**

**EDPB WP248** treats **two or more** of its nine high-risk criteria as triggering a DPIA. We hit at
least three:

| WP248 criterion | Us |
| --- | --- |
| **Evaluation or scoring** | The suspicion meter scores a player's behaviour and produces a reliability judgement |
| **Systematic monitoring** | Observation of players' behaviour in the online environment during matches |
| **Vulnerable data subjects** | **Children play here**, and the power imbalance between a platform and a player is real — the player cannot negotiate the terms |
| *(arguably)* **Automated decision-making with significant effect** | Exclusion from a rated platform. Significant, if not legal |

**Art. 35(3)(a)** independently makes it mandatory for *"a systematic and extensive evaluation of
personal aspects … based on automated processing … and on which decisions are based that produce
legal effects concerning the natural person or similarly significantly affect him."* A meter that
ends in a ban is that.

> **So this is not a defensive formality — it is a legal requirement, and a missing DPIA is its own
> infringement**, independent of whether the processing itself was lawful.

## B.2 Systematic description of the processing

The lifecycle, from [058 §6c](../decisions/058-what-we-sell.md), with the data at each step:

| # | Step | Personal data involved | Retained? |
| --- | --- | --- | --- |
| 1 | Server-side AC flags a player, or an admin does | Account ID, match ID, tick | With the case |
| 2 | **Monitoring begins; POV recording armed at the next freezetime** | Demo held **by the player**, not us | **Nothing stored unless he appeals** |
| 3a | **Meter falls (the common case)** — player never told | **Pseudonymous calibration record**, no account link, carrying the **negative** label | **Yes** — §B.6. Required for V14 to have a denominator |
| 3b | Meter rises past threshold | Case record | With the case |
| 4 | Player runs the scanner | **Raw hardware attributes**, transiently | **No — one request, in memory** |
| 5 | Report lands on the panel, signed and encrypted, direct from the scanner | Signature matches only | With the case |
| 6 | **Report shown to the player with the verdict** | — | — |
| 7 | Scanner reports back to the server-side AC | — | — |
| 8 | Clean → plays. Dirty → **ban keyed to the salted HWID hash** | **Hash + decision** | **Yes** — §B.6 |
| 9 | Appeal, answered with the POV demo, decided by a **referee** | Demo, verdict, reasons | With the case |

**Nature:** automated behavioural scoring, then an on-demand client-side scan the player initiates,
then a human decision.
**Scope:** players flagged by the meter. Not the whole player base.
**Context:** grassroots competitive CS, Polish primary market, some minors, a small scene where
reputation matters and exclusion is visible.
**Purpose:** protecting the integrity of a rating.

## B.3 Necessity and proportionality

Carried by **Part A**, which is the same argument and is not repeated. Three additions specific to
the DPIA:

- **Purpose limitation** — bounded in §A.1, enforced by *R6* below.
- **Data minimisation** — hash only; attributes justified individually; collection triggered, not
  routine.
- **Accuracy (Art. 5(1)(d))** — the meter's accuracy is not assumed. It is **measured**, by
  `DetectionCalibration` + `CalibrationChecks`, and **the negative labels in step 3a exist precisely
  so accuracy has a denominator.** A system that records only its own hits cannot demonstrate
  accuracy, and Art. 5(2) makes demonstrating it our burden.

## B.4 Risks to the rights and freedoms of data subjects

**Severity × likelihood, before and after mitigation.** R1 and R3 are the two that matter.

| # | Risk | Sev | Lik | Mitigation | Residual |
| --- | --- | --- | --- | --- | --- |
| **R1** | **False positive → wrongful, hard-to-escape ban.** At 2% prevalence and 99% specificity, a third of bans are innocent people | **High** | Med | Meter screens before the scan; **no auto-ban until the FPR is measured (V14)**; human referee; evidence shown; appeal answered with a POV demo; ledger reversal replays every rating ([050](../decisions/050-match-results-and-ratings.md)) | **Med** — and it stays Med until V14 has real numbers. **This is the risk that gates the ship date** |
| **R2** | **Art. 22 — solely automated decision** with significant effect | High | Med | §B.5 | Low |
| **R3** | **Shared or transferred hardware — the ban hits the wrong person.** Family PC, sibling, LAN café, second-hand machine. Ordinary in this market | **High** | **Med** | **[PROPOSED] a hardware ban never fires on the hash alone.** The hash **raises a case**; a referee decides. A person whose only connection is the machine can rebut it, and the rebuttal must be cheap to make and cheap to accept. **[TBD] whether the scanner can distinguish an OS reinstall from a different user** | **Med** — a genuinely unresolved residual, and it must be named in the notice |
| **R4** | **Pepper compromise → the hash space is brute-forceable.** Hardware attributes have low entropy; a leaked pepper turns the hash back into an identifier | Med | Low | Pepper held **outside the database** (existing pattern, [012](../decisions/012-security.md)); never in the repo, never in a backup alongside the DB. **[TBD] rotation procedure** — rotation invalidates every stored hash, so the plan must exist before the first ban |
| **R5** | **Scanner over-collection.** A disk scanner sees far more than cheats — file names, documents, unrelated software | **High** | Med | **[PROPOSED] the report contains signature matches only — never a file listing, never a directory tree, never a process dump beyond matches.** A build constraint on the scanner, belonging in its spec rather than in a later review | Low |
| **R6** | **Function creep** — the hash gets reused for multi-accounting, marketing, or "just this once" | Med | Med | §A.1 bounds it. **Any new purpose reopens this document.** [PROPOSED] the DB column carries a comment naming this file | Low |
| **R7** | **Erasure request conflicts with the ban** | Med | High | §B.7 — the tombstone rule, reasoned in advance | Low |
| **R8** | **Transparency failure** — collection is undisclosed | High | Low | The Art. 13 notice is a **precondition** of first collection (§A.4). Undisclosed collection is a separate breach from unlawful collection | Low |
| **R9** | **Minors cannot navigate the appeal** | Med | Med | **[PROPOSED] the appeal is one form, plain language, no legal vocabulary, and the verdict text explains itself.** The same principle as `±RD` and `Provenance` — the record explains itself | Low |

## B.5 Art. 22 — automated decision-making

**Art. 22(1) prohibits decisions based *solely* on automated processing** producing legal or
similarly significant effects. Platform account suspension is a recognised instance: the Italian
**Garante fined Foodinho €5 million** for algorithmic rider deactivation with no human intervention
and no way to contest.

| Art. 22(3) safeguard | Where it lives |
| --- | --- |
| **Human intervention** | A referee decides. **Not a rubber stamp** — [050](../decisions/050-match-results-and-ratings.md) lets a referee reverse a result and replay every rating from history, so the authority is real and demonstrable |
| **Right to express a point of view** | The report is shown to the player with its verdict; he disputes specific findings |
| **Right to contest** | The appeal, answered with the POV demo |

> ⚠ **The trap, stated so it cannot be forgotten: *"solely automated"* includes rubber-stamp human
> sign-off.** A reviewer who cannot genuinely overrule the machine does not count. **The moment a
> referee's queue is large enough that they click through, this safeguard is fiction and Art. 22
> bites.** [PROPOSED] measure it: a referee override rate of ~0% across a meaningful number of cases
> is evidence of rubber-stamping, not of a perfect detector, and must trigger a review.

**This gives V14 a second, independent reason.** Auto-banning before the false-positive rate is
measured is not only a product risk — without a genuine human path it is an Art. 22 problem.

## B.6 Retention (Art. 5(1)(e))

*Indefinite* is hard to defend. A defined period tied to the ban is. **Every period below is
[PROPOSED] and needs sign-off.**

| Data | Retention | Reasoning |
| --- | --- | --- |
| **Raw hardware attributes** | **Never persisted.** One request, in memory | Minimisation |
| **HWID hash + decision, fixed-term ban** | **Ban duration + 90 days** | The 90 days is the appeal/dispute window, deliberately matching [024](../decisions/024-demos.md)'s demo retention so the ban and its evidence expire together. **A ban outliving its evidence is unappealable** |
| **HWID hash + decision, permanent ban** | **Retained, reviewed every 24 months**, review recorded | A permanent ban needs a stronger justification *and* periodic review, both documented. **The review must be a job that runs**, not a policy |
| **Scanner report** | **90 days after the case closes**, or with the ban if one results | It is evidence for the appeal and worthless after |
| **Pseudonymous calibration records (incl. negative labels)** | **[PROPOSED] 36 months** | The necessity argument: V14's arithmetic needs **~16 000 clean observations** to claim a population-wide rate, and grassroots volume takes years to produce that. **No account link, so this is the least intrusive form of the data** — but 36 months is long, and it is the period counsel should push back on hardest |
| **POV demo supplied on appeal** | **With the case**; [024](../decisions/024-demos.md)'s exception rule (disputes kept) applies | We store nothing unless he appeals |

> **A retention policy with no job enforcing it is worse than none** — it is a written promise we are
> visibly breaking. [024](../decisions/024-demos.md) already makes that point about demos; it applies
> harder here. **Every row above needs its job.**

## B.7 Data subject rights

| Right | How it is answered |
| --- | --- |
| **Information (Art. 13)** | [privacy-notice-hwid.md](privacy-notice-hwid.md). **Published before first collection** |
| **Access (Art. 15)** | The player is told a hash exists, its purpose, its retention and the decision it supports. **We cannot return the raw identifier — we do not have it**, and saying so is the honest answer |
| **Erasure (Art. 17)** | **The ban tombstone survives, and here is the reasoning.** **Art. 17(3)(e)** — retention for the establishment, exercise or defence of legal claims. **Art. 17(1)(c)** with **Art. 21(1)** — we may refuse where we demonstrate compelling legitimate grounds. **What survives is the minimum: the hash and the decision. Not the account, not the contact data, not the demo.** This is [012 §6](../decisions/012-security.md)'s *anonymise the player, keep the match* taken one step further: **erase the person, keep the ban tombstone** |
| **Objection (Art. 21(1))** | **Available, because the basis is legitimate interests.** The compelling-grounds answer is §A.2's necessity argument, written **in advance** — *demonstrate* is the operative word in Art. 21(1), and an improvised answer at the first request is not a demonstration |
| **Rectification (Art. 16)** | The appeal path is the rectification path. A wrong verdict is corrected and the rating ledger replayed |
| **Art. 22(3)** | §B.5 |
| **Portability (Art. 20)** | Not engaged — the basis is legitimate interests, not consent or contract |

## B.8 Security measures

Beyond the hashing rule: TLS in transit; **hash server-side with a secret pepper held outside the
database**; **never log the raw value** — not in an access log, not in an exception, not in a debug
build; scanner reports travel **direct from scanner to panel, signed and encrypted**, and a tampered
or unverifiable report is **refusal, never clean** ([058 §6c](../decisions/058-what-we-sell.md));
access to the ban table is covered by the existing append-only audit log
([012 §7](../decisions/012-security.md)).

## B.9 Consultation and review

| | |
| --- | --- |
| **Data subjects consulted?** | **[TBD]** — Art. 35(9) says *"where appropriate."* [PROPOSED] ask Pukawka's community. It costs a forum post and the answer is genuinely informative |
| **DPO** | **Not appointed.** Art. 37 triggers are **[TBD]** — *"regular and systematic monitoring of data subjects on a large scale"* becomes arguable as the platform grows, and this assessment should not assume it away |
| **Prior consultation (Art. 36)** | Required only if residual high risk remains after mitigation. **R1 and R3 sit at Med after mitigation, not High — the conclusion doing the most work in this document, and the first to re-examine if V14's numbers come back badly** |
| **Review** | [PROPOSED] on any change of purpose, attributes or retention; on V14's first real numbers; otherwise every 12 months |

## B.10 DPIA outcome

**Proceed, conditionally.** The conditions are §A.4's checklist plus:

- [ ] R3's rebuttal path built — **a hash never bans on its own**
- [ ] R5's scanner constraint written into the scanner spec — **signature matches only**
- [ ] R4's pepper rotation procedure exists **before the first ban**
- [ ] A retention job exists for every row of §B.6
- [ ] Referee override rate instrumented (§B.5)

**Signed:** ______________________  **Date:** ____________

---

## What writing this changed

Three things [CONTEXT §2.11](../../CONTEXT.md)'s research did not surface, found while working the
balancing test:

1. **Shared hardware (R3) is a first-class risk, not an edge case.** Family PCs, siblings, LAN cafés
   and second-hand machines are ordinary in Polish grassroots CS. **A hash must never ban on its
   own** — it raises a case a referee decides. That is a design change, not a paragraph.
2. **The scanner is the bigger privacy surface, not the hash (R5).** A disk scanner sees documents
   and unrelated software; the hash sees one machine attribute. The constraint — **signature matches
   only, never a file listing** — belongs in the scanner's spec now, while the scanner is still
   unwritten and it is free.
3. **A ~0% referee override rate is evidence *against* us, not for us** (§B.5). It is what
   rubber-stamping looks like from the outside, and Art. 22 turns on exactly that distinction.
