# 018 — Frontend architecture

**Status:** **decided**
**Decision:** **Razor Pages (server-rendered, 4 locales) + TypeScript islands** for bracket,
map veto/lobby and live match views.
**Date:** 2026-08-10
**Lenses applied:** architecture-designer, frontend-design

---

## What NEXTSKILL's frontend actually is

Before comparing anything — the page inventory, because the ratio decides the argument.

| Type | Pages | Share | Nature |
| --- | --- | --- | --- |
| **Content** | Profiles, team pages, rankings, tournament info, match history, match detail, footer/static | **~80%** | Read-heavy, **should be publicly indexable**, low interactivity |
| **Interactive** | Map veto / lobby, live bracket, live match view | ~10% | Real-time, multi-user, genuinely app-like |
| **Forms & tables** | Login, register, team management, admin panel | ~10% | Ordinary CRUD |

**Most of NEXTSKILL is a content site with a few interactive corners.** Not an application
with some content pages. That asymmetry is the whole decision.

---

## Distribution reality

NEXTSKILL's audience is **not** on one platform:

| Channel | Role |
| --- | --- |
| **Discord** | Where tournament organisation happens, alongside the web panel |
| **Forums** | Where the community also lives — announcements, discussion, recruitment |
| **Anywhere else** | The stated goal is to **reach as many communities as possible** — Steam groups, Facebook groups, other Discords, other forums |

**Growth happens through links pasted into other people's spaces.** That makes how a
NEXTSKILL link *renders elsewhere* a growth mechanism, not a cosmetic detail.

### Languages: Polish (primary), English, Russian, German

**Four locales from the start.** `pl` is primary; `en`, `ru`, `de` supported.

This is a real requirement, not speculative, so the machinery is built in — using
**ASP.NET Core's built-in localisation** (`IStringLocalizer` + `.resx`). No third-party
i18n library needed; it's part of the framework.

| Concern | Decision |
| --- | --- |
| **URL structure** | Locale segment: `/pl/…`, `/en/…`, `/ru/…`, `/de/…`. **Decided now because URLs are effectively permanent** — links pasted into Discord and forums live forever |
| `hreflang` tags | Every page declares its alternates, so search engines serve the right language |
| `og:locale` | Set per page, so link previews render in the right language |
| Locale detection | `Accept-Language` on first visit → redirect to the matching locale; user choice persists in a cookie and always wins |
| **User-generated content** | **Never translated.** Team names, nicknames and tournament descriptions stay as written. Only *our* UI strings are localised |
| Dates, numbers | Formatted per locale. `timestamptz` in the database ([008](008-database.md)), localised on render |

### Two consequences worth flagging early

**1. This strengthens the server-rendering argument.** Per-locale URLs, `hreflang`, and
localised `og:` meta tags are trivial with server-rendered HTML. In an SPA they require SSR
to work correctly for crawlers — the same crawlers that don't run JavaScript.

**2. It constrains the design decisions coming next** (typography, layout):

| Constraint | Why |
| --- | --- |
| **Fonts must cover Latin Extended *and* Cyrillic** | Polish diacritics (ą ć ę ł ń ó ś ź ż), German umlauts (ä ö ü ß), and Russian (Кириллица). Many display fonts have no Cyrillic at all — this eliminates a large share of font choices |
| **Layouts must tolerate ~30% longer strings** | German is notoriously long. Fixed-width buttons and single-line labels break |
| No text baked into images | Untranslatable |

> `ponytail:` framework-native localisation, four `.resx` files, locale in the route.
> Ceiling: no translation workflow tooling. Upgrade path: only if the number of languages or
> contributors grows enough to need one.

## The fact that decides it: link previews everywhere

**Someone will paste a tournament or profile link into Discord or a forum thread,
constantly.** For that link to show a preview card — title, team names, thumbnail — the
crawler must read meta tags from the HTML.

**Discord's crawler does not execute JavaScript.** Neither do Twitter/X, Facebook,
Slack, or most link unfurlers.

A plain SPA returns `<div id="root"></div>` with empty meta tags. Every link shared in the
community's primary channel would render as a bare URL.

Google *does* execute JavaScript now, so classic SEO survives an SPA. **Social unfurling
does not.** For a community platform whose growth happens through links pasted in Discord,
that is a product problem, not a technical footnote.

Any option chosen must produce **real HTML with real meta tags** for public pages.

---

## The three options

### A. Razor Pages + TypeScript islands

ASP.NET Core renders content pages server-side. TypeScript is added only where interactivity
genuinely requires it.

```
Razor Pages (C#)          TypeScript islands
─────────────────         ──────────────────
profiles, teams           bracket rendering
rankings                  map veto / lobby
tournament pages          live match (SSE)
match history
login / register
admin panel
```

| | |
| --- | --- |
| **For** | SEO and unfurling work with **zero effort** — it's real HTML. No API round-trip for page content. **Two languages total** (Pawn + C#) across most of the app. Far less JavaScript to write. No second runtime to deploy ([017](017-deployment-and-cicd.md)). Forms and tables are Razor's home ground |
| **Against** | Two rendering models to hold in your head. Less "app-like" feel. Islands need a small build step anyway. **Much of [039](039-public-api.md)'s public API becomes unnecessary** — only the interactive islands need JSON endpoints |
| **Learning** | Teaches HTTP, HTML, CSS, forms and progressive enhancement — **fundamentals**, plus focused modern JS where it's warranted |

