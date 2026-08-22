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

> **Result: it works.** `record` produced a demo on a **loopback-hosted server**; the client **froze for
> 1–2 seconds and recovered**; recording stopped and the file saved.
>
> **The 2023 claim is overstated.** *"`record` will freeze your game most of the time"* described a
> hang. What happens is a **stall that resolves**. The feature is usable.

| Observation | |
| --- | --- |
| Demo produced | **yes** |
| Client froze | **1–2 s, then recovered** — a stutter, not a hang |
| Stopped and saved | **yes** |
| Environment | **loopback host** — the demo header reads `loopback:1`, so a listen server or a locally-run `srcds`. **Not a remote server under load.** See below |

### Noted during the run

**The demo header says `loopback:1`.** Inspected 2026-08-22 from `ns_test.dem` (2.6 MB, valid
`PBDEMS2` magic, so structurally sound — not truncated). `loopback` means the client connected to a
server **on the same machine**. That covers both a listen server *and* a locally-run dedicated
`srcds`, so it does not contradict the result — but it does mean the run was **not** against a
remote server with ten players and real network conditions.

> **What still stands:** `record` works, produces a valid file, stalls 1–2 s and recovers. That was
> the question, and it is answered.
>
> **What is not yet shown:** whether the stall is worse under load — ten players, real latency,
> a remote host. **Worth one repeat on a real server**, because 1–2 s is already enough to have
> moved the design, and a worse number would move it further.

`sv_cheats 1` was needed during an earlier **bot game**. Almost certainly the bot setup rather than
`record` — but note that a loopback host means `sv_cheats` *could* have been on for the recorded run
too. **The clean test remains: `record` on a server you do not host.** If `record` ever needs
cheats, the CS2 appeal path dies outright, because `sv_cheats` cannot be on during a real match.

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

## Half 1b — how good can GOTV be made? *(protocol, 2026-08-22)*

**Why this one is worth more than the others:** if a higher capture rate makes GOTV good enough for
**aim** analysis, CS2 gains a forensic artefact **we hold ourselves** — no client cooperation,
nothing for the accused to withhold — and **V15's forcing question stops mattering for CS2.**

> **Either way GOTV keeps its behavioural job.** It already answers wallhack and pre-aim, with
> `spec_show_xray`. This test only asks whether it can *also* answer the aim question.

### Setup

1. Copy [`ns_gotv_test.cfg`](ns_gotv_test.cfg) into `…/game/csgo/cfg/`
2. Start a local server on a map you will reuse for all three runs
3. In console: `exec ns_gotv_test`
4. **Write down what `help tv_snapshotrate` prints** — the default is what run A measures, and it
   is not documented anywhere reliable. The game is the authority

### The three runs

**Same map, same bots, exactly 5 minutes each, and kill a few bots each run** so there is a real
flick to compare. Equal duration is the whole reason the sizes mean anything.

| Run | Set this first | Then | File |
| --- | --- | --- | --- |
| **A** | *(leave `tv_snapshotrate` at default)* | `tv_record ns_gotv_a` → 5 min → `tv_stoprecord` | `ns_gotv_a.dem` |
| **B** | `tv_snapshotrate 64` | `tv_record ns_gotv_b` → 5 min → `tv_stoprecord` | `ns_gotv_b.dem` |
| **C** | `csgo_fatdemo_enable true` | `tv_record ns_gotv_c` → 5 min → `tv_stoprecord` | `ns_gotv_c.dem` + `.fatdem` |

If `tv_snapshotrate 64` is refused or clamped, **note the value it actually took** — a ceiling is
itself the answer.

### Results

| | Default rate (A) | Rate 64 (B) | Fat demo (C) |
| --- | --- | --- | --- |
| `tv_snapshotrate` reported | | | |
| File size | | | |
| MB per minute | | | |
| Extra files produced | | | `.fatdem`? |

Sizes and MB/minute: run [`./compare-sizes.sh`](compare-sizes.sh) (it reads the three files
straight out of the CS2 directory and extrapolates to a full match).

**Then:** watch the same flick in A and B with `demo_timescale 0.1`. Does the higher rate show
crosshair movement the default smooths away?

| | |
| --- | --- |
| **Is the aim visibly better resolved in B or C?** | ☐ yes ☐ no ☐ cannot tell |
| **Cost multiplier vs A** | B: ___×  C: ___× |
| **Against [024](../../docs/decisions/024-demos.md)'s measured 233 MB mean** | new estimate: ___ MB/match |

### Verdict

☐ **Good enough** — CS2 needs no client POV for aim; V15 forcing becomes irrelevant there
☐ **Not good enough** — the client POV demo stays necessary for the aim question
☐ **Good enough but too expensive** — record the multiplier; this becomes a per-tournament option
  rather than a default

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
| **`record` usable in CS2, 2026** | ☑ **yes** — 1–2 s stall, recovers, file saves | Run 2026-08-22, **loopback host** — repeat on a remote server under load |
| **`playdemo` opens the file** | ☑ **structurally valid** — `PBDEMS2` magic, 2.6 MB, header parses. Opening it in-game still untested | Header inspected 2026-08-22 |
| **Server can force it** | ☐ not tested — **Phase 6** | Needs the CSSharp plugin |
| **High-rate GOTV good enough?** | ☐ untested — **half 1b** | Would make the forcing question moot for CS2 |

**CONTEXT §2.9 updated 2026-08-22.** It no longer records the CS2 state as unknown: the freeze
claim survived two rounds of desk research and died in five minutes of testing. Which is the
lesson worth keeping — **the web on this question is one blog post from November 2023 wearing
different hats.**
