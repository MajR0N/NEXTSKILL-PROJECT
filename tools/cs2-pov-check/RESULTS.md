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

## Half 1 — does `record` still freeze CS2? *(no server needed)*

The claim that it freezes traces to **one Leetify post dated 8 Nov 2023**, describing an outage
window that Valve closed the next day (CONTEXT §2.9). Reports continued to April 2024. **Nothing
primary exists for 2026** — every current search result recycles that same post. So measure it.

Run each row on a live CS2 client. A casual/deathmatch server is fine; a full match is not needed.

| # | Action | Froze? | Recording indicator? | `.dem` written? | Playable with `playdemo`? |
| --- | --- | --- | --- | --- | --- |
| 1 | `record ns_test1` while **alive, mid-round** | ☐ | ☐ | ☐ | ☐ |
| 2 | `record ns_test2` while **dead / spectating** | ☐ | ☐ | ☐ | ☐ |
| 3 | `record ns_test3` **during warmup** | ☐ | ☐ | ☐ | ☐ |
| 4 | `record ns_test4`, play a **full round**, then `stop` | ☐ | ☐ | ☐ | ☐ |

Row 2 matters more than it looks: the 2023–24 reports include a workaround of *starting the
recording while spectating before spawning*. If only row 1 fails, the feature is usable with a
constraint rather than broken.

**Client / build tested:**

```
CS2 version (Help → About, or `version` in console):
Date tested:
OS:
```

### Anything that felt wrong

Stutter on start, frame drops while recording, file size, crash on `playdemo`:

```
(free text)
```

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
| **`record` usable in CS2, 2026** | ☐ yes ☐ no ☐ only when spectating | |
| **Server can force it** | ☐ yes ☐ blocked ☐ not tested (Phase 6) | |

**Then update CONTEXT §2.9**, which currently records the 2026 state as *unknown* on the honest
grounds that no primary source exists. This sheet is how it stops being unknown.
