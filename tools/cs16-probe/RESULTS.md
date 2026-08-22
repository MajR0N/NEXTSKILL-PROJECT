# CS 1.6 probe — protocol and results sheet

Answers **V2b, V2c, V3, V4** and **V15's CS 1.6 half** in one server session.
The plugin logs what the *server* can see; this sheet records what only a *human at a client*
can see. Both halves are needed — the engine gives the server no feedback about demo recording.

Fill this in during the session, not from memory afterwards.

---

## Setup

1. Build: `amxxpc nextskill_probe.sma`
2. Drop `nextskill_probe.amxx` into `addons/amxmodx/plugins/`, add it to `plugins.ini`, restart.
3. Log lands in `addons/amxmodx/logs/nextskill_probe_YYYYMMDD.log`, one CSV line per event.

**The server must admit non-Steam clients** (Reunion/dproto) or V3 — *the one that could hurt*
([005](../../docs/decisions/005-cs16-plugin.md)) — cannot be answered at all.

## Runs

Each tester does **four** connects where possible. Before each, in client console:

```
cl_filterstuffcmd 0      // or 1
connect <server>
amx_probe_note run A steam filter0
```

| Run | Client | `cl_filterstuffcmd` |
| --- | --- | --- |
| A | Steam | 0 |
| B | Steam | 1 |
| C | **non-Steam** (Reunion/dproto) | 0 |
| D | **non-Steam** | 1 |

`amx_probe_now` re-reads state at any moment without reconnecting.

---

## What the log answers by itself

| Line | Reading it |
| --- | --- |
| `CONNECT ... setinfo_at_connect=[…]` | **V2b.** Non-empty ⇒ the token is readable before team join and the roster gate can fail closed. Empty here but populated in `INSERVER` ⇒ the gate must wait, and the delay is the gap between the two lines |
| `CONNECT` across runs A/C | **V3.** Compare the non-Steam rows against the Steam rows. Any difference in presence, timing or value is the finding — the `steam` field records what each client type reports |
| `READBACK ... got_len` vs `sent_len` | **V4.** `got_len < sent_len` ⇒ the info key truncates and a 256-bit token does not fit as-is |
| `READBACK ... got_len=0` in runs B/D only | **V2c, setinfo half.** The filter blocks a server-issued `setinfo`. *Expected to be survivable* — our design has the **player** set it in his own config, not the server — but confirm rather than assume |

## What only a human can answer

Watch **your own client** after connecting. The server cannot see any of this.

### V15 — did the recording actually start?

| Run | Recording indicator appeared? | `.dem` file present in game dir? | Notes |
| --- | --- | --- | --- |
| A (Steam, filter 0) | ☐ yes ☐ no | ☐ yes ☐ no | |
| B (Steam, filter 1) | ☐ yes ☐ no | ☐ yes ☐ no | |
| C (non-Steam, filter 0) | ☐ yes ☐ no | ☐ yes ☐ no | |
| D (non-Steam, filter 1) | ☐ yes ☐ no | ☐ yes ☐ no | |

Expected file: `nextskill_probe_<slot>.dem` in the game directory.

**If B or D fails while A or C succeeds, `cl_filterstuffcmd` blocks `record`.** That is a real
finding and not a blocker — [058](../../docs/decisions/058-what-we-sell.md) §6c already treats a
recording that does not start as **refusal, never clean**. Write it down either way.

### Anything that felt wrong

Freezes, stutters, console errors, a name change, the client refusing to connect:

```
(free text)
```

---

## Verdicts

Copy these into `CONTEXT.md` §7 when filled, and strike the V-items they close.

| | Verdict | Evidence (log line or run) |
| --- | --- | --- |
| **V2b** | ☐ readable at connect ☐ only later ☐ never | |
| **V2c** | ☐ filter irrelevant ☐ blocks `setinfo` ☐ blocks `record` ☐ blocks both | |
| **V3** | ☐ non-Steam identical ☐ **differs — see notes** | |
| **V4** | ☐ 43-char token survives ☐ truncated at ___ chars | |
| **V15 (1.6)** | ☐ server can start a POV demo ☐ cannot ☐ only without the filter | |

> **V3 is the one that could hurt.** If non-Steam clients behave differently, the two-factor
> binding in [010](../../docs/decisions/010-identity-binding.md) needs rethinking for the majority
> of the 1.6 audience — so record exactly *how* it differs, not just that it did.
