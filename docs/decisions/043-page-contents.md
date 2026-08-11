# 043 — Page contents (Phase 4 grilling)

**Status:** **decided** — Phase 4 page contents settled
**Date:** 2026-08-10

[021](021-site-structure.md) settled *which pages exist*; [041](041-layout-system.md) settled
*how a page is composed*. This settles **what is actually on them**, which
[BUILD-PLAN](../BUILD-PLAN.md) flagged as blocking Phase 4.

---

## Home `/` — activity first, with announcements

### The reframe that decided it

**The home page is not the landing page.** Growth runs through links pasted into Discord and
forums ([018](018-frontend-architecture.md)), and those links point at a *match*, a
*tournament*, a *profile*. Almost nobody arrives at `/` from a shared link.

Who does land there:

- someone who heard the name and typed it
- someone clicking the logo from a deep page
- **an organiser deciding whether to trust us with their tournament**

That is an orientation job, not a conversion job. And it points at what actually kills
community platforms: **looking dead.** A tournament site whose front page shows last year's
event is one nobody joins.

### Layout

```
● NA ŻYWO   Stara Szkoła 7–5 Wataha · de_nuke · 12 runda
─────────────────────────────────────────────────────────
📌 OGŁOSZENIE   Puchar Zimowy — zapisy otwarte do 24.11

NAJBLIŻSZE TURNIEJE            OSTATNIE WYNIKI
▸ Puchar Zimowy · 16 drużyn    Pukawka 13–11 Nokaut
▸ Liga NEXTSKILL · kolejka 4   Husaria  6–13 Nokaut

TOP DRUŻYNY                    [ jednolinijkowy pitch dla gości ]
1 Pukawka Five  1847 ±32
```

| Block | Why it is there |
| --- | --- |
| **Live matches** | Answers "is anything happening here?" in under a second |
| **Pinned announcements** | The CMS from [021](021-site-structure.md). An announcement is proof of life as much as information |
| Upcoming tournaments | The action a visitor most likely wants |
| Recent results | Density; also the most shareable links |
| Top teams | A taste of the ranking without being the ranking |
| One-line pitch, logged-out only | A stranger still needs to know what this is — but it does not deserve the fold |

**Empty state matters more here than anywhere.** On day one there are no live matches. The
home page must look *early*, not *abandoned* — "pierwszy turniej wkrótce" with a real date
beats a blank panel ([020](020-motion-and-ux.md)).

---

## Notifications — in-app inbox + email for time-critical

### The gap this fills

[014](014-discord.md) chose webhooks deliberately, and webhooks post to **channels, not
people**. So "your match starts in 10 minutes" had no way to reach an individual. And the
no-show problem is not someone staring at the lobby — it is someone who never opened it.

| Channel | Carries | Why |
| --- | --- | --- |
| **In-app (bell)** | Team invites, roster changes, result confirmed, dispute raised, rating updated | Cheap: one table plus read state. Where a user looks when they are already here |
| **Email** | Match scheduled, **match starting soon**, password reset, account security | The only channel that reaches someone who is *not* looking — which is exactly the person who no-shows |
| Discord channel ([014](014-discord.md)) | Registration open, match starting, results, disputes | Broadcast to the community, not addressed to a person |

**Team invites need somewhere to live**, which alone rules out having no inbox.

### Consequences

- One `notifications` table: recipient, type, payload, `read_at`, created. Global, not
  tenant-scoped — a player may be invited by any organiser's team.
- **Email deliverability is real work** — SPF/DKIM/DMARC or a sending service. Budget for it
  rather than discovering it when reset emails land in spam.
- Notification text is localised ([018](018-frontend-architecture.md)); the *email* uses the
  recipient's saved locale, not the sender's.
- **Never put a secret in a notification** — no link codes, no `setinfo` tokens
  ([012](012-security.md)).

---

## Search — trigram, accent-insensitive

### The deciding detail: Polish diacritics

Someone typing `Stara Szkola` must find **Stara Szkoła**. Diacritics (ą ć ę ł ń ó ś ź ż) are
routinely dropped when typing quickly, and Russian users will type Latin approximations. A
search matching exact characters only would feel broken to the people it exists for.

### Trigram, not full-text

> *Plain version:* **full-text search** (`tsvector`) is built for *documents* — it stems
> words, ranks relevance, drops common words. **Trigram search** (`pg_trgm`) breaks text into
> three-character chunks and matches on overlap, so it tolerates typos and partial words.
>
> Team and player names are two words, not documents. Stemming does nothing useful for
> "Pukawka". Trigram is both lighter **and** better here.

```sql
CREATE EXTENSION pg_trgm;
CREATE EXTENSION unaccent;

CREATE INDEX ix_teams_name_trgm ON teams
  USING gin (unaccent(name) gin_trgm_ops);
```

| Searchable | Not searchable |
| --- | --- |
| Team names and tags | News article bodies (revisit if news volume grows) |
| Player nicknames | Match payloads |
| Tournament names | Anything tenant-scoped, which stays scoped ([036](036-authorization.md)) |

