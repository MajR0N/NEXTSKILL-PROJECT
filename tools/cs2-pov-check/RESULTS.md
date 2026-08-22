# CS2 / CS:GO POV recording — check sheet

Answers **V15's CS2 half**. There is deliberately **no plugin here**, and the reason is the whole
point of the sheet:

> **V15 has two halves, and they are not equal.** *Does `record` work at all in CS2 today?* and
> *can the server force it?* The first needs **no server and no code** — one client, one console
> command, five minutes. The second needs a CounterStrikeSharp plugin that does not exist until
> Phase 6.
>
> **If `record` still freezes the client, the forcing question is moot.** So test the free half
> first, and only build for the second if the first survives.

`ponytail:` no CSSharp scaffold until Phase 6 needs one anyway. Ceiling: this cannot answer the
forcing question. Upgrade path: one `ClientCommand` call inside the plugin we are already going to
write.

---

## Half 1 — does `record` still freeze CS2? — **RUN 2026-08-22**

> **Result: it works.** `record` produced a demo on a **dedicated server**; the client **froze for
> 1–2 seconds and recovered**; recording stopped and the file saved.
>
> **The 2023 claim is overstated.** *"`record` will freeze your game most of the time"* described a
> hang. What happens is a **stall that resolves**. The feature is usable.

| Observation | |
| --- | --- |
| Demo produced | **yes** |
| Client froze | **1–2 s, then recovered** — a stutter, not a hang |
| Stopped and saved | **yes** |
| Environment | **dedicated server** — the one that matters, not a listen server |

### Noted during the run

`sv_cheats 1` was needed during an earlier **bot game on a local server**. Almost certainly the bot
setup, not `record` — the dedicated-server run then succeeded, and a dedicated server runs
`sv_cheats 0`. **Treated as answered by that**, but if anyone sees `record` refused on a
`sv_cheats 0` server, reopen it: `record` requiring cheats would kill the CS2 appeal path outright,
because `sv_cheats` cannot be on during a real match.

### Still untested

- ☐ **`playdemo` on the saved file** — a demo a referee cannot open is worth nothing. **Do this one**
- ☐ Whether the 1–2 s stall is worse mid-round than at freezetime
- ☐ CS2 build number at time of test *(record it — a Valve update can reverse this)*

### What it changed in the design

**1–2 seconds is not free.** [058](../../docs/decisions/058-what-we-sell.md) §6c armed recording at
the instant the suspicion meter crossed, which can land mid-round. A stall there costs a duel, and
**most flagged players are innocent** — the monitoring phase is meant to resolve quietly. So:
**recording is now issued at the next freezetime, not on the flag.**

The stall is also a **felt** tell, not just a visible one. That sharpens the evasion loop already
recorded in §6c, and makes its countermeasure matter more: **a meter that collapses the instant
recording starts is itself scored.**

---

## Half 2 — can the server force it? *(Phase 6)*

Only run this once half 1 shows `record` is usable.

**The known obstacle:** `FCVAR_SERVER_CAN_EXECUTE` gates which commands a server may execute on a
client. The published CS2 ConVar dump shows `_record` carrying only
`FCVAR_DONTRECORD | FCVAR_RELEASE` — **no server-execute flag** — which is suggestive but not
conclusive, since `record` itself was not visible in the dump.

| | |
| --- | --- |
| **Mechanism to try** | The CounterStrikeSharp equivalent of `ClientCommand(client, "record …")`. On CS:GO the SourceMod form is documented to need a **timer — it does not work during connect** |
| **Expected failure** | The command is refused and the server console reports it as blocked |
| **If it is blocked** | **Not a dead end.** [058](../../docs/decisions/058-what-we-sell.md) §6c already covers it: *ask* instead of forcing, and a missing recording is **refusal, never clean.** Weaker, still a signal |
| **If it works** | Record which CS2 build, because this is exactly the kind of thing a Valve update reverses ([002](../../docs/decisions/002-cs2-plugin.md) Tier 4) |

---

## Verdict

| | Verdict | Evidence |
| --- | --- | --- |
| **`record` usable in CS2, 2026** | ☑ **yes** — 1–2 s stall, recovers, file saves | Run 2026-08-22, dedicated server |
| **Server can force it** | ☐ not tested — **Phase 6** | Needs the CSSharp plugin |

**CONTEXT §2.9 updated 2026-08-22.** It no longer records the CS2 state as unknown: the freeze
claim survived two rounds of desk research and died in five minutes of testing. Which is the
lesson worth keeping — **the web on this question is one blog post from November 2023 wearing
different hats.**
