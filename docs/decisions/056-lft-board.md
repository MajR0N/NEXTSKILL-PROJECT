# 056 — LFT board and recruitment marketplace

**Status:** decided
**Date:** 2026-08-12
**Relates to:** [021](021-site-structure.md), [025](025-rating-system.md),
[029](029-profile-login-footer.md), [040](040-threat-model.md),
[043](043-page-contents.md), [045](045-teams-and-rosters.md),
[049](049-team-page.md), [051](051-phase-4-remainder.md),
[054](054-becoming-a-rated-player.md), [055](055-multi-game-scope.md)

---

## Context

[021](021-site-structure.md) established the reason for `/lft`: finding a fifth is one of the
hardest problems in the community scene, and NEXTSKILL can attach a verified competitive record
to recruitment in a way a Discord message cannot. [043](043-page-contents.md) set fourteen-day
freshness. [049](049-team-page.md) designed team openings and private applications. [051](051-phase-4-remainder.md)
separated cheap interest from a binding roster invitation and bounded team outreach.

Those decisions settled the objects but not the marketplace around them. They did not decide what
the board opens on, which fields are comparable, how the two directions meet, when contact data is
revealed, where private state lives, or when ephemeral recruitment data dies.

The revised [marketing strategy](../MARKETING-AND-SALES-STRATEGY.md) supplies the product boundary:
NEXTSKILL is a layer over community relationships, not a destination trying to own them. LFT must
help scheduled teams form without becoming a social feed, chat product or simultaneous-player queue.

---

## Decision

### One board, two explicit sides

`/lft` is one public, server-rendered, two-sided recruitment marketplace:

1. **Teams recruiting** — active `TeamOpening` rows.
2. **Players looking for teams** — active `LftListing` rows.

They are separate views, never one mixed feed. A page-wide CS2 / CS 1.6 switcher governs every
listing, opening, rating and filter. Game, view and filters are query-string state so a result set
is shareable and works without JavaScript.

The deterministic defaults are:

- `game=cs2`, because the Polish launch and reachable audience are CS2;
- `view=openings`, because the primary player journey is “show me somewhere I can join.”

The same URL never changes its default from account history. CS 1.6 remains one switch away and is
allowed to have an honest empty state rather than counterfeit launch liquidity.

### Recruitment remains attached to durable identity

The board is for discovery, not a second profile system. A player card links to the permanent
player page with game/recruitment state; an opening links to the permanent team page. There are no
standalone detail pages for ephemeral adverts. A Discord link therefore still resolves after an
advert expires, now to the competitive identity and record rather than a dead listing URL.

`/lft` and every filtered variant emit `noindex, follow`. It is public and shareable, but volatile
UGC is not the SEO surface. Durable team and player pages remain indexable. Robots metadata is not
treated as access control.

---

## The listing contract

### Fields shared by player listings and team openings

| Field | Rule |
| --- | --- |
| Game | Exactly one of CS2 / CS 1.6 |
| In-game roles | Up to two from **IGL, AWP, rifler, entry, support, lurker, flexible** |
| Availability | Recurring weekly windows: local weekday, start, end and time zone |
| Languages | Structured multi-select; not inferred from nationality |
| Server regions | Structured multi-select describing playable latency, not nationality |
| Note | Optional plain text, maximum 300 characters; no Markdown or clickable links |
| Cycle | Increments on publication, explicit renewal or a material edit |
| Freshness | Published time and fourteen-day expiry |

Role values are game-scoped product vocabulary rather than user-authored labels. `coach` is not an
in-game role: it remains a roster slot under [045](045-teams-and-rosters.md).

Languages use stable language codes with localised labels. Server regions are a platform-managed
allow-list rather than a compiled country enum, so Brazil or CIS can be added without a migration.
Polish and Central Europe may be suggested at launch but are not restrictions.

Availability compares genuine weekly overlap. The publisher chooses a time zone; a signed-in
viewer sees the windows in their saved time zone. Until an anonymous viewer supplies one, the UI
shows the publisher's labelled zone rather than guessing. An enhancement may detect the browser
zone, but the server-rendered representation remains complete.

### Player-specific fields and facts

A player listing selects one or more acceptable roster commitments:

- player;
- substitute;
- coach.

That axis is separate from in-game role. A support player seeking a starting place is not the same
request as one accepting substitute duty.

Current roster membership does not block a game-specific listing. Current teams remain visible on
the public player page; the product does not make somebody leave before exploring a move. Accepting
an invitation does not silently close their LFT listing, because being rostered was explicitly not
made an eligibility gate. The workspace prompts the player to close or keep it.

An unrated player may advertise and is labelled **Unrated**. The product never replaces missing
evidence with a self-declared skill level: requiring a rating would make finding a first team depend
on already having played for one.

### Team-opening-specific fields and facts

An opening targets exactly one player, substitute or coach roster place. It exists only when a
captain deliberately publishes it and capacity exists; spare capacity alone is not recruitment.
The count of active openings cannot exceed available capacity for that slot.

