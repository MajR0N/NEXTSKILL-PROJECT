# 046 — Tournament lifecycle, registration and seeding

**Status:** **decided**
**Date:** 2026-08-11
**Relates to:** [022](022-tournament-formats.md) (formats), [023](023-bracket.md) (brackets),
[036](036-authorization.md) (tenancy), [043](043-page-contents.md) (page shape),
[045](045-teams-and-rosters.md) (rosters)

---

## The state machine

```
Draft ──▶ RegistrationOpen ⇄ RegistrationClosed ──▶ Running ──▶ Completed
   └──────────────┴───────────────┴──────────────────┴──▶ Cancelled
```

**Everything not listed is refused**, so a status added later cannot slip into a transition
nobody decided. Four rules carry weight:

| Rule | Why |
| --- | --- |
| **Draft is private** | An organiser sketching next season's event must not have it appear in a public list. A leaked draft is exactly the kind of thing that erodes an organiser's trust in the platform ([037](037-platform-hosting.md)) |
| **Registration must close before Running** | `Draft → Running` would generate a bracket from a field that was never assembled or seeded ([023](023-bracket.md)) |
| **Reopening is allowed** | Organisers close at seven teams and want an eighth. Forbidding it pushes them to cancel and recreate — losing the URL already pasted into Discord ([018](018-frontend-architecture.md)) |
| **Completed and Cancelled are terminal** | Results have already moved ratings ([025](025-rating-system.md)). Unwinding them is a different operation, not a status change |

The state machine is a **pure function**, for the same reason the bracket and the ratings are:
an illegal transition does not throw or look wrong — it produces a tournament in a state
nobody designed for, and the damage surfaces later as a bracket built from a roster that was
still moving. The UI only renders buttons for legal transitions, but **the buttons are not the
enforcement** — a hand-typed URL gets the same answer.

---

## Registration

### A team must be able to field the format

**The rule that connects [045](045-teams-and-rosters.md) to tournaments.** A tournament is
1v1, 2v2 or 5v5; a team may enter only if it has that many **fieldable** members.

```
fieldable = accepted members whose slot is Player or Substitute
            (coaches excluded — 045)
```

A team of three entering a 5v5 is **a forfeit that has not happened yet**, and a forfeit
discovered at match time costs the opponent their evening, not just a result. Counting
*members* rather than *fieldable* members would get this wrong in the specific case that
matters: four people, two of them coaches.

The team page keeps showing the count afterwards, because a roster that drops below the format
size **after** entering is the same problem arriving later.

### Entering is a captain's act, not an organiser's

`ManageRoster`, not `ManageTournament` ([036](036-authorization.md)). The captain is committing
their team's time; the organiser is not committing it for them.

### Withdrawal is soft, and only before the bracket exists

- `withdrawn_at`, never a delete — a team that entered is part of that event's history.
- The partial unique index ignores withdrawn rows, so a team can re-enter while registration
  is open, exactly as a player can rejoin a team they left ([045](045-teams-and-rosters.md)).
- **Once the tournament is Running the field is frozen.** A team vanishing then would leave
  matches pointing at a team that is not in the event ([023](023-bracket.md)). Withdrawing from
  a running tournament is a *forfeit* — a different operation, with a result attached.

### A registration cap exists

An uncapped community cup that attracts three hundred teams is not a nice problem to have; it
is an event nobody can run.

---

## Seeding

**Seeds are assigned when registration closes**, because seeding needs the whole field to exist
before it can be ordered. Until then the column is null and the page shows a dash — an honest
"not yet" rather than a placeholder number.

### Strongest first, provisional teams last

```
1. settled ratings, highest first
2. provisional and unrated teams
3. ties: earlier registration, then team id
```

**A provisional team is not seeded on its rating even when that rating is high**
([025](025-rating-system.md)). Two lucky wins would otherwise buy a top seed and an easy path —
the same "played two matches, ranked #1" problem the ranking page already refuses.

