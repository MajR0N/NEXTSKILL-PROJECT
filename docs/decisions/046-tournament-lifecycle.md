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

## Still open

- **Bracket generation and persistence.** `Running` is reachable and seeds exist, but the
  bracket is not yet written to `matches`. That is the next slice ([023](023-bracket.md)).
- **Forfeit as an operation** — the honest path out of a running tournament.
- **Tournament editing** after creation, and templates ([015](015-admin-panel.md)).
- **Check-in before start.** Seeds prove who registered, not who turned up.

## The runnable checks

Pure (`TournamentChecks.cs`):

- the happy path is legal end to end, and **`Draft → Running` is not**
- `Completed` and `Cancelled` have **no way out**
- every status has a decided answer for public / accepts-entries / frozen
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