Every opening designates one current captain as its recruitment contact. All current captains may
manage the opening and requests, but only the designated captain's chosen verified contact is ever
revealed. If that captain leaves or loses the role, new approaches and contact grants pause until a
replacement is explicitly selected.

Filling the target capacity, closing or disbanding the team closes the opening and its unanswered
requests under [049](049-team-page.md). Other captains' external identifiers are never exposed.

### Competitive facts are derived, not copied

Cards display the subject's current game-specific rating state, uncertainty, match count and recent
form from the same projections as ranking and profile pages. Recruitment stores no second rating.

- player ordering/filter bands use OpenSkill's canonical conservative `μ − 3σ` score;
- team rating shows Glicko-2 rating and `±RD`;
- provisional and unrated are explicit states, never silently placed into a settled-rating band.

This is the part a Discord advert cannot self-mint.

---

## Discovery and ordering

Both sides default to `(PublishedAt DESC, Id ASC)`, with protected cursor pagination. Publication,
explicit renewal and a confirmed material edit are the only actions that move an advert. Editing a
note does not bump it. Rating is visible and filterable but never buys default prominence.

Phase 4 provides explicit filters for:

- roster slot;
- in-game role;
- actual schedule overlap;
- language;
- acceptable server region;
- rating state/band.

There is no compatibility score, recommendation model or opaque reordering. Filters narrow the
freshness-ordered set. A zero-result state retains every filter, explains the zero, and offers
explicit one-click removals. A completely empty game/view links to the other view or game and to a
publish/share action; it never injects mismatched results.

Compatibility guides discovery but is not a contractual eligibility engine. Game and roster-slot
intersection are hard requirements. Role, schedule, language, region and rating mismatches produce
a visible confirmation warning but an informed user may proceed. An applicant without a public LFT
listing accepts the opening's game and target slot directly; [049](049-team-page.md)'s private
application-without-public-advert path remains valid.

---

## Publication lifecycle

Listings and openings expire after fourteen days and never auto-renew. One email and in-app reminder
is issued 48 hours before expiry. If the owner does nothing, the advert leaves the public board but
remains privately reviewable for republication.

Explicit renewal reconfirms the structured fields and starts a fresh cycle. Changing a roster slot,
role, recurring schedule, language or server region is also a new cycle:

1. show the consequences before saving;
2. close unanswered requests from the old cycle with neutral `ListingChanged` state;
3. publish the revised advert with new freshness.

Changing only the optional note neither starts a cycle nor invalidates requests. Accepted contact
grants survive public expiry or a later edit; they have their own bounded lifecycle below.

---

## Two directions, two meanings

### Player to team: `OpeningApplication`

A signed-in player applies to one opening with an optional 300-character plain-text note. A public
LFT listing is not required. The player may withdraw; a captain may decline or accept contact; a
captain may later convert the relationship into the existing slot-specific roster invitation. The
player must still accept that invitation under ordinary capacity checks.

### Team to player: `TeamInterest`

A current captain approaches one active LFT player on behalf of one active opening. Context-free
team approaches do not exist. The player immediately sees the team, game, target roster slot,
roles, schedule and designated captain before deciding.

Interest remains cheap and reversible. It does not occupy roster capacity and is not a roster
offer. The binding offer remains the existing invitation from [045](045-teams-and-rosters.md).

### Bounded pending state

- at most ten pending applications per player;
- one application per player/opening/cycle;
- at most ten pending interests per team;
- one interest per team/player pair at a time;
- both pending states expire after seven days.

Withdrawal, decline, acceptance, expiry, listing change or opening closure frees the relevant cap.
Mutually accepted contact is no longer pending.

Declining prevents resubmission during that advert cycle. **Block** is a separate durable pair-level
control that suppresses both sides across later cycles and hides the blocked counterpart from
actionable results. **Report** is separate again and enters moderation; an ordinary decline is not
silently treated as an abuse allegation.

---

## Contact consent, not chat

Anonymous visitors may browse. Publishing an advert or responding in either direction requires an
authenticated account with at least one verified contact method selected as shareable. Discord
keeps this available to non-Steam CS 1.6 players; Steam ownership is not an implicit requirement.

No provider identifier or contact link is public. When the recipient accepts:

- the applicant/player sees the designated captain's chosen verified Discord or Steam contact;
- the designated captain sees the player's chosen verified contact;
- the relationship appears as mutual contact in the private workspace;
- conversation moves off-platform.

NEXTSKILL does not build recruitment chat. It owns the competitive record, consent state and
eventual invitation; the team or community owns the conversation.

The contact grant expires after thirty days unless converted into a roster invitation. Either side
may revoke platform access sooner by closing or blocking. The platform removes its grant and private
record while stating honestly that it cannot erase contact data somebody already copied elsewhere.

---

## Private workspace, notifications and retention

`/me/recruitment` is the one private management surface for:

- the player's LFT listings and applications;
- captain-controlled team openings and interests, grouped by team;
- pending requests;
- mutual contacts;
- recently closed requests.

Public owner pages may link to create/edit actions, but private requests never depend on notification
history and are not scattered across public pages.

Email plus in-app notification is reserved for an event that needs action or protects freshness:

- new application or interest;
- mutual acceptance;
- 48-hour expiry reminder.

