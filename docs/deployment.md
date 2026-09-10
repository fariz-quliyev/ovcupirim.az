# Deployment

How to actually run `docker-compose.prod.yml` on a real host. This is the concrete "do this, in
this order" companion to two documents that already cover the reasoning:

- `docs/production-runbook.md` — storage, logging, health checks, backup/restore procedure and
  drill results, production-scale query performance.
- `docs/deployment-verification.md` — the checklist to run against a live environment after this
  procedure and after every configuration change.

Nothing here repeats their content; it points at them where they already answer the question.

## A. Architecture

One host, three containers, two named volumes, one internal Docker network:

```
                    ┌─────────────────────────────────────┐
   :80 (→:443) ───► │  web (nginx)                         │
   the only          │  - serves the built React SPA        │
   published         │  - proxies /api/* and /health → api  │
   port              │  - serves /uploads/* from the shared  │
                     │    volume directly (read-only)        │
                     └───────────────┬───────────────────────┘
                                      │ ovcuprim network (172.28.0.0/24)
                     ┌───────────────▼───────────────────────┐
                     │  api (.NET 10 / ASP.NET Core)          │
                     │  - Storage__Local__RootPath=/data/uploads (read-write)
                     │  - no port published to the host       │
                     └───────────────┬───────────────────────┘
                                      │
                     ┌───────────────▼───────────────────────┐
                     │  postgres (16-alpine)                  │
                     │  - postgres-data volume                │
                     │  - no port published to the host       │
                     └─────────────────────────────────────────┘

  migrate — a fourth, run-to-completion container (not part of the long-running stack): applies
  EF Core migrations once, before `api` is ever started, then exits. See §C.
```

This is deliberately the single-instance + persistent-volume architecture
`docs/production-runbook.md` §1 and §7 already commit to for initial launch — nothing here changes
that decision or reaches for object storage, a second API instance, or an orchestrator beyond
Docker Compose. Revisit only if that decision itself changes.

Only `web`'s port 80 (443 once TLS is added — see `frontend/nginx.conf`'s commented HTTPS block)
is ever reachable from outside the host. `api` and `postgres` are reachable only from other
containers on the `ovcuprim` network.

## B. First-time setup

```bash
cp .env.production.example .env
# Fill in every blank — see that file's own comments for what each key protects against and
# docs/payments-epoint.md / docs/sms-poctgoyercini.md for the two provider-specific sections.
```

Nothing in `.env` has a safe guessed default; the application refuses to start without the
required ones, exactly as it does in any other environment.

## C. Build, migrate, start

```bash
docker compose -f docker-compose.prod.yml build

# Applies every pending EF Core migration and exits. Never run automatically at api startup —
# migrations are always an explicit, operator-run step, matching the workflow CI and local
# development already use (`dotnet ef database update`).
docker compose -f docker-compose.prod.yml run --rm migrate

docker compose -f docker-compose.prod.yml up -d
```

`api` will not start until `postgres` reports healthy and `migrate` has exited successfully
(`depends_on: condition: service_completed_successfully`); `web` will not start until `api`
reports healthy. A fresh deploy therefore comes up in the right order on its own — only the
`migrate` step above has to be run by hand, every time the migration set changes.

## D. Taxonomy and the official region dataset

Two one-off seeding steps, both idempotent — safe to re-run against an already-seeded database,
and both already exist; nothing new was built for them.

```bash
# Categories, attributes, static pages, FAQ. Inserts anything missing; changes nothing that
# already exists.
docker compose -f docker-compose.prod.yml run --rm api --seed
```

The region dataset is imported through the admin API instead of a container command, because it
requires an authenticated Admin session:

```bash
curl -s -X POST "https://<real-domain>/api/v1/admin/regions/import" \
  -H "Authorization: Bearer <admin-access-token>" \
  -H "Content-Type: application/json" \
  --data @regions.official-2024.json
```

