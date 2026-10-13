# 049 — Team page competitive history and recruitment

**Status:** decided
**Date:** 2026-08-11
**Relates to:** [021](021-site-structure.md) (site structure),
[025](025-rating-system.md) (ratings), [035](035-match-result-integrity.md) (result trust),
[036](036-authorization.md) (captain authority), [043](043-page-contents.md) (page contents),
[044](044-visual-direction.md) (logos), [045](045-teams-and-rosters.md) (roster)

---

## One team identity, two competitive views

The public team identity and roster are shared. Every competitive fact — rating, rating trend,
match activity, record, trophies and recruitment openings — sits under one page-wide
**CS2 / CS 1.6 switcher** and is never mixed across games.

Both games remain visible even when one has no activity. An honest empty state lets a captain
publish a first opening before the team has played or earned a rating.

The selected game is shareable URL state: `?game=cs2` or `?game=cs16`. Without an explicit
selection, choose the team's most recently played game, then its only rated game, then CS2.
There is no cookie-only state that makes the same URL mean different things to different people.

The page leads with the selected game's compact competitive overview. The shared roster and
complete historical sections follow it.

## Match activity and record

Urgent activity and history are different surfaces:

- show at most **three live/upcoming matches** first;
- show the **five most recent results** in the overview;
- continue older results in protected cursor pages of **20 rows**.

The overview also shows the all-time confirmed W–L record and last-five form. A scoreless
forfeit is an explicit administrative win/loss and appears in history, but remains unrated.
A bye is bracket routing where no match occurred: it appears in the bracket, not match history,
the W–L record or form. Cancelled matches count nowhere.

Decision [035](035-match-result-integrity.md) remains the gate. Suspect and disputed scores may
be displayed as unresolved facts, but they are never labelled or counted as a win or loss.

## Rating trend

Current Glicko rating and RD remain visible together. The trend is a server-rendered,
non-interactive chart over the last **20 rated matches**, with uncertainty exposed through
accessible labels/data. Unrated outcomes do not create pretend rating points; their history row
already explains why they did not move the rating.

The trend comes from immutable post-result rating changes, not from trying to reconstruct an old
rating from the current aggregate. This ledger is also the audit/recomputation seam required by
[025](025-rating-system.md).

## Trophies are trusted placements, not self-issued badges

Only a placement from a NEXTSKILL event or a **trusted organiser** may become a prominent public
trophy. An untrusted community event still appears in ordinary tournament and match history, but
cannot mint profile credentials for itself.

Trophies represent authoritative top-three placements:

- champion and runner-up may come from a completed final;
- third place exists only when explicit standings or a third-place result establishes it;
- semifinalists never receive invented joint bronze awards.

Completion writes an immutable placement record with its game and provenance. The game-specific
shelf is ordered newest-first. Each item names its season or says **independent event**, so
independent cups remain first-class without producing sparse season groups.

## An open slot is an intentional recruitment listing

Unused roster capacity is not recruitment. An **open slot** exists only when a captain publishes
one specific, game-scoped position containing the target roster slot, preferred in-game role,
availability and an optional public note. A team may therefore advertise a CS2 substitute and a
CS 1.6 coach independently.

Each opening expires after **14 days**, supports explicit one-click renewal, and closes when the
captain closes it, the target capacity is filled, or the team is disbanded. The same freshness
rule governs player LFT listings in [043](043-page-contents.md).

Candidates respond through an authenticated **I'm interested** action, never through public
Discord, Steam or contact details. It creates a lightweight application rather than a chat:

- one active application per player/opening;
- an optional short note;
- applicant withdrawal and captain decline;
- a captain may convert it into the existing slot-specific roster invitation;
- the player still has to accept that invitation under the normal capacity checks.

Applying to one chosen team does **not** require a public LFT listing. If one exists its structured
role and availability may enrich the captain's view, but private interest is narrower than public
advertising and must remain possible on its own.

## Logos

Captain-managed square team logos are wanted, with the generated monogram as a permanent
fallback. Phase 4 continues to use monograms. Upload format, cropping, storage, validation,
moderation and final presentation wait for [044](044-visual-direction.md)'s prototype and
production-asset work, so one page slice does not quietly invent a separate treatment.

## Fixed boundaries

- Only captains mutate roster, openings or applications; the competitive history is public.
- Disbanding closes recruitment but preserves ratings, matches and trophies on the permanent team
  URL, consistent with [045](045-teams-and-rosters.md).
- Empty states explain no rating, no matches, no trophies or no openings without fabricating data.
- Every public free-text field is encoded, moderated and subject to [040](040-threat-model.md)'s
  abuse controls.

## Implementation dependencies

The current page already has identity, roster roles/capacity, captain controls, disband/restoration
and current per-game rating. Complete history, trend and trophies require the Matches slice to
commit reported results, rating changes and tournament placements. Openings/applications share
the recruitment model completed by [056](056-lft-board.md). Those dependencies block fabrication,
not this design: the product contract above is closed.