Decline, withdrawal, ordinary expiry and automatic closure are in-app only. Unsuccessful closed
applications/interests remain visible privately for fourteen days and are then hard-deleted. Durable
block markers remain. Account erasure removes ephemeral recruitment data immediately.

Mutual contact is a conversation start, not recruitment success. Only acceptance of the resulting
slot-specific roster invitation counts as success and may close the opening as filled. Both events
remain separately measurable; marketing claims use the latter.

---

## Persistence shape

The objects remain separate because their directions, permissions and lifecycle costs differ:

| Object | Purpose |
| --- | --- |
| `LftListing` *(exists, expanded)* | Player advert, one current row per account/game |
| `TeamOpening` | One team-owned game/slot vacancy and designated captain |
| `OpeningApplication` | Player → opening request |
| `TeamInterest` | Team opening → player listing request |
| `RecruitmentBlock` | Durable player/team-pair suppression |

There is no polymorphic `RecruitmentItem` table whose nullable foreign keys change the row's meaning.
Shared field handling may be implemented by one internal value object/module, not by weakening the
database identities.

Applications and interests carry an explicit state such as `Pending`, `Accepted`, `Declined`,
`Withdrawn`, `Expired`, `ListingChanged`, `OpeningClosed` or `Converted`, plus timestamps needed for
expiry and retention. State transitions are allow-listed commands, not entity binding.

A small lifecycle worker delivers the proactive reminder and deletes expired private state. Every
notification has a stable event key so retries cannot send it twice. Reads still exclude expired
rows from the public board even if the worker is late.

---

## Security and moderation boundaries

- All mutations require antiforgery protection, authentication and resource-based authorization.
- Only current captains manage a team's openings and interests.
- Request DTOs allow-list every writable field; ratings, identities and team ownership are derived.
- Notes are encoded plain text, capped at 300 characters and render URLs inert.
- Public projections never contain email, SteamID, Discord ID or contact-grant data.
- Publishing and request submission use dedicated rate-limit policies in addition to the hard open
  caps; failed authorization does not consume somebody else's cap.
- Reported public adverts can be moderated; blocks need no moderator.
- Account erasure removes listings, applications, interests and contact grants, while the existing
  anonymous roster/rating history rules remain unchanged.

---

## What was rejected

- **One mixed feed** — the directions, facts and actions are different.
- **Highest-rating default** — turns evidence into permanent visibility and starves new players.
- **Self-declared skill** — destroys the board's advantage over Discord.
- **Requiring a rating** — makes a first team depend on already having had one.
- **Requiring players to leave a roster before listing** — punishes substitutes and tentative moves.
- **Context-free team DMs** — recreates spam and hides the vacancy being offered.
- **In-app recruitment chat** — contradicts “layer, not destination” and duplicates communities.
- **Automatic renewal or silent filter relaxation** — makes board activity dishonest.
- **Standalone advert pages** — creates dead URLs for temporary state.
- **Compatibility scoring in Phase 4** — fabricates precision before behaviour exists to calibrate it.

---

## Runnable checks

1. Anonymous `/pl/lft` returns the CS2 team-opening view, complete without JavaScript, with
   `noindex, follow`; query state switches game/view/filter predictably.
2. Cards use current ranking/profile projections, show uncertainty, and label a missing rating
   **Unrated** without accepting a self-rated value.
3. Publication validates at most two roles, structured slots/windows/languages/regions and a
   300-character inert plain-text note.
4. Results remain freshness ordered through cursor pagination; rating changes and note edits do not
   move a row.
5. Explicit filters preserve URL state and never trigger opaque scoring or silent relaxation.
6. Fourteen-day expiry removes an advert from public reads; one idempotent reminder is sent at
   48 hours; no path auto-renews it.
7. A material edit closes unanswered old-cycle requests and republishes; a note edit does neither.
8. A player can list while rostered and can list while unrated; current teams stay public.
9. A player without a public listing can apply to an opening; a team cannot approach a player
   without both an active opening and active compatible-game listing.
10. Pending application and interest caps, pair uniqueness and seven-day expiry hold under
    concurrent submissions.
11. Decline blocks the current cycle; durable block suppresses later requests in both directions.
12. Contact identifiers are absent from public HTML and are revealed only after mutual acceptance
    to the player and designated captain.
13. Losing the designated captain pauses new requests/grants without exposing another captain.
14. Contact access expires after thirty days or revocation; unsuccessful closed records disappear
    after fourteen days; account erasure removes all ephemeral recruitment data immediately.
15. Mutual acceptance records a conversation, while only accepted roster invitation records a
    recruitment success and fills the opening.

---

## Consequences

Phase 4 gains more schema and one private page, but every piece closes a concrete lifecycle or
privacy hole. The result is deliberately not a social product: a public, verifiable board helps a
player and a real vacancy find one another, then returns the relationship to the team/community that
owns it.

The current `LftListing` table is only a seed of this contract. Its free-text `Role` and
`Availability` fields, missing roster/language/region/cycle/contact structure, and existing profile
projection must be migrated rather than mistaken for the completed feature.
