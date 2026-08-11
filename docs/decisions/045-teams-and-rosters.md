# 045 — Teams: roster size, editing, and disbanding

**Status:** **decided** — roster size settled by the tournament formats
**Date:** 2026-08-11
**Relates to:** [011](011-accounts-and-login.md) (locked rosters), [021](021-site-structure.md),
[022](022-tournament-formats.md), [036](036-authorization.md), [008](008-database.md)

---

## Roster shape: 5 + 3 + 2

**Tournaments run 1v1, 2v2 or 5v5**, so five is the largest lineup any event can field. A
bench and a coaching staff sit on top of that.

| Slot | Places | Plays? |
| --- | --- | --- |
| **Player** — the starting five | 5 | yes |
| **Substitute** — the bench | 3 | **yes** — a bench that cannot be fielded is not a bench |
| **Coach** — staff | 2 | **never** |
| | **10 total** | |

`MaxTotal` is *derived*, not enforced separately: the per-slot caps sum to exactly ten, so a
second total check could never fire and would be dead code wearing the costume of a safeguard.

### The slot is a second axis, not a wider role

```
TeamRole   Captain | Member        <- authority   (036)
RosterSlot Player | Substitute | Coach  <- what place they occupy  (045)
```

**A coach may be the captain; a starting player may have no powers at all.** Folding these
together would mean promoting somebody to captain silently put them in the lineup — a class of
bug that only surfaces at map start, when the wrong five are on the server.

### What makes a coach real rather than decorative

```csharp
IsFieldable(slot) => slot is Player or Substitute;
```

Without this, "coach" is a label on a player. **A lineup that could include the coach would be
a 5v5 played 4v5**, discovered when the match starts
([011](011-accounts-and-login.md), [035](035-match-result-integrity.md)). The team page states
it in words too — *nie gra w meczach* — because a name in a roster list reads as a player
unless something says otherwise.

Coaches are also **never rated**: a player rating is a claim about how somebody performs in a
server they were never in ([025](025-rating-system.md)).

### Caps are per slot, and pending invites occupy a place

```csharp
CanAdd(slot, occupied) => occupied < Capacity(slot);
```

Two consequences worth stating:

- **"The roster is not full" is not the same question as "there is room for another starter".**
  A team with five players, no substitutes and no coaches has five free places and cannot
  accept a sixth player.
- **An unanswered invite holds its place.** Counting accepted members alone would leave the cap
  unable to stop half of what it exists to stop: a full roster plus two hundred outstanding
  invitations would pass a members-only check while spamming two hundred people
  ([040](040-threat-model.md)).

The same cap applies to *moving* someone between slots. Six starters arrived at by promotion is
still six starters.

### Unknown slots get zero places

`Capacity()` falls through to **0** and `IsFieldable()` to **false**. A member added to the enum
later — Analyst, Manager — cannot be filled and cannot play until somebody decides, rather than
inheriting whatever the last branch happened to return. The completeness check in
[034](034-test-strategy.md) then fails the build until the decision is recorded.

### Moving between slots is the point of a bench

Benching a starter and fielding a substitute is a slot change, not a removal. Without it, a
player unavailable for one week would have to be removed and re-invited — which loses the
roster history and makes a one-week absence look like leaving the team.

---

## Editing a team

**Renaming and re-tagging are allowed, including after matches have been played.**

Brackets, results and ratings reference a team **by id**, never by name, so a rebrand cannot
corrupt history. Community teams rebrand constantly. A rule forbidding it would protect
nothing at the cost of something people actually do.

**Who:** the captain, through a new operation.

| Operation | Covers | Held by |
| --- | --- | --- |
| `ManageRoster` | invite, remove, promote | captain |
| **`ManageTeam`** | rename, re-tag, disband | captain |

Split rather than merged. They are the same authority *today*, but removing a player and
dissolving the team are not the same act — and the moment a manager-style role exists that may
shuffle a roster without being able to dissolve the team, the seam is already where it needs
to be. One enum value now beats untangling a merged rule later.

The completeness check in [034](034-test-strategy.md) enforces that every `Operation` is
grantable by somebody, so a new operation cannot be added and left unreachable.

---

## Disbanding

### Soft, always

```
teams.disbanded_at  <- set
teams row           <- NEVER deleted
```

Matches and ratings point at the team id. **Deleting the row would leave finished tournaments
referencing nothing** — history rewritten by a captain clicking a button months later. This is
the same call [008](008-database.md) already makes for people leaving a roster
(`left_at`, never a delete), for the same reason: *the past is not the captain's to edit.*

### What disbanding does

| | |
| --- | --- |
| Leaves the directory | it is not a team anyone can play against any more |
| **Keeps its page** | a link may be sitting in a Discord message from last season, and old brackets still name it ([018](018-frontend-architecture.md)) |
| **Keeps its tag reserved** | see below |
| Everyone leaves at that moment | so nobody holds captain rights over a dissolved team, and the roster stops claiming players who are free to join elsewhere |

**Why the tag stays reserved.** A tag is how a team is recognised in a bracket. Freeing it
would let a new team inherit a dead one's tag, and every old result would read as though it
belonged to somebody else. The unique index does this for free precisely *because* the row
survives.

### Confirmation is typing the tag

Not a yes/no dialog — those get clicked reflexively. Retyping the thing being destroyed is the
one pattern that reliably interrupts autopilot ([020](020-motion-and-ux.md)). This is the most
destructive action a captain has, and it is the only place in the product using this pattern
so far.

---

## Still open

- **Un-disbanding.** Currently impossible — a captain who dissolves by mistake needs staff.
  Nothing about the data model prevents adding it; it just has not been decided who may.
- **Team logos.** Monograms are generated from the tag ([019](019-visual-identity.md)).
  Whether uploads ever exist is a [044](044-visual-direction.md) question, and it brings image
  validation, storage and moderation with it.
- **Team page contents** — match history, rating trend, trophies. Flagged in
  [BUILD-PLAN](../BUILD-PLAN.md) as needing its own grilling.

## The runnable checks

All in `tests/NextSkill.Checks/TeamChecks.cs`, against a real database and the real pipeline:

- each slot caps separately (5 / 3 / 2), and **an unaccepted invite still occupies a place**
- a member can be moved between slots, and **the destination cap applies to a move**
- a coach is never fieldable, and the page says so in words
- a stranger can neither open nor submit team settings
- a captain can rename and re-tag, and the team **id does not move**
- disbanding demands the exact tag, and **the row survives**
- a disbanded team leaves the directory, keeps its page, keeps its tag, and recruits nobody
