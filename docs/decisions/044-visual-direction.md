# 044 — Visual direction: palette, theme, and the rest of the design language

**Status:** 🔴 **OPEN — grilling agenda, nothing decided**
**Date opened:** 2026-08-11
**Blocks:** any colour appearing in the product
**Relates to:** [019](019-visual-identity.md) (now provisional), [020](020-motion-and-ux.md),
[041](041-layout-system.md), [043](043-page-contents.md)

---

## Why this document exists

[019](019-visual-identity.md) picked CT blue and T gold in a single pass, while the
conversation was really about *something else* — it was answering "does colour carry meaning?"
not "which colours". [041](041-layout-system.md) then built a whole layout system on top of
tokens whose values were never seriously interrogated.

That is backwards, and it is worth naming plainly: **the layout system is well grilled and
the palette is not.** Everything in 019 that concerns *behaviour* — colour never carrying
meaning alone, uncertainty shown rather than hidden, spacing as information — has survived
scrutiny and stands. Everything that concerns *appearance* is one unexamined choice.

## What ships in the meantime: monochrome

The site is now built in **warm greys only**. This is not a placeholder in the "grey box"
sense; it is a deliberate constraint with two payoffs:

1. **Nothing to unpick.** Colour spreads. Every page shipped with a provisional palette is a
   page to revisit. Tokens shipped as greys are one block to edit.
2. **It audits the accessibility rule for free.** 019 forbids colour from carrying meaning
   alone. Monochrome is the strongest possible test of that claim: anything that becomes
   ambiguous with hue removed *was* leaning on hue, and that is a bug to fix now — not a
   defect for a colour-blind player to discover later.

Mechanically: `--t`, `--ct`, `--win`, `--loss` and `--live` keep their names and become
lightness steps. Restoring colour is an edit to nine lines in `nextskill.css`.

> **Known weak spot already surfaced by the constraint.** The veto strip distinguishes *who
> banned which map* using `--t` versus `--ct`. In monochrome those are near-white versus
> mid-grey — legible, but thin. If it still feels thin at the end of this grilling, the
> component needs a second signal (side label, icon, position) regardless of what palette we
> land on.

---

## The agenda — questions to be grilled, not answered here

### A. Identity and positioning

1. **Who is this supposed to look like, and who must it *not* look like?** FACEIT, ESEA,
   Toornament, Challengermode, Battlefy all exist. Looking like a cheap FACEIT is worse than
   looking like nothing in particular.
2. **Is the CS 1.6 audience served by the same visual language as the CS2 audience?** 1.6
   carries two decades of nostalgia; CS2 players expect a modern esports product. One skin
   for both, or a per-game accent?
3. **Does the platform have a visual identity separate from NEXTSKILL-as-organiser?**
   [MARKETING-AND-SALES-STRATEGY](../MARKETING-AND-SALES-STRATEGY.md) makes us our own first
   customer. If Pukawka runs a tournament here, whose brand is the page wearing?
4. **What does the logo actually look like?** The wordmark is currently type only. Is that
   the answer, or a gap?

### B. Palette — the actual open question

5. **Is the CT-blue / T-gold pairing an insight or a cliché?** It is the most obvious
   possible choice for a CS product. Obvious is not automatically wrong — it is instantly
   legible to the audience — but it has never been argued *against*.
6. **Does side colour mean anything outside a match?** A team in a ranking table is neither
   CT nor T. If the accent only carries meaning on the veto and scoreboard, using it as the
   global primary is decoration wearing a semantic costume.
7. **One accent or two?** Two accents means every component has to answer "which one" and
   most answers will be arbitrary.
8. **Dark-only, or light theme too?** [019](019-visual-identity.md) assumed dark without
   argument. A light theme roughly doubles the token work and every screenshot.
   Who is asking for it?
9. **How many hues does the whole product get?** Counted honestly: surfaces, text levels,
   accent(s), win, loss, live, avatar generation. What is the ceiling?
10. **Contrast floor.** WCAG AA (4.5:1 body, 3:1 large) is the assumed target — confirm it,
    and confirm it holds for `--text-dim` on `--surface`, which is where thin palettes
    usually fail.
11. **Team-colour generation.** Five avatar shades exist. Are five enough before collisions
    are obvious? Do team-chosen colours ever get to override them, and if so what stops a
    team picking something illegible?

### C. Style and theme

12. **What visual style, named explicitly?** Earlier exploration ranged over poster school,
    brutalist, editorial, neo-brutalism, glassmorphism, Swiss. Nothing was ever committed to,
    and "modern, not retro" is a rejection, not a direction.
13. **Type.** The stack is currently `Segoe UI` — a default, not a decision. A display face
    for scores and headings is the single highest-leverage change available. Cost: a webfont
    on a page that must stay fast on Polish mobile connections.
14. **Texture, depth, and motion vocabulary.** Flat, or gradients and shadows? Are the
    existing surface gradients on `.match` a system or an accident?
15. **Corner radius, border weight, density.** These read as "brand" more than colour does
    and are currently inherited from nowhere in particular.
16. **Imagery.** Map thumbnails, team logos, player photos, sponsor logos — does the design
    assume they exist? Right now every image slot is a grey block.

### D. Reality checks

17. **What does it look like with real Polish text?** Longer strings than English, plus
    diacritics. Does the headbar wrap at 360px?
18. **What does it look like empty, and at 128 teams?** [020](020-motion-and-ux.md) is
    emphatic that empty states matter most on day one.
19. **What does it look like as a Discord embed?** Growth runs through pasted links
    ([018](018-frontend-architecture.md)). The Open Graph card may be the most-seen surface
    in the product, and it has had no design attention at all.
20. **Can one person maintain it?** Every additional style axis is a decision to re-make on
    every future page.

---

## Method

The three screens flagged in [041](041-layout-system.md) as able to surprise us — the
**double-elimination bracket**, the **organiser panel**, and the **empty states** — are the
proving ground. A palette that survives those three survives everything else.

**Grilling first, prototypes second.** Prototyping before the questions above are answered
is how 019 happened.

## Exit criteria

This document closes when it can state, with the argument recorded and not just the
conclusion:

- the palette, in tokens, with contrast measured rather than assumed
- the type stack, with its performance cost accepted explicitly
- the named style, and the styles rejected and why
- whether a light theme exists
- the Open Graph card design

At that point [019](019-visual-identity.md) is rewritten to point here for appearance and
keeps only its behavioural rules, and the monochrome block in `nextskill.css` is replaced.
