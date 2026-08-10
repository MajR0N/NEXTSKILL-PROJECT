# 008 — Database

**Status:** **decided**
**Date:** 2026-08-10
**Decision:** **One PostgreSQL database.** No Redis until SignalR or a measured cache need
requires it. Demo files on disk/object storage, metadata rows only.

---

## First: how many databases?

The original question was *"which databases for users, server data, etc."* — implying one
per concern. **That is the wrong shape, and it's worth explaining why before choosing a
product.**

> *Plain version:* splitting data across databases costs you the two things that make this
> problem easy.
>
> **Joins.** "Show this player's matches with team names and current rating" is one query
> when it's one database. Across three databases it becomes three queries plus manual
> stitching in C#, and it gets slower and buggier as the page gets richer.
>
> **Transactions.** "Record the result **and** advance the bracket **and** update ratings —
> or do none of it" is one transaction in one database. Across databases you get partial
> failure: a bracket that advanced but ratings that didn't. That's a corrupt tournament,
> and it's very hard to detect after the fact.

Splitting is what you do when one database genuinely cannot cope, and you pay those costs
deliberately. NEXTSKILL is nowhere near that, and
[035](035-match-result-integrity.md) makes atomicity a **correctness requirement**, not a
convenience.

**Decision: one database.** Users, teams, matches, servers, ratings, demos-metadata all
live together.

Demo *files* are the exception — they are blobs on disk or object storage, with only their
metadata in the database. Databases are bad at large binaries.

---

## What this database actually has to do

| Requirement | Why | Weight |
| --- | --- | --- |
| Highly relational data | players ↔ teams ↔ rosters ↔ matches ↔ brackets ↔ ratings | **High** |
| Real transactions | result + bracket advance + rating must be atomic ([035](035-match-result-integrity.md)) | **High** |
| Per-game variable payloads | CS2 and CS 1.6 report different things ([001](001-game-scope.md)) — stats, demos, round data | **High** |
| Excellent EF Core support | The stack is ASP.NET Core ([004](004-stack.md)) | **High** |
| Free, and cheap to host | Community project, no budget | **High** |
| Scale-out path | Stated goal: "if it succeeds, we'd love it scalable" | Medium |
| Low ops burden | One person maintaining it | Medium |

---

## The candidates

### PostgreSQL

| | |
| --- | --- |
| **For** | Best-in-class **`jsonb`** — indexable, queryable JSON, ideal for per-game payloads. Excellent EF Core provider (Npgsql). Free, no licensing traps. Strong constraints (`CHECK`, partial/expression indexes, exclusion constraints) that let the **database** enforce rules the app might forget. Standard everywhere, easy to host. |
| **Against** | Slightly more to learn than MySQL. Not the CS-community default. |
| **Fit** | **Strong.** `jsonb` directly solves the two-games problem: the shared columns stay real columns, and the differing parts live in one indexed JSON column — no lowest-common-denominator schema, no table-per-game. |

### MySQL / MariaDB

| | |
| --- | --- |
| **For** | The **CS community default** — MatchZy stores stats in MySQL, AMXX plugins commonly target it. Ubiquitous cheap hosting. Familiar to anyone from the HL/CS server world. Good EF Core provider (Pomelo). JSON support exists and is decent. |
| **Against** | JSON support is meaningfully weaker than `jsonb` (no direct JSON indexing — needs generated columns). Historically looser about data integrity, though modern versions are much better. |
| **Fit** | **Good.** The ecosystem argument is real — if Pukawka or other organisers ever want direct DB access, MySQL is what they'll know. But our plugins talk **HTTP, not SQL** ([002](002-cs2-plugin.md)), so that familiarity never actually gets used. |

### SQLite

| | |
| --- | --- |
| **For** | **Zero ops.** One file. No server, no connection strings, no backups beyond copying a file. Genuinely excellent, and the laziest thing that could possibly work. Fine EF Core support. |
| **Against** | **One writer at a time.** Fine for one app instance and modest writes; fails as soon as you run two app instances — which is exactly the scale-out path. Weaker typing. Migrating SQLite→Postgres later is real, annoying work. |
| **Fit** | **Good for now, wrong for the stated goal.** If scalability were not a requirement this would be the honest recommendation. It conflicts directly with "we'd love to make it scalable." |

### SQL Server

| | |
| --- | --- |
| **For** | The most native .NET experience — first-party EF Core provider, best tooling, LocalDB for dev. |
| **Against** | **Licensing.** Express is free but capped (10 GB, limited RAM/CPU); beyond that it is genuinely expensive. Heavier in containers. JSON is stored as text with indexing only via computed columns — clearly weaker than `jsonb`. |
| **Fit** | **Weak.** Buys polished tooling at the cost of a licensing ceiling on a free community project. |

### MongoDB (and document databases generally)

| | |
| --- | --- |
| **For** | Flexible schema; per-game payload differences are trivial. |
| **Against** | **This data is aggressively relational.** Players, teams, rosters, matches and brackets are joins in every direction. Document stores make you either denormalise (and then fight consistency) or do application-side joins. Cross-document transactional guarantees are weaker than a relational database's. |
| **Fit** | **Poor.** The one thing it wins at — flexible payloads — Postgres already does with `jsonb`, without giving up joins or transactions. |