### Determinism is the requirement, not fairness

Seed 1 and seed 2 start in opposite halves ([023](023-bracket.md)), so seeding decides whether
the two strongest teams meet in the final or in round one. Getting it wrong breaks nothing
visible — it just quietly ruins the event.

The tie-break on registration time is arbitrary but **total and stable**. Seeds are also
**written to the database**, not computed on read, so a bracket an organiser has already shown
people cannot reshuffle because somebody's rating moved overnight in another organiser's event.

**Any change to the field clears the seeding** — a withdrawal, or reopening registration. A
stale seed describes a field that no longer exists.

---

## The tenancy problem this slice created

Tournaments are **tenant-scoped**, and the query filter *throws* when there is no organiser in
scope ([036](036-authorization.md)). But the tournament list and page are **public**, and a
visitor has no organiser.

The escape hatch 036 already permits — explicitly named methods calling `IgnoreQueryFilters()`
— is now a single file, `PublicReads.cs`:

| Method | Returns |
| --- | --- |
| `PublicTournaments()` | every status **except Draft** |
| `PublicRegistrations(id)` | entries in **one** tournament, scoped to an id so it cannot be called without one |
| `FindForManagementAsync(id)` | includes drafts, and is named to make an unguarded call obvious in review |

**The bar for anything in that file:** not "does this work", but *"would I be happy for anyone
on the internet to see everything this returns"*.

Writes go the other way: a handler resolves the tournament, asks `AccessPolicy`, and only then
sets the tenant — so the scope of the write is the same fact the policy checked, never
something the form said.

---

## Editing and templates

Tournament setup remains editable without changing the permanent tournament id or URL. A
draft may change all executable setup. Opening registration freezes game, format and team size
because teams have committed to that event shape; name, description, date, season and capacity remain
editable until start. Capacity can never fall below the active field. Running, completed and
cancelled setup is immutable history.

Organisers may save the repeatable setup as a named, tenant-scoped template and clone it into
another private draft ([015](015-admin-panel.md)). Templates are value snapshots, not inherited
configuration: they do not copy the event name, date or season and retain no link to either source or
clone.

Registration, withdrawal, editing and status transitions all take a row lock on the same
tournament. This makes the lifecycle checks atomic with their mutations and also makes bracket
generation genuinely part of the transaction that changes the status to `Running`.

---

## Season assignment

Season membership is optional because a community cup may be independent of platform standings.
Fresh create forms default to the currently active season, while organisers may explicitly choose
another non-closed season or “outside a season”. A submitted id is checked against live season rows
and backed by a foreign key; a hand-written Guid cannot create a dangling archive membership.

When a tournament has a date, that date must fall within the selected season's window. Assignment
may change with other descriptive setup until start. Once a season is closed, existing membership
is frozen and no new tournament may be assigned or started inside it. Otherwise a new result could
silently change standings [021](021-site-structure.md) promises never to recompute.

The assignment appears on the public tournament list and detail page. Templates omit it because a
season is absolute event context, like the date, not reusable competitive setup.

---

## Check-in and event forfeits

> **Amended 2026-08-11 — check-in no longer blocks.** The original design required every captain
> to confirm before a tournament could start. Organiser interviews named the wound precisely —
> *"organization: a lot of delays, manual work, no free time"*, 3–5 people per event — and a gate
> that makes an organiser chase sleeping captains is the labour this product exists to delete
> (CONTEXT §3 rule 7). **Three layers replace one gate**, and no human action is required at any
> point; every action that *is* taken adds information.

Closing registration opens the check-in window, and it resolves in three layers:

