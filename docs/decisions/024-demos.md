# 024 — Demo storage & retention

**Status:** decided
**Date:** 2026-08-10
**Applies to CS2 only** — CS 1.6 has no server-side demo pipeline
([001](001-game-scope.md)).

> **Narrowed 2026-08-22 by [058](058-what-we-sell.md) §6c.** *"CS 1.6 has no server-side demo
> pipeline"* remains true and this document still applies to CS2 only. But a **client-side POV
> demo** is a different mechanism and it exists in 1.6: the server-side AC arms recording when its
> suspicion meter flags a player, and he uploads his own demo **only when he appeals a ban**.
>
> **Nothing here changes.** There is no broadcast pipeline, no routine retention, no storage cost —
> we hold nothing unless someone appeals — and **it must never be marketed as demos for 1.6**
> ([005](005-cs16-plugin.md) §"No demos" stands). What it buys is one artefact for the referee in
> the case that matters most, where 1.6 previously had none. **Confirmed available** — AMXX `client_cmd` is field-proven for exactly this ([CONTEXT §2.9](../../CONTEXT.md)); the residual question is only whether a player who enables `cl_filterstuffcmd` can refuse it, which §6c already treats as refusal rather than clean.
>
> **Extended the same day.** A client POV demo is not merely 1.6's substitute for what this
> document describes — **it is the higher-fidelity artefact in every game.** GOTV runs at snapshot
> rate with interpolated, quantised angles, which is fine for a match record and **not sufficient
> for per-tick aim analysis** ([CONTEXT §2.9](../../CONTEXT.md)). Nothing in this document's
> retention or budget changes — **POV demos cost us nothing until someone appeals** — but a referee
> deciding from GOTV alone must have that labelled, so no verdict reads as if it rested on evidence
> it did not have.

---

## Scale

| | |
| --- | --- |
| One CS2 GOTV demo, zipped | ~50–150 MB *(approximate — confirm on the first real upload)* |
| 16-team Bo3 tournament | ~30 maps ≈ **3 GB** |
| Weekly cadence, no deletion | ~**156 GB/year**, growing |

> ## Measured 2026-08-22 — the estimate above was low, and this is the confirmation it asked for
>
> The line *"~50–150 MB **(approximate — confirm on the first real upload)**"* has been confirmed
> against **7 real CS2 GOTV demos** (Valve matchmaking, `de_mirage` and others, a Warsaw `srcds`
> host), sitting on a local install:
>
> | | Measured | This document assumed |
> | --- | --- | --- |
> | One CS2 GOTV demo | **150–299 MB, mean 233 MB** | 50–150 MB |
> | ~30-map Bo3 tournament | **~7 GB** | ~3 GB |
> | Weekly cadence, one year | **~360 GB** | ~156 GB |
>
> **Roughly 2.5× the estimate.** And these are the raw `.dem`: CS2 demos are already
> snappy-compressed internally, so zipping recovers little — the *zipped* figure this table quotes
> is not meaningfully smaller than what was measured.
>
> **Caveat, stated so nobody over-reads it:** these are Valve matchmaking demos. A community
> tournament match may be shorter, and `tv_snapshotrate` on our own servers is ours to set — the
> same knob [CONTEXT §2.9](../../CONTEXT.md) flags for the fidelity question, pulling the other way.
> **Higher fidelity means bigger files, and that trade is now quantified rather than assumed.**
>
> ### This settles the storage question rather than reopening it
>
> §"Storage" below still reads *"disk volume on the app host"* and *"object storage was rejected
> for now"*. **[042](042-hosting.md) already superseded that** — *"024 chose local disk first; R2 is
> the upgrade"* — but this document was never amended to say so, so read alone it gives the wrong
> answer.
>
> **The measurement removes the doubt.** At 90-day retention the live set is **~37–75 GB**, not the
> 15–30 GB [042](042-hosting.md) sized against — and [042](042-hosting.md)'s chosen VPS has a
> **40 GB disk**. Local disk is not a fallback we are keeping in reserve; **it is arithmetically
> dead**, and R2's zero-egress argument only gets stronger as the files get bigger.