See `docs/region-dataset.md` for where that file comes from (the Azerbaijan State Statistics
Committee's 2024 classifier, already reconciled and tested — not invented here) and exactly what
it contains. Without this step, sellers cannot select a region and therefore cannot publish a
listing at all, per `docs/production-runbook.md`'s own launch-blocker list.

## E. Health checks

| Endpoint | Behind | Checks | Wire it to |
|---|---|---|---|
| `GET /api/v1/health` | `api`'s own Docker `HEALTHCHECK` | Process is answering | Nothing further needed — Compose already restarts `api` on `unless-stopped` if the container exits; this is what marks it "healthy" for `web`'s `depends_on`. |
| `GET /health` (via `web`, path `/health`) | `web`'s nginx proxy | Database reachability | An external uptime monitor / status page, if one is added later. |
| `GET /` (via `web`) | `web`'s own Docker `HEALTHCHECK` | nginx is serving | Nothing further needed. |

The liveness/readiness distinction and why they must stay separate (a database blip should not
restart the API) is `docs/production-runbook.md` §3's reasoning, unchanged here — this section
only says where each check is wired in this specific compose stack.

## F. Restart policy

Every long-running service is `restart: unless-stopped` — survives a host reboot and a container
crash, never fights an operator's deliberate `docker compose stop`. `migrate` is `restart: "no"`:
it is meant to run once and exit; restarting a failed migration automatically would retry a
possibly-broken migration in a loop instead of surfacing the failure.

## G. Logs

Every container logs structured JSON to stdout in production (Serilog — see
`docs/production-runbook.md` §2) and is captured by Docker's `json-file` driver, capped at 10 MB
× 3 files per container (`docker-compose.prod.yml`'s `x-logging` anchor) so logs cannot fill the
disk unbounded. Read them with:

```bash
docker compose -f docker-compose.prod.yml logs -f api
```

Forwarding them to an aggregator (CloudWatch, Loki, …) is a deployment choice this repository does
not make — point whatever collector the deployment already uses at the containers' stdout, or
swap the `json-file` driver for that collector's own logging driver.

## H. Backup

The procedure, what it proves, and the one open item (production-scale timing) are
`docs/production-runbook.md` §4 — unchanged by this packaging. Two things specific to this compose
stack:

- Run `pg_dump` from inside the `postgres` container (or from a machine with network access to it
  temporarily published), since its port is not exposed to the host by default:
  ```bash
  docker compose -f docker-compose.prod.yml exec postgres \
    pg_dump -U ${POSTGRES_USER} -d ${POSTGRES_DB} -Fc -f /tmp/ovcuprim-$(date +%Y%m%d-%H%M).dump
  docker compose -f docker-compose.prod.yml cp postgres:/tmp/ovcuprim-<timestamp>.dump ./
  ```
- The `uploads-data` volume needs its own backup, alongside the database — a filesystem-level
  snapshot of the volume (`docker run --rm -v uploads-data:/data -v $(pwd):/backup alpine tar czf
  /backup/uploads-$(date +%Y%m%d).tar.gz -C /data .` is one way, run while `api` is briefly
  stopped for a consistent snapshot, or backed up via whatever the host's volume-snapshot tooling
  already does).

## I. Rollback

Two independent things can need rolling back — decide which actually happened before choosing:

**A bad application release, schema unchanged.** Re-deploy the previous image tag/commit:
```bash
git checkout <previous-commit>
docker compose -f docker-compose.prod.yml build api web
docker compose -f docker-compose.prod.yml up -d api web
```
No `migrate` re-run needed if the schema did not change between the two commits.

**A bad migration.** EF Core migrations are forward-only; there is no generated "down" step run
automatically. Two ways back, in order of preference:
1. If the migration is purely additive (a new nullable column, a new table) and the bad release
   was the *application* code, not the schema — just roll back the application per above and leave
   the schema ahead. This is almost always safe and is the reason migrations in this project are
   deliberately incremental and additive.
2. If the schema change itself must be undone: restore the database from the backup taken before
   the migration ran (§H and `docs/production-runbook.md` §4's restore procedure), then re-deploy
   the previous application commit. This loses any data written between the backup and the
   restore — acceptable only as a deliberate, last-resort decision, never automated.

Cutting the running stack over to a restored database is the same deliberate, separate action
`docs/production-runbook.md` §4 already describes (repoint `CONNECTION_STRING` in `.env`, then
`docker compose -f docker-compose.prod.yml up -d api`) — this procedure does not perform that
cutover on its own.

## J. What this does not decide

Everything `docs/production-runbook.md`'s "Remaining launch blockers" section already lists is
unchanged by this packaging: the real Epoint/SMS credentials, the real domain and
`ForwardedHeaders` values if a proxy sits in front of `web` itself (rare — `web` normally *is* the
proxy in this topology), backup automation scheduling, and prohibited-item screening content. This
document only says how to run the containers once those inputs exist.
