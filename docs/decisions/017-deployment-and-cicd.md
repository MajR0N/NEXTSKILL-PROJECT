# 017 / 032 — Deployment, Docker & CI/CD

**Status:** decided
**Date:** 2026-08-10
**Lens applied:** devops-engineer
**Decision:** **Docker Compose on a single EU host. GitHub Actions deploys on push to
`main`. No deploy panel.**

---

## Deployment topology

One host, one `docker compose` file, EU region ([011](011-accounts-and-login.md) GDPR,
[037](037-platform-hosting.md)).

```
                    ┌─────────────────────────────┐
   internet ──CDN──►│  reverse proxy (TLS)        │
   (Cloudflare)     ├─────────────────────────────┤
                    │  nextskill-api  (ASP.NET)   │
                    │  postgres                   │
                    │  (no redis — see 039)       │
                    └─────────────────────────────┘
                    demo files: mounted volume / object storage
```

| Choice | Reason |
| --- | --- |
| Single host | One product, one developer, modest load. [006](006-architecture.md) chose one deployable |
| **CDN/proxy in front** | The only real DDoS answer ([040](040-threat-model.md) §8). **Origin IP must stay private** |
| No Redis | SSE removed the backplane requirement ([039](039-public-api.md)) |
| No Kubernetes | Orchestrating one container is a hobby, not a requirement |
| Postgres in a container, **volume-backed** | Fine at this scale. Backups matter more than the hosting model |
| Demos on a mounted volume | Outside the web root ([012](012-security.md)); move to object storage when volume demands it |

> `ponytail:` one compose file, one host. Ceiling: no horizontal scaling, and a host failure
> is an outage. Upgrade path: the app is already stateless, so scaling out means adding
> instances behind the proxy plus Redis for SSE fan-out — not a rewrite.

### Backups are the part that actually matters

More important than any scaling concern. **The database is the product** — losing it means
losing every tournament, rating and account.

- Automated nightly `pg_dump`, stored **off the host**
- **A restore is tested, not assumed.** An untested backup is a hope, not a backup
- Demo files backed up separately, on their own retention schedule ([024](024))

## CI/CD

**GitHub Actions on push to `main`.** That is the entire deploy story.

| Stage | Action |
| --- | --- |
| On PR | Build, run tests, lint |
| On push to `main` | Build image, push to registry, deploy, health check |
| Failure | Fail loudly; previous container keeps serving |

Secrets live in GitHub Actions and on the host — **never in the repository**
([040](040-threat-model.md) §14).

### Decision: no deploy panel — deliberately skipped

The original idea was a dev panel inside the admin CMS that ships code to production with
GitHub branch/commit verification. **Skipped, on YAGNI and security grounds.**

| Question | Answer |
| --- | --- |
| Who needs it? | One developer — the same person with repo access |
| Can they already deploy? | Yes: `git push` |
| What does the panel add? | A button |
| What does it cost? | **Remote code execution as a feature** |

A web panel that deploys code means the web application holds production deploy credentials
and can execute code. One admin-panel XSS ([040](040-threat-model.md) §5) or one stolen
session becomes full server compromise — deploy keys, database credentials, HMAC secrets,
and any stored RCON passwords ([007](007-server-hosting-model.md)).

**Build it when:** someone needs to deploy who *shouldn't* have repository access. Not before.

**And when that day comes, build the safe version:** the panel calls the GitHub API to
dispatch a workflow and displays status. It never pulls code, never builds, never restarts
anything. Compromising it then yields a button that deploys already-reviewed code — not a
shell.

**Cheap middle ground available any time:** a **read-only** dev dashboard showing running
commit, branch, deploy time, and whether production matches `main`. All the visibility, no
deploy authority, no new attack surface.

## Code optimisation (016)

**No optimisation without measurement.**

Nothing in NEXTSKILL is known to be slow, because nothing exists. Optimising now means
guessing, and guesses about performance are wrong more often than they're right.

What *is* worth doing up front, because they're design decisions rather than optimisations:

| Do now | Why it isn't premature |
| --- | --- |
| Indexes for known access patterns | Adding an index later is fine; a schema built for the wrong access pattern is not |
| **Avoid N+1 in EF Core** (`Include`, projections) | A correctness-adjacent habit, not a tuning exercise |
| Cursor pagination ([039](039-public-api.md)) | Offset pagination breaks correctness under writes |
| Rating/bracket engines as pure functions ([022](022-tournament-formats.md), [025](025-rating-system.md)) | Trivially cacheable *later* precisely because they're pure |
| HTTP caching on public reads | A protocol feature we get free with REST |

Everything else waits for a profiler.

> `ponytail:` no cache, no premature tuning. Ceiling: none known. Upgrade path: measure,
> then cache the specific thing that's slow.
