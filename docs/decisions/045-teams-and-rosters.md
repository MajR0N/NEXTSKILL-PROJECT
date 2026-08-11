# 045 — Teams: roster size, editing, and disbanding

**Status:** **decided** — roster size settled by the tournament formats
**Date:** 2026-08-11
**Relates to:** [011](011-accounts-and-login.md) (locked rosters), [021](021-site-structure.md),
[022](022-tournament-formats.md), [036](036-authorization.md), [008](008-database.md)

---

## Tournament formats decide the roster size

**Tournaments run 1v1, 2v2 or 5v5.** That is the input everything below follows from, and it
was the missing piece: a roster cap argued in the abstract is arbitrary, but once the largest
lineup any event can field is five, the cap picks itself.

| | |
| --- | --- |
| **Team roster** | **max 5 active places** |
| Registered lineup | capped by the tournament, which may be lower (a 2v2 event does not want five names on a scoresheet) |
| Match lineup | exactly the format size, enforced by the plugin from a locked list ([011](011-accounts-and-login.md)) |

**Why not more than five.** There is no format that can field a sixth person. A sixth roster
place would be a place that means nothing — it would appear on the team page, count in no
lineup, and invite the question "why is this person here?" every time somebody reads it.

**The cost, stated plainly.** A five-cap means **no substitutes at the team level**. A team
whose player has a shift at work has to remove them and invite a replacement, and forfeits are
what kill community tournaments. The upgrade path is at the *tournament* level, not here: an
event that wants a bench sets its own registered-roster size above its match size. That keeps
substitutes an event rule, where they belong, instead of a permanent property of every team.

### Pending invites occupy a place

Non-obvious and load-bearing:

```
CanInvite(activeMembers, pendingInvites) => active + pending < 5
```

Counting accepted members alone would leave the cap unable to stop half of what it exists to
stop. **Five members plus two hundred outstanding invites** would pass a members-only check
while spamming two hundred people ([040](040-threat-model.md)). It also forces the honest
order of events: a full team frees a place *before* inviting, and the person leaving finds out
either way.

> `ponytail:` the cap is enforced in the handler, not by the database. Ceiling: two captains
> inviting at the same instant can put a roster one over. Upgrade path: a trigger or a
> serialisable transaction — not worth it for a limit whose exact edge nobody can perceive.

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

- the roster caps at five, and **an unaccepted invite still occupies a place**
- a stranger can neither open nor submit team settings
- a captain can rename and re-tag, and the team **id does not move**
- disbanding demands the exact tag, and **the row survives**
- a disbanded team leaves the directory, keeps its page, keeps its tag, and recruits nobody