### B. React SPA + REST API

Everything in TypeScript, consuming [039](039-public-api.md) as designed.

| | |
| --- | --- |
| **For** | One frontend language and one mental model. Largest ecosystem and the most examples for anything you'll want to build. Genuinely app-like interactions. Uses the API design already written. Most transferable skill on a CV |
| **Against** | **Empty HTML by default — breaks Discord unfurling and weakens SEO.** Fixing it means SSR (Next.js), which adds **a Node runtime alongside .NET** — a second thing to deploy, monitor and keep alive ([017](017-deployment-and-cicd.md)). Every page is an API round-trip. The most JavaScript to write, while also learning C# |
| **Learning** | Teaches the dominant industry stack — but layers React's model on top of web fundamentals rather than teaching them |

### C. Blazor (C# everywhere)

| | |
| --- | --- |
| **For** | Language unity taken to its conclusion: **Pawn + C# only**. Shares `Contracts` types directly ([004](004-stack.md)). No JS build pipeline |
| **Against** | **WASM:** multi-MB initial download; SEO and unfurling need prerendering anyway. **Server:** a persistent SignalR circuit **per user** — which directly fights the SSE decision ([039](039-public-api.md)) and worsens connection exhaustion ([040](040-threat-model.md) §8). Smallest ecosystem of the three |
| **Learning** | **Weakest fit for the stated goal.** Blazor teaches you Blazor. It deliberately abstracts away HTTP, HTML and JavaScript — the things "learning web development" means |

> Blazor Server's per-user circuit is not a small objection. [039](039-public-api.md) chose
> SSE specifically to avoid persistent per-user connections and a Redis backplane. Blazor
> Server reintroduces exactly that, for **every page**, not just live ones.

---

## Comparison

| | A. Razor + islands | B. React SPA | C. Blazor |
| --- | --- | --- | --- |
| **Discord link previews** | ✅ free | ❌ needs SSR (+Node) | ⚠️ needs prerendering |
| SEO | ✅ free | ⚠️ JS-dependent | ⚠️ |
| Languages in project | **3** (mostly 2) | 3 | **2** |
| Second runtime to deploy | ✅ none | ❌ Node, if SSR | ✅ none |
| Fits SSE choice ([039](039-public-api.md)) | ✅ | ✅ | ❌ Server mode conflicts |
| Interactive veto/bracket | ✅ islands | ✅ native strength | ✅ |
| Ecosystem | Good | ✅ **largest** | Smallest |
| JS to write | **Least** | Most | None |
| **Teaches web fundamentals** | ✅ **most** | Partly | ❌ least |
| Transferable skill | Moderate | ✅ **highest** | Lowest |

*(SvelteKit/Nuxt belong in category B with SSR built in — same trade-offs, gentler curve,
smaller ecosystem, still a second runtime.)*

---

## Recommendation: A — Razor Pages + TypeScript islands

1. **80% of the site is content**, and content is exactly what server rendering is best at.
2. **Discord unfurling works for free** — decisive for a community that grows through shared
   links.
3. **No second runtime.** [017](017-deployment-and-cicd.md) deploys one container; SSR React
   would add a Node process to run, monitor and patch.
4. **Least code.** You'd write TypeScript for three features instead of forty pages, while
   simultaneously learning C#.
5. It teaches **web fundamentals** — HTML, HTTP, forms, progressive enhancement — which is
   what "learning web development" actually means. Modern JS still gets learned, on the
   parts that genuinely need it.

**The honest case for B:** React is the industry default and the most transferable skill
here. If the goal is employability as much as shipping, that argument is real — and
SvelteKit/Nuxt would give SSR without hand-rolling it, at the cost of a Node runtime.

**C is not recommended.** Blazor Server's per-user circuit contradicts a decision already
made, and Blazor teaches the least about the web.

## Consequence if A is chosen

**[039](039-public-api.md) shrinks.** The public REST API stops being the primary UI
transport and becomes a small surface for the interactive islands (bracket, veto, live) plus
any future public/third-party use. The plugin contract ([009](009-api-contract.md)) is
unaffected.

## Decision

**Razor Pages + TypeScript islands.**

```
Razor Pages (C#)                 TypeScript islands
/pl/ /en/ /ru/ /de/              ──────────────────
  profiles, teams                  bracket rendering
  rankings, tournaments            map veto / lobby
  match history & detail           live match view (SSE)
  login, register, admin

Localisation: IStringLocalizer + .resx (framework-native)
Runtime:      one - .NET only. No Node in production.
```

### Consequence: [039](039-public-api.md) shrinks

The public REST API is **no longer the primary UI transport**. Content pages are rendered
server-side and fetch nothing. The API narrows to:

| Still needed | No longer needed for the UI |
| --- | --- |
| Endpoints the TS islands call (bracket, veto, live) | Endpoints that only existed to render content pages |
| SSE streams ([039](039-public-api.md)) | Full CRUD surface for every resource |
| Future third-party/public API use | NSwag TS generation for the whole app — only for island endpoints |

The plugin contract ([009](009-api-contract.md)) is **completely unaffected**.

### Consequence: design constraints are now fixed

Carried into the visual design decisions (019 typography, 020 layout):

- **Font must cover Latin Extended + Cyrillic** — this eliminates most display/gaming fonts
- **Layouts must tolerate ~30% longer strings** (German)
- **No text baked into images**
- Progressive enhancement: content pages must work before any JS loads
