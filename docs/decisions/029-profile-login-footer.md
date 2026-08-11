# 029 / 030 / 031 — Profile, login & footer

**Status:** decided
**Date:** 2026-08-10

Three small page specs, kept together because each is short and they share one property:
**they are solved patterns.** They will be built directly in Razor without prototyping
([019](019-visual-identity.md)) — nothing here can surprise us.


> **Built (2026-08-11):** login, the footer, public `/players/{id}` profiles and private
> `/me`. Public profiles show per-game rating and uncertainty, accepted team history,
> linked-account badges without provider IDs, and live LFT state. The public account query
> is an explicit allow-list and is checked not to expose email, IP, token hashes or provider IDs.
> `/me` also rotates the CS 1.6 `setinfo` token: a copy-ready command is shown once, only the
> hash is stored, and the page states plainly that the token is readable by servers and useless
> without the per-match code. Steam and Discord can be linked and unlinked only through their
> verified provider flows; typed external IDs are never accepted. The security rail reports the
> active-session count and exposes a CSRF-protected sign-out-everywhere action that revokes every
> server-side session before clearing the current cookie. Account erasure requires the current
> password and exact nickname, explains that match history survives anonymously, destroys private
> data and live state, and leaves historical links pointing to a plain anonymous tombstone.
>
> **Still owed by their own slices:** persisted locale and export.
> Recent player matches and achievements need an
> immutable participant snapshot; current roster membership cannot honestly reconstruct them.

---

## 029 — Profile

### Public profile — `/players/{id}`

| Section | Contents |
| --- | --- |
| Header | Generated avatar ([019](019-visual-identity.md)), nickname, country/city, linked Steam/Discord badges |
| **Ratings** | **One per game** — CS2 and CS 1.6 never mix ([025](025-rating-system.md)). Each with ±RD and confidence bar |
| Teams | Current and past, with dates |
| Recent matches | Result, map, opponent, link |
| Achievements | Tournament wins, finals reached |
| LFT status | If listed, links to their entry ([021](021-site-structure.md)) |

**Never shown publicly:** email, IP, `setinfo` token, link codes, any credential
([012](012-security.md)).

**Provisional players** show their rating marked provisional rather than a rank
([025](025-rating-system.md)) — the same honesty as the ranking table.

### Own profile — `/me`

Adds what only the owner may see or do:

| Item | Note |
| --- | --- |
| **`setinfo` token** | Shown with copy-ready `setinfo` line, plus the plain explanation of why it is not a password ([011](011-accounts-and-login.md)) |
| **Rotate token** | One click. **There is no "reveal existing token" — you rotate, never retrieve** |
| Linked accounts | Link/unlink Steam and Discord ([011](011-accounts-and-login.md)) |
| Locale | Overrides `Accept-Language`, persists ([018](018-frontend-architecture.md)) |
| Security | Change password, active sessions, sign out everywhere |
| Data | Export, and account deletion — which **anonymises the player and keeps the matches** ([012](012-security.md)) |

The deletion note must be stated *on the page*, not just in the privacy policy: a player
deserves to know that their matches survive, because other people's ratings depend on them.

---

## 030 — Login & register

Native account is primary; Steam and Discord are optional **links, not login providers**
([011](011-accounts-and-login.md)).

| Rule | Detail |
| --- | --- |
| Register | Email, password, nickname. Nickname charset restricted ([040](040-threat-model.md) §11) |
| Password | Argon2id. Strength shown, minimum enforced ([011](011-accounts-and-login.md)) |
| **Errors reveal nothing** | "Nieprawidłowy e-mail lub hasło" — never which one. Same for reset: identical response whether the address exists ([040](040-threat-model.md) §13) |
| Rate limited | Per account **and** per IP ([012](012-security.md)) |
| CSRF | Token on every form ([012](012-security.md)) |
| Session | Cookie rotated on login and on privilege change |
| **Redirect after login** | **Relative paths only.** `//evil.com` and absolute URLs rejected ([040](040-threat-model.md) §3) |
| Works without JS | It is a form ([020](020-motion-and-ux.md)) |

**Registration must explain the `setinfo` step for CS 1.6 players** — not as a wall of text,
but as one honest line with a link. A player who registers and then cannot play has had a
worse experience than one who was told up front.

---

## 031 — Footer

Deliberately plain. It carries obligations and orientation, not marketing.

```
NEXTSKILL                    TURNIEJE          SPOŁECZNOŚĆ
Platforma turniejowa         Ranking           Discord
dla sceny CS 1.6 i CS2       Turnieje          Forum
                             Szukam drużyny    Regulamin
                             Archiwum sezonów  FAQ
─────────────────────────────────────────────────────────
Prywatność · Warunki · Kontakt      PL EN RU DE      v1.4.2
```

| Element | Why it's there |
| --- | --- |
| **Privacy & terms** | GDPR obligation, not decoration ([012](012-security.md)) |
| **Locale switcher** | Second place after the header — the one a Russian speaker scrolls to find ([018](018-frontend-architecture.md)) |
| Contact | Dispute escalation and abuse reports need a route |
| **Version number** | Makes "which version were you on?" answerable in support, and confirms deploys ([017](017-deployment-and-cicd.md)) |
| Discord / forum links | Where the community actually is ([021](021-site-structure.md)) |

**Not in the footer:** newsletter signup, social icon row, cookie banner theatre, "made with
love". The footer is where obligations live, and padding it with marketing buries them.