Small enough to ignore in year one, painful by year three. And the growth is one-directional.

## Decision: 90-day retention, with exceptions kept forever

| Match type | Retention |
| --- | --- |
| Normal match | **90 days**, then deleted automatically |
| **Disputed or `suspect`** | **Indefinite** — the demo is the evidence ([035](035-match-result-integrity.md)) |
| Final / grand final | **Indefinite** — the archive worth having |
| Anything staff-flagged | Indefinite |

**Why 90 days:** demos exist to settle disputes and let people review. Both happen within
days. After three months a normal group-stage demo is downloaded essentially never — but
finals are watched for years.

Steady-state storage stays flat because ~90% is deleted, while the part anyone actually
wants is kept.

### The retention job must actually run

A retention *policy* with no job enforcing it is worse than no policy — it's a written
commitment being silently broken. The deletion job is part of the feature, not a follow-up:

- runs on a schedule, logs what it deleted
- **never deletes a demo attached to an unresolved dispute**, regardless of age
- deletes the file and the metadata row together, in that order

## Storage

> ⚠ **Superseded by [042](042-hosting.md): demos live in Cloudflare R2, not on the app host.** 042
> says so explicitly — *"024 chose local disk first; R2 is the upgrade"* — and the 2026-08-22
> measurement above closes it: the live set at 90-day retention is **~37–75 GB against a 40 GB VPS
> disk.** The paragraphs below are kept as the reasoning that produced the upgrade path, **not as
> current policy.** Everything else in this section — authorised streaming endpoint, our own
> filenames, separate backup schedule — still applies, against R2 instead of a local volume.

**Disk volume on the app host**, outside the web root ([012](012-security.md)).

| | |
| --- | --- |
| Served via | An authorised endpoint that streams the file — never as a static path |
| Filenames | **Generated by us** from the match ID. Never taken from the uploaded archive ([040](040-threat-model.md) §7) |
| Backups | Separate schedule from the database ([017](017-deployment-and-cicd.md)) — demos are large and less critical than tournament data |

> `ponytail:` local disk, no object storage. Ceiling: bounded by one host's disk, and a host
> failure loses demos not yet backed up. Upgrade path: move to S3-compatible storage
> (R2/B2) if volume outgrows the disk — the serving endpoint stays identical, only the
> backing store changes.

Object storage was rejected **for now** because it adds a dependency, credentials, and an
egress bill to solve a problem 90-day retention already keeps small.

## GDPR

Demos contain player identifiers, so they are **personal data**
([011](011-accounts-and-login.md), [012](012-security.md)).

| Obligation | How this decision satisfies it |
| --- | --- |
| Defined retention | 90 days, published in the privacy notice |
| Enforced retention | The deletion job, which must actually run |
| Deletion requests | A player's data is anonymised, but **the demo is not deleted** — it is a record of a competitive match involving nine other people. Documented in the privacy notice rather than decided ad hoc |
| Access control | Authorised endpoint only; never a public static URL |

That third row is the one that needs stating in advance. A naive "delete everything about
this user" would destroy match evidence belonging to other players.

## Security

Demo upload is **the sharpest attack surface in the system** ([040](040-threat-model.md) §7)
— large archives arriving from third-party servers we don't control
([007](007-server-hosting-model.md)):

- decompression ratio limit and absolute size cap; abort mid-extraction
- HMAC verified **before** any bytes hit disk ([009](009-api-contract.md))
- per-server quota and a free-space check before accepting
- served with an explicit content type and `Content-Disposition: attachment`

## Who can download

| Role | Access |
| --- | --- |
| Anyone | Demos for public, confirmed matches |
| Match participants | Always, including disputed matches |
| Referee / organiser | Their tournament's demos ([036](036-authorization.md)) |
| Staff | Any demo, for dispute resolution — **audited** |

## Open follow-up

Whether to **parse demos for statistics** ([DemoFile.Net](https://github.com/saul/demofile-net),
~500 ms per match parallel). Deliberately deferred: it is a large feature, it is CS2-only, and
it would produce statistics that CS 1.6 players cannot have — which conflicts with keeping
both games first-class. Revisit only after both games are live.