**Rate-limited and never revealing private data** — search must not become a way to
enumerate accounts ([040](040-threat-model.md)).

---

## Tournament `/t/{id}` — state-driven, bracket on its own route

### Two problems this shape solves

**A tournament page has three lives.** Before, during and after are genuinely different
pages wearing one URL:

| State | What the visitor wants |
| --- | --- |
| **Registration** | What is this, when, who is in, how do I join |
| **Running** | Bracket, live matches, who plays next |
| **Finished** | Who won, final standings, demos |

**And the bracket is a different archetype.** [041](041-layout-system.md) makes it *wide
canvas* — full-bleed with its own horizontal scroll — while the tournament page is *detail*
at 1120px. A bracket tab is therefore not just a tab; it is a different page shape.

```
/t/{id}            detail archetype, state-driven
  registration  ->  pitch, rules, registered teams, [ Zapisz druzyne ]
  running       ->  live matches, next fixtures, [ Zobacz drabinke ]
  finished      ->  mistrz, final standings, demos

/t/{id}/bracket    wide canvas, full bleed (023)
```

Both URLs are permanently shareable ([018](018-frontend-architecture.md)). The bracket link
in particular is the one that gets pasted mid-event.

---

## Onboarding — one signup, then intent

Everyone creates the same account. One screen after asks what brought them, and routes:

```
1. konto (email, haslo, nick)
2. "Co Cie tu sprowadza?"
   - Chce grac        -> LFT board / join a team
   - Mam druzyne      -> create team + invite
   - Organizuje       -> organiser setup, servers, plugin
3. setinfo token introduced ONLY when joining a CS 1.6 roster
```

**Why one account model:** an organiser is nearly always a player too. Separate signup paths
would produce either two accounts or an awkward merge later.

**Why `setinfo` arrives late:** explaining a config-file token to someone who just wanted an
account is noise — but a CS 1.6 player who learns about it at match time cannot play. Joining
a 1.6 roster is the moment it becomes both relevant and urgent, and it is where the honest
copy from [011](011-accounts-and-login.md) belongs: *not a password, useless alone,
revocable in one click.*

---

## NEXTSKILL as a first-party organiser

**The cold-start problem this fixes:** on day one no organiser is running anything, so a new
player picks "I want to play" and finds nothing to play in. The `/lft` board and the home
page activity feed are both empty. The platform is useless until the first *partner*
arrives, rather than the first *user*.

**NEXTSKILL runs its own open cups.**

### It costs almost nothing architecturally

[037](037-platform-hosting.md) already models organisers as tenants, so NEXTSKILL-as-organiser
is **an `organiser` row with `trusted = true`**. No new concept, no new code path, no
special case in the tenancy filters. The platform becomes its own first tenant.

### Servers: partner-donated, so [007](007-server-hosting-model.md) stands

First-party cups run on **servers partners lend**, flagged as available for open events.
NEXTSKILL still hosts zero game servers.

| | |
| --- | --- |
| Partner gets | Their name on a tournament that brings new players into their community |
| We get | Something to run without becoming a server operator |
| Risk | Dependent on goodwill; scheduling contention with their own events |

> `ponytail:` a flag on registered servers, nothing more. Ceiling: blocked if no partner has
> capacity. Upgrade path: rent one or two servers for first-party events only — a small
> amendment to [007](007-server-hosting-model.md), not a reversal.

---

## Remaining page defaults

These follow from decisions already made. Recorded so they are choices rather than
improvisations.

### Team `/teams/{id}`

The initial default below has now received its full grilling pass in
[049](049-team-page.md). That decision supersedes this short sketch with the game switcher,
history/integrity rules, rating trend, trusted trophies and recruitment lifecycle.

### LFT `/lft`

Shape already set by [021](021-site-structure.md). Additions:

- **Listings expire after 14 days**, with a one-click renew. A stale board is worse than an
  empty one — nothing kills an LFT board faster than messaging five people who all quit
  months ago.
- Rate-limited, moderated, `rel="nofollow ugc"` on anything user-supplied
  ([040](040-threat-model.md) section 9).

### News `/news`

Index and post. Pinned items surface on the home page. **Sanitised markdown, never raw
HTML** ([040](040-threat-model.md) section 5). Localised with **Polish fallback** rather than
an empty page ([018](018-frontend-architecture.md)).

### Seasons `/seasons/{n}`

Frozen standings, permanent URLs, never recomputed ([021](021-site-structure.md)). A later
result reversal does not rewrite history.

### Error pages

| Page | Must do |
| --- | --- |
| **404** | Offer a route onward — search, rankings, tournaments. Never a dead end |
| **500** | A reference id the user can quote, and **no stack trace** |
| **403 / banned** | Say what happened and how to appeal. A silent wall breeds support tickets |
| **Maintenance** | Static, separately hosted, so it survives the thing it reports on ([033](033-maintenance-mode.md)) |

All four are localised and work without JavaScript ([020](020-motion-and-ux.md)).
