# Privacy notice — anti-cheat section (Art. 13)

**Status:** 🟡 **DRAFT — written 2026-08-22, not reviewed by counsel, not published**
**Owed by:** [CONTEXT §2.11](../../CONTEXT.md) (V13), [012 §6](../decisions/012-security.md)
**Reasoning behind every claim here:** [hwid-lia-dpia.md](hwid-lia-dpia.md)

> ⚠ **This is a section, not the whole notice.** It covers only the anti-cheat processing. It gets
> pasted into the full NEXTSKILL privacy notice, which does not exist yet and also owes sections on
> accounts, demos, Discord/Steam links and cookies.

> ⚠ **Publication is a precondition, not a follow-up.** Art. 13 requires the information *at the
> time the data is obtained*. **Undisclosed collection is a separate breach from unlawful
> collection** — so if this is not live, the anti-cheat does not collect.

> ⚠ **The published version must be Polish.** `pl` is the primary locale
> ([018](../decisions/018-frontend-architecture.md)) and the market is Poland. This English draft
> exists so counsel reviews the *substance*; the Polish text is the binding one and **must not be a
> machine translation of it**. The other three locales follow the ordinary localisation path.

> `ponytail:` written in plain second person, not legalese, on purpose. Art. 12(1) requires *"concise,
> transparent, intelligible and easily accessible form, using clear and plain language"* — and
> [hwid-lia-dpia.md](hwid-lia-dpia.md) §B.4 R9 records that some readers are 15 years old. A notice
> nobody can read fails the article that requires it.

---

## Draft text

### Anti-cheat

NEXTSKILL is a **rated** platform: every match changes a number that describes how good you are, and
how good the people you played against are. That makes cheating a problem that reaches past one
match — it corrupts the ratings of everyone in it and everyone they play later. This section
explains what we do about that, and what it means for your data.

#### When anything happens at all

Nothing here runs in the background and nothing runs on your machine while you play.

Our server-side anti-cheat watches match events on the game server. If it — or an admin — finds
something suspicious, a **monitoring period** starts. **Most monitoring periods end with nothing
happening and you are never told about them.** That is the intended outcome, and we keep a record of
it (see *What we keep*) without linking it to your account.

If the suspicion does not resolve, we ask you to run our **scanner** — a program you download and
run yourself, when we ask, which checks your machine for known cheat software and sends us a report.
**It is not required to play**, it does not run during a match, and it does not stay installed.

#### What the scanner collects

| | |
| --- | --- |
| **A hardware identifier** | Characteristics of your computer, combined into a single value that identifies **the machine**, not you personally |
| **Cheat signature matches** | Only the things it recognises as cheat software. **It does not send us a list of your files, folders, documents or other programs** |

**We never store the hardware identifier itself.** It is converted, on our server, into a one-way
value (a "hash") using a secret only we hold. We keep only that value. It lets us ask *"is this the
same machine that was banned?"* and nothing else — we cannot work backwards from it to your
hardware.

#### Why we do this, and on what legal basis

**Legal basis: our legitimate interests, Art. 6(1)(f) GDPR** — specifically preventing fraud, which
Recital 47 of the GDPR names as a legitimate interest. The interests are ours and, more so, those of
every other player whose rating a cheated match distorts.

**Why the identifier and not just an account ban:** banning an account takes a minute to defeat —
you make another one. A ban that cannot be defeated in a minute needs something that is not free to
replace. We looked at the alternatives (email or phone verification, IP bans) and recorded why each
one is either weaker, more intrusive, or both.

#### Decisions are made by a person

**A hardware match never bans anyone by itself.** It raises a case that a **human referee** decides,
and that referee can — and does — overrule the system.

**This matters if you share a computer.** A family PC, a sibling's machine, a LAN café, a computer
you bought second-hand: the identifier is the machine's, not yours. **If a case reaches you because
of a machine rather than because of you, say so** — it is a normal outcome and a normal thing to
resolve.

If a decision goes against you:

- **You see the evidence.** The report and the verdict are shown to you, not just the conclusion.
- **You can respond to specific findings**, not just dispute the result.
- **You can appeal**, and a referee reviews it.
- **Where we recorded your match from the server's point of view, that recording is part of the
  answer to your appeal.**

You have these rights under **Art. 22 GDPR**, which limits decisions made purely by automated means.
We are telling you about them here because you should not have to find out they exist.

#### What we keep, and for how long

| What | How long |
| --- | --- |
| **The raw hardware identifier** | **Never stored.** It exists for the length of one request and is discarded |
| **The one-way value, where a ban resulted** | The length of the ban **plus 90 days** — so the evidence outlives the ban and an appeal is still possible |
| **The one-way value, where a permanent ban resulted** | Kept, and **reviewed every 24 months** to check the ban is still justified |
| **A scanner report** | **90 days** after the case closes, or for as long as the ban it supports |
| **A record that a monitoring period happened and resolved as nothing** | **Up to 36 months**, with **no link to your account**. We keep it because without it we cannot measure how often the system is wrong — and a system that only counts its own hits cannot be checked |
| **A match recording you sent us with an appeal** | With the case |

#### Your rights

You can ask us for a **copy** of what we hold, ask us to **correct** it, ask us to **delete** it, and
— because our basis is legitimate interests — **object** to this processing entirely. You can also
complain to the Polish supervisory authority, **UODO** (uodo.gov.pl).

Two honest limits:

- **We cannot show you the hardware identifier**, because we do not have it. We have only the
  one-way value.
- **If you are banned, deleting your account does not lift the ban.** We keep the one-way value and
  the decision, and nothing else — not your name, not your email, not your contact details. GDPR
  allows this (**Art. 17(3)(e)** and **Art. 21(1)**) where we can show a compelling reason, and ours
  is that a ban you can erase by pressing "delete account" is not a ban. **If you think that is
  wrong in your case, object — we will answer.**

#### Where it is held

The one-way values and case records are stored on servers **in the European Union**. They are not
sent to anyone else — no advertiser, no analytics provider, no other platform.

**Contact:** [TBD — controller name, address, contact e-mail]

---

## Before this is published

- [ ] Controller identity filled in
- [ ] Retention periods in [hwid-lia-dpia.md](hwid-lia-dpia.md) §B.6 signed off — **the numbers here must match that document exactly**
- [ ] The list of hardware attributes decided, and checked against *"characteristics of your computer"* above being an honest description of it
- [ ] Counsel review
- [ ] Polish translation, by a person
- [ ] Pasted into the full privacy notice, which still owes its other sections
- [ ] Live **before** the anti-cheat collects anything
