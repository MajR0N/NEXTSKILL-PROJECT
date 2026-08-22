# tools/

Measurement instruments for the open verification items in
[CONTEXT §7](../CONTEXT.md). Each exists because the question it answers **cannot be settled by
reading** — it needs a real server, a real client, or real matches.

| Tool | Answers | Needs |
| --- | --- | --- |
| [`cs16-probe/`](cs16-probe/) | **V2b, V2c, V3, V4, V15 (1.6 half)** | A CS 1.6 server that admits **non-Steam clients**, and testers |
| [`cs2-pov-check/`](cs2-pov-check/) | **V15 (CS2 half)** — plus `ns_gotv_test.cfg` and `compare-sizes.sh` for the GOTV fidelity/size test | A CS2 client. Half of it needs nothing else |
| [`steam-serverlist-probe/`](steam-serverlist-probe/) | **V9** — does `GetServerList` work as 057 needs? | A Steam Web API key. Nothing else |
| `NextSkill.Domain/Integrity/DetectionCalibration.cs` + `CalibrationChecks.cs` | **V14** | Runs today; the *inputs* need matches |

## Why the CS 1.6 items share one plugin

V2b, V2c, V3 and V4 are four questions with one shape: *how does this engine behave with `setinfo`
and server-issued commands, on the client types our audience actually uses?* They need the same
server, the same session and the same testers. **Server time with real non-Steam clients is the
scarce resource — not code**, so the probe asks everything at once.

V15's 1.6 half rides along free: it is one more `client_cmd` on the same connect.

## Why V14's tool is code and the others are sheets

V14 is arithmetic plus data. The arithmetic is in the domain, checked by the ordinary runner, and
**runs today** — it already establishes that the auto-ban threshold must be near zero and that
~16 000 clean observations are needed to claim a population-wide rate, versus 300 on a screened
pool. What it lacks is the data, which needs matches.

V2c/V3/V4/V15 are engine behaviour. **No amount of code substitutes for one connect from a real
Reunion client**, so those tools are a probe that logs facts and a sheet a human fills in.

## Order

1. ~~**`cs2-pov-check/` half 1**~~ — **done 2026-08-22.** `record` works; 1–2 s stall, not a hang.
2. **`cs2-pov-check/` half 1b** — **the highest-value test left.** If a raised `tv_snapshotrate` or
   a fat demo resolves aim well enough, CS2 needs no client POV for the aim question and V15's
   forcing half becomes irrelevant there. `exec ns_gotv_test`, three runs, then `./compare-sizes.sh`.
3. **`steam-serverlist-probe/`** — **needs only an API key**, so it is blocked on nothing and no
   one. It gates the Phase 5 listing, which is the phase actually in progress.
4. **`cs16-probe/`** — one session closes four V-items and the rest of V15.
5. **V14** — runs now; its inputs arrive with the first real matches, so the *instrumentation*
   (Phase 6: log every flag including the ones that resolve silently) is the part that must not be
   forgotten. Miss it and V14 is unanswerable retroactively.
