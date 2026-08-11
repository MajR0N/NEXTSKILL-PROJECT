# 021 — Site structure, features & navigation

**Status:** **decided** — structure, features and navigation pattern all settled
**Date:** 2026-08-10


> **Built (2026-08-11):** headbar, footer, ranking, teams, tournaments (including season assignment), bracket, account pages.
>
> **Not built:** home page, matches, LFT board, news/CMS, seasons archive, notifications bell,
> search, organiser panel, staff panel. The tables behind several of these exist; the pages do
> not.

---

## Four surfaces, matching the role model

Navigation mirrors [036](036-authorization.md) exactly, so menus and permissions cannot
drift apart. If a link is visible, the role behind it can use it.

```
PUBLIC (no login)                     PLAYER (logged in)
├─ /                  home            ├─ /me              profile, setinfo token
├─ /ranking           teams + players ├─ /me/teams        invites, roster
├─ /tournaments       list            ├─ /me/matches      upcoming, lobby
│  └─ /t/{id}         bracket, teams  └─ /me/settings     locale, links, security
├─ /matches
│  └─ /m/{id}         veto, demo      ORGANISER (tenant-scoped)
├─ /teams/{id}                        ├─ /org/tournaments create, seed, run
├─ /players/{id}                      ├─ /org/servers     register, health, verify
├─ /lft               free agents     ├─ /org/matches     referee, disputes
├─ /news              announcements   └─ /org/staff       roles
├─ /seasons/{n}       archive
└─ /rules, /faq                       STAFF (platform)
                                      ├─ /admin/users     moderation, bans
                                      ├─ /admin/orgs      trusted flag
                                      ├─ /admin/news      CMS
                                      ├─ /admin/audit     the log
                                      └─ /admin/system    maintenance mode
```

## Features added during grilling

### Free agent / LFT board — `/lft`

Players advertise as looking-for-team; teams advertise open slots.

**Why it earns its place:** finding a fifth is the single hardest problem in the community
scene, and it's why most rosters die. A tournament platform that helps teams *exist* is more
valuable than one that only runs their matches.

**The thing that makes ours better than a Discord message:** listings show **real rating with
uncertainty** ([025](025-rating-system.md)), not self-claimed skill. A `#szukam-teamu`
channel can't do that.

| Field | Source |
| --- | --- |
| Rating ±RD | [025](025-rating-system.md) — genuine, not self-reported |
| Games (CS2 / 1.6) | [001](001-game-scope.md) |
| Role, availability, region | Self-declared |
| Recent form | Match history |

Needs: expiry (stale listings are worse than none), rate limiting, and moderation
([040](040-threat-model.md) §9 — user-generated content is an SEO-spam surface).

### News / announcements — `/news`, managed in `/admin/news`

**This is what the CMS from the original brief actually manages.** Tournament announcements,
rule changes, results roundups.

- Localised across all four locales ([018](018-frontend-architecture.md)) — but a post may
  exist in fewer, falling back to Polish rather than showing an empty page
- Pinned announcements surface on the home page
- **News posts are the most shareable link type**, feeding directly into the
  Discord/forum growth loop ([018](018-frontend-architecture.md)) — so Open Graph cards matter
- Body is **sanitised markdown, never raw HTML** ([040](040-threat-model.md) §5)

### Seasons + archives — `/seasons/{n}`

Tournaments group into seasons; past seasons stay browsable with standings frozen.

**Already implied by decisions made earlier:** the league format ([022](022-tournament-formats.md))
needs a season concept, and "who won Season 2" must stay answerable forever.

| Question | Answer |
| --- | --- |
| Do ratings reset between seasons? | **No hard reset.** A soft RD widening at season start is the Glicko-2-native way to say "we're less sure now" ([025](025-rating-system.md)) — consistent with how roster changes are handled |
| Are old standings recomputed? | **Never.** Frozen at season close. A later result reversal does not rewrite history |
| Archive URLs | Permanent — links pasted into Discord and forums live forever ([018](018-frontend-architecture.md)) |

### Not taken

**Forum bridge / SSO and Twitch embeds** — declined for now. Forum SSO in particular is a
serious auth surface for a convenience feature ([040](040-threat-model.md)); revisit only if
the forum becomes a primary signup path.

---

## Navigation: headbar for public, sidebar for panels

**Decided.** The deciding argument is specific to this product.

### The bracket decides it

A double-elimination bracket is **the widest content in NEXTSKILL** — it scrolls horizontally
even on a large screen ([022](022-tournament-formats.md)). A persistent sidebar permanently
steals ~240px from the one page that needs every pixel it can get.

That alone rules out a sidebar on public pages.

### The two surfaces have genuinely different needs

| | Public | Organiser / Staff panels |
| --- | --- | --- |
| Destinations | ~8 | ~20, grouped |
| Visit pattern | Arrive from a link, read one page, leave | Log in, work across many screens |
| Content width | Wide — brackets, tables | Forms and tables; narrower is fine |
| SEO / unfurling | Critical ([018](018-frontend-architecture.md)) | Irrelevant |
| **Right pattern** | **Headbar** | **Sidebar** |

> *Plain version:* a **headbar** suits places you arrive at, read, and leave — it stays out
> of the content's way. A **sidebar** suits places you *work* in, where you move between
> twenty screens and want them all one click away. Most products need both, because they
> contain both kinds of place.

### Mobile

- Public: headbar collapses to a drawer. **Locale switcher stays visible**, not buried —
  a Russian speaker landing on a Polish page must be able to fix that immediately.
- Panels: sidebar collapses to a drawer.
- **The bracket gets its own treatment on mobile** — round-by-round paging rather than a
  scaled-down tree. Deferred to [023](023) as its own decision.

> `ponytail:` two nav patterns, one per surface. Ceiling: two components to maintain instead
> of one. Upgrade path: none needed — this is the standard split for a reason.
