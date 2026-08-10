# 015 — Admin panel & CMS

**Status:** decided
**Date:** 2026-08-10
**Decision:** **Fixed formats with parameters, plus clonable templates.** No rule engine.

---

## The line: what is data, what is code

This is the decision that keeps the panel finishable.

| Configurable per tournament (**data**) | Fixed in code |
| --- | --- |
| Format — single elim, double elim, round robin, groups, league | **Rating maths** ([025](025-rating-system.md)) |
| Team count, seeding method | **Integrity gates** ([035](035-match-result-integrity.md)) |
| Bo-N per stage (e.g. Bo1 groups, Bo3 from quarters) | **Bracket routing** ([023](023-bracket.md)) |
| Map pool | **Two-factor binding** ([011](011-accounts-and-login.md)) |
| Veto sequence — **chosen from presets**, not authored | Tenancy scoping ([036](036-authorization.md)) |
| Tiebreak chain — **chosen from presets** | |
| Schedule, prize text, rules text, registration window | |
| Server assignment, Discord webhook ([014](014-discord.md)) | |

### Why not a rule engine

> *Plain version:* letting organisers define veto sequences and tiebreakers as free-form data
> means building a small configuration language — with its own validation, its own error
> messages, and its own bugs. Worse, bugs become unreproducible: *"it works in the other
> tournament."* Every tournament would run a slightly different code path.

With fixed formats, **every tournament runs the same tested code**. The cost is that an
exotic format requires us to build it — which is the right trade, because an exotic format
*should* be a deliberate decision, not something an organiser can accidentally create at
2am before an event.

Veto and tiebreak are offered as **presets** because they genuinely vary between communities
— but a preset is a tested named option, not authored logic.

## Templates

Named starting points that clone into a new tournament:

```
SZABLONY
  ▸ Puchar 16   · double elim · Bo1 → Bo3 od ćwierćfinału
  ▸ Liga 10     · round robin · Bo1 · sezon
  ▸ Mix 8       · single elim · Bo1
  ▸ Grupy 16    · 4×4 round robin → single elim Bo3
  [ klonuj i zmień ]
```

Same engine underneath — purely a setup shortcut. Two real benefits: a tournament takes
three clicks instead of twelve, and organisers stop making poor format choices by accident.

## Panel scope

### Organiser panel — `/org/*` (tenant-scoped, [036](036-authorization.md))

| Section | Contents |
| --- | --- |
| **Tournaments** | Create from template, seed teams, open/close registration, start, monitor |
| **Matches** | Live state, referee actions, **resolve disputes** ([035](035-match-result-integrity.md)) |
| **Servers** | Register, verify by challenge-response, health, plugin version ([007](007-server-hosting-model.md)) |
| **Staff** | Assign organiser_admin / referee roles |
| **Integrations** | Discord webhook ([014](014-discord.md)) |

**The most important screen is disputes**, because it is where a human decision is required
before a bracket advances — and the one place a wrong click has permanent consequences.

### Staff panel — `/admin/*` (platform-wide)

| Section | Contents |
| --- | --- |
| **Users** | Moderation, bans, account merges |
| **Organisers** | Approve, set the `trusted` flag (CONTEXT §3b) |
| **News** | The CMS — see below |
| **Audit log** | Read-only, append-only ([036](036-authorization.md)) |
| **System** | Maintenance mode with a **live-match count** ([033](033-maintenance-mode.md)) |

**Staff cannot read RCON credentials, `setinfo` tokens or webhook URLs**
([007](007-server-hosting-model.md), [012](012-security.md)). The panel shows *status*, never
secrets — which removes both insider risk and the temptation.

## The CMS, scoped honestly

The original brief asked for a CMS. What it actually manages:

| Content | Editable |
| --- | --- |
| News / announcements | ✅ ([021](021-site-structure.md)) |
| Static pages — rules, FAQ, privacy | ✅ |
| Map pools | ✅ |
| Tournament rules text | ✅ per tournament |
| Email/Discord message templates | ✅ |
| **Everything else** | ❌ Code |

**Not a page builder.** No drag-and-drop layout, no arbitrary components. Content is
**sanitised markdown, never raw HTML** ([040](040-threat-model.md) §5), localised across four
locales with Polish fallback ([018](018-frontend-architecture.md)).

> `ponytail:` markdown fields on fixed page templates. Ceiling: no novel page layouts without
> a deploy. Upgrade path: none planned — a page builder is a product in itself, and this one
> has enough products in it already.

## Design

Sidebar navigation ([021](021-site-structure.md)), same palette and tokens as the public site
([019](019-visual-identity.md)) — a denser rhythm, not a different design system.

**Flagged for prototyping** ([019](019-visual-identity.md)): dense forms and tables are the
place a system built for content pages is most likely to break.

## Not in the panel

**Deploy controls.** Deliberately excluded — a web panel that ships code is remote code
execution as a feature ([017](017-deployment-and-cicd.md)). A read-only "what's running"
dashboard is the acceptable version, if it's wanted later.