---

## Comparison

| | PostgreSQL | MySQL/MariaDB | SQLite | SQL Server | MongoDB |
| --- | --- | --- | --- | --- | --- |
| Relational fit | ✅ | ✅ | ✅ | ✅ | ❌ |
| Transactions | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| JSON per-game payloads | ✅ **`jsonb`, indexed** | ⚠️ workable | ⚠️ basic | ⚠️ text + computed cols | ✅ |
| EF Core support | ✅ Npgsql | ✅ Pomelo | ✅ | ✅ first-party | ⚠️ |
| Cost | ✅ free | ✅ free | ✅ free | ⚠️ Express capped | ✅ free tier |
| Ops burden | Medium | Medium | ✅ **none** | Medium-high | Medium |
| Scale-out | ✅ | ✅ | ❌ **single writer** | ✅ | ✅ |
| CS-community familiarity | ⚠️ | ✅ **the default** | ⚠️ | ❌ | ❌ |
| DB-enforced integrity | ✅ **strongest** | ⚠️ | ⚠️ | ✅ | ❌ |

---

## Head-to-head: PostgreSQL vs MySQL/MariaDB

SQLite, SQL Server and Mongo eliminated. This is the real decision, argued with **actual
NEXTSKILL rules** rather than feature lists.

### Round 1 — enforcing "one live match per server"

[007](007-server-hosting-model.md) requires that a registered server never hosts two live
matches at once. This is a genuine correctness rule, not a nicety.

**PostgreSQL** — the database enforces it, in one line:

```sql
CREATE UNIQUE INDEX one_live_match_per_server
  ON matches (server_id)
  WHERE status = 'live';
```

That is a **partial index** — a uniqueness rule that applies only to rows matching a
condition. Finished matches are exempt; live ones cannot collide. Two concurrent requests
racing to start a match on the same server: one succeeds, one gets a database error.
Correct, always, regardless of application bugs.

**MySQL/MariaDB** — **has no partial indexes.** The options are:

- enforce it in C# — which is a race condition unless you also take an explicit lock
- a `BEFORE INSERT` trigger — logic hidden away from the codebase
- a nullable "live marker" column with a unique index, kept in sync by hand — a hack that
  works but that someone will eventually break

> *Plain version:* a **race condition** is when two things happen at the same instant and
> the result depends on which lands first. Two organisers clicking "start match" together
> can both pass a C# check that says "no live match exists" before either has written a
> row. A database constraint can't be raced — the database is the single point where
> order is decided.

The same pattern recurs across NEXTSKILL: one active roster slot per account per match,
one unused link code per slot ([011](011-accounts-and-login.md)), one confirmed result per
map. **Postgres expresses each as an index; MySQL needs application discipline for each.**

### Round 2 — querying per-game payloads

CS2 and CS 1.6 report different data ([001](001-game-scope.md)), so the varying part lives
in JSON.

**PostgreSQL `jsonb`** — binary-stored, and **indexable as a whole**:

```sql
CREATE INDEX ON matches USING GIN (payload);
-- now ANY key inside payload is searchable, including keys
-- you haven't thought of yet
SELECT * FROM matches WHERE payload @> '{"map": "de_dust2"}';
```

**MySQL JSON** — queryable, but **not directly indexable.** You must declare a generated
column per key you want to search, in advance:

```sql
ALTER TABLE matches
  ADD COLUMN map VARCHAR(64)
    AS (payload->>'$.map') STORED,
  ADD INDEX (map);
-- every NEW query pattern = another schema migration
```

This is the difference that matters most in practice. Early on you **do not know** which
payload fields you will want to filter by — that's precisely why the data is in JSON. With
Postgres, a new query is a new query. With MySQL, a new query is a **schema migration**.

### Round 3 — data types NEXTSKILL actually needs

| Need | PostgreSQL | MySQL/MariaDB |
| --- | --- | --- |
| **Timestamps across countries** — Polish organiser, international players | `timestamptz` stores a true instant, converts on read | `DATETIME` has no timezone; `TIMESTAMP` has the 2038 problem. Timezone handling is manual and error-prone |
| **IP addresses** for abuse detection ([011](011-accounts-and-login.md)) | Native `inet` — validates, supports subnet operators | `VARBINARY`/`VARCHAR` and do it yourself |
| **UUIDs** for public-facing IDs | Native `uuid`, 16 bytes | `BINARY(16)` (fast, unreadable) or `CHAR(36)` (readable, wasteful) |
| Arrays (map pools, veto orders) | Native array type | Another table, or JSON |
| Enums (match status, integrity state) | Real `ENUM` type | `ENUM` exists but altering it is painful |

Timezone handling deserves emphasis: **a tournament schedule is a timezone problem.**
A Polish organiser scheduling a match for Brazilian and Turkish players
([001](001-game-scope.md)) will hit this on day one. `timestamptz` makes it boring; MySQL
makes it a recurring source of bugs.

### Round 4 — where MySQL genuinely wins

Stated fairly, because these are real:

| MySQL advantage | Assessment for NEXTSKILL |
| --- | --- |
| **The CS community's native database.** MatchZy stores stats in MySQL; AMXX plugins target it | Real — but **never exercised here.** Our plugins talk HTTP, not SQL ([002](002-cs2-plugin.md)). No organiser ever touches our database |
| Slightly simpler to operate; more shared-hosting support | Real, but marginal — both are one `docker compose` service |
| Faster on trivial primary-key reads (historically) | Irrelevant at this scale, and Postgres has closed most of the gap |
| More people in the CS scene can help debug it | Genuine, if you expect community help with the database |

### Round 5 — things that are now a tie

Worth stating so they don't get used as false arguments:

- **Window functions and CTEs** (needed for leaderboards and rankings) — MySQL 8+ has both.
- **CHECK constraints** — MySQL 8.0.16+ enforces them.
- **Transactions/MVCC** — InnoDB is solid.
- **EF Core** — Npgsql (first-party-grade) vs Pomelo (third-party, good). Slight Postgres
  edge, not decisive.

MySQL 8 closed most of the historical gap. **The remaining differences are exactly the ones
NEXTSKILL happens to need:** partial indexes, indexable JSON, and `timestamptz`.

### Verdict

| | PostgreSQL | MySQL/MariaDB |
| --- | --- | --- |
| Enforcing NEXTSKILL's uniqueness rules | ✅ one index each | ❌ app logic or triggers |
| Querying unknown-in-advance JSON keys | ✅ GIN index, any key | ❌ schema migration per key |
| International tournament scheduling | ✅ `timestamptz` | ⚠️ manual |
| IP storage for abuse detection | ✅ `inet` | ⚠️ manual |
| CS-community familiarity | ❌ | ✅ (unused here) |

---

## Recommendation: PostgreSQL

Three reasons:

1. **`jsonb` solves the two-games problem cleanly.** CS2 and CS 1.6 report genuinely
   different data. Shared fields stay real columns with real constraints; the differing
   parts live in one indexed `jsonb` column. No table-per-game, no schema that fits neither
   game well.
2. **The strongest integrity guarantees**, and [035](035-match-result-integrity.md) made
   integrity a first-class requirement. Constraints the database enforces cannot be
   forgotten by application code at 2am.
3. **Free, standard, and scales out** — satisfying the stated ambition without buying
   anything.

**The genuine counter-argument is MySQL/MariaDB:** it's the CS server community's native
database, and if organisers ever touch data directly, it's what they know. That argument
loses only because our plugins communicate over HTTP and never touch the database at all —
so the familiarity never gets exercised.

**The honest ponytail counter-argument is SQLite:** zero ops, one file, and genuinely
sufficient for a first tournament. It is rejected *only* because of the explicit
scalability goal — SQLite's single-writer model is incompatible with running more than one
app instance.

## Also decided

- **Redis is not added yet.** It arrives only when SignalR needs a backplane across
  multiple app instances ([004](004-stack.md)) or when a measured cache need appears.
  Not before. One moving part at a time.
- **Demo files are not in the database.** Blobs on disk or object storage; metadata rows
  only. Retention policy owed under decision 024, and GDPR-relevant
  ([011](011-accounts-and-login.md)).

## Decision

**PostgreSQL.**

The deciding factor was not raw capability — MySQL 8 is entirely capable — but **who
controls the infrastructure.** NEXTSKILL is hosted by us on our own domain
([037](037-platform-hosting.md)), not installed by organisers on their hosting. There is
therefore no external constraint forcing MySQL, and no reason to accept its three gaps:

| NEXTSKILL rule | PostgreSQL | MySQL |
| --- | --- | --- |
| One live match per server ([007](007-server-hosting-model.md)) | partial unique index | app logic (raceable) or trigger |
| Query payload keys not known in advance ([001](001-game-scope.md)) | GIN index over `jsonb` | generated column + migration per key |
| International tournament scheduling | `timestamptz` | manual UTC discipline |

The MySQL counter-argument — CS-community familiarity — was real but **never exercised**:
plugins talk HTTP, not SQL, and organisers will never be given direct database access
(it would expose RCON credentials and GDPR-covered personal data).

### Schema commitments arising from earlier decisions

These must exist in the **first** migration, because adding them later is painful:

- `matches.game` — game discriminator ([001](001-game-scope.md))
- `matches.integrity` — `confirmed` / `suspect` / `disputed` ([035](035-match-result-integrity.md))
- `organisers.trusted` — per-organiser trust flag (CONTEXT §3b)
- `matches.payload jsonb` — per-game variable data, GIN-indexed
- partial unique index enforcing one live match per server ([007](007-server-hosting-model.md))
- IP storage as `inet`, hashed where used for abuse detection ([011](011-accounts-and-login.md))
- all timestamps as `timestamptz`, never naive

### Accepted lock-in

Using `jsonb` GIN indexes, partial indexes and `inet` is deliberate coupling to PostgreSQL.
EF Core abstracts the ordinary 90%, but these features are precisely *why* Postgres was
chosen, so a later switch would mean re-solving them in application code. **Cheap to change
now, expensive in a year.** Accepted.