| Layer | When | Who acts | Meaning |
| --- | --- | --- | --- |
| **1 — automatic** | On registration close | **Nobody** | Every entrant that can still field the team size is **checked in by default** |
| **2 — explicit** | Any time before start | A captain, optionally | A stronger signal, and the moment to **set a lineup** other than the default ([050](050-match-results-and-ratings.md) §2). A captain who cannot play **withdraws** instead |
| **3 — presence** | At match time | The players, by connecting | **Ground truth.** A team absent past the grace window is forfeited automatically by the existing event-forfeit path |

Each layer is a fallback for the previous one failing to produce information, so the truth is
settled by what actually happens rather than by who remembered to click. Reopening registration
clears explicit confirmations, because the field may change; the automatic layer re-evaluates.

The start transaction still checks the facts that matter, and **layer 2 is no longer among them**:

1. at least two active entrants exist;
2. every entrant can still field the tournament's team size;
3. an assigned season has not been closed.

Any refusal leaves both lifecycle state and bracket rows untouched.

**The grace window is one configurable number**, defaulting to 15 minutes after the scheduled
start. It is the only new parameter this amendment introduces.

**What was given up:** a captain who knows in advance they cannot field a team previously said so
by *not* checking in; now they must actively withdraw. That is a fair trade — one announcement
("withdraw by Friday or you are in") replaces five individual chases, and the cost of being wrong
is a forfeit that the bracket already handles correctly.

The organiser view shows all three states per team — *automatic*, *confirmed*, *connected* — which
is strictly more information than the binary it replaces.

Once the bracket exists, leaving is an **event forfeit**, not pre-start withdrawal and not one
ordinary match loss. A captain may forfeit only their own team; organiser staff and referees may
record either side. The actor, timestamp and required human reason are stored on both the match
result and the registration. Exact-tag confirmation guards the destructive action.

The opponent advances without a score and the result is never rated. In double elimination the
loser route receives an explicitly resolved empty slot, so the forfeiting team cannot fall into
the lower bracket. When later routing leaves a match with one occupant it resolves as an unrated
bye; zero occupants cancel and propagate the vacancy. Plugin events take the same match lock and
acknowledge terminal events without overwriting the administrative result.

---

## Connected slice

Registration-open announcements are now implemented through [014](014-discord.md)'s durable
outbox. They are committed in the same locked lifecycle transaction, while network delivery is
decoupled and cannot roll back the status transition.

## The runnable checks

Pure (`TournamentChecks.cs`):

- the happy path is legal end to end, and **`Draft → Running` is not**
- `Completed` and `Cancelled` have **no way out**
- every status has a decided answer for public / accepts-entries / frozen
- start requires at least two checked-in, currently fieldable entrants
- a **provisional team never takes a top seed**, however high its rating
- seeding is **deterministic** — reordering the input does not reorder the seeds

End to end (`TournamentE2EChecks.cs`), against a real database:

- **a draft is invisible** to the public list, to strangers, and by direct id — but visible to
  its organiser
- an illegal transition is refused **when typed by hand**, not merely hidden
- a team with four members but only two fieldable **cannot enter a 5v5**
- entering is refused for anyone who is not the captain
- closing registration seeds every entrant, with **no duplicate seeds**
- withdrawing **clears the seeding**; a running tournament refuses withdrawal
- only the owning organiser can edit or manage templates
- drafts edit every setup field; publishing freezes competitive structure; starting freezes all setup
- template names are case-insensitively unique and clones are independent private drafts
- capacity never shrinks below the active field
- open, existent, date-compatible season assignments persist and are shown publicly
- closed, unknown and out-of-window seasons are refused; the database rejects a dangling id
- a closed season blocks tournament start without creating a bracket
- start is refused for unchecked or newly incomplete rosters without creating a bracket
- only each team's captain may check it in
- a captain cannot forfeit an opponent by changing the submitted team id
- a forfeit is audited and scoreless, advances the opponent, and changes no rating

Persistence (`BracketPersistenceChecks.cs`), against real routing rows:

- an event forfeit propagates a resolved vacancy through double elimination
- the exiting team never appears downstream; a lone surviving team receives an unrated bye
