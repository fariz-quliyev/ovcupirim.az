# Staging runbook

How to run and verify a staging environment. Staging exists to rehearse a production deployment
against the real images, the real nginx, the real fail-fast checks and a real PostgreSQL — the one
thing it does differently is simulate the two integrations that have no sandbox yet.

Companion documents, none of which this one repeats:

- `docs/deployment.md` — the production topology, first run, migrations, backup, rollback.
- `docs/deployment-verification.md` — the checklist to run against any deployed environment.
- `docs/production-runbook.md` — day-to-day operations once live.

## A. What makes Staging different from Production

Exactly one thing, and it is deliberate: `ASPNETCORE_ENVIRONMENT=Staging` (set in
`docker-compose.staging.yml`) makes `AddInfrastructure` wire the **simulated** SMS sender and
payment gateway instead of Poctgoyercini and Epoint — see `allowSimulatedIntegrations` in
`backend/src/Ovcuprim.Api/Program.cs`. Neither provider has a confirmed sandbox
(`docs/payments-epoint.md`, `docs/sms-poctgoyercini.md`), so this is how a staging deployment can
complete an OTP sign-in and a full payment flow at all.

Everything else is production-strict, and was verified to be:

| Behaviour | Staging | Why it matters |
|---|---|---|
| `AllowedHosts` refused when unset/wildcard | Enforced | A wrong `Host:` header answers 400, verified live |
| Storage root must be absolute + writable | Enforced | Host refuses to start otherwise, verified live |
| HSTS + HTTPS redirect | Enforced | `IsDevelopment()` only, untouched |
| OTP rate limits (60s resend, 5/hour per address) | Production values | Verified live — the Development overrides in `appsettings.Development.json` do **not** apply |
| Structured JSON logging to stdout | Production formatter | `appsettings.Development.json` is not loaded |
| Dev region fixture (`regions.dev.json`) | **Never seeded** | `--seed` only includes it under `IsDevelopment()` |
| Synthetic listing generator | Refused | Guarded on `IsDevelopment()` |

**Real credentials cannot be activated by accident.** While the environment is Staging,
`AddInfrastructure` ignores `Sms__Poctgoyercini__*` and `Payments__Epoint__*` entirely. Setting a
real key in `.env.staging` does nothing — the simulated gateway is still used. This is structural,
not a convention.

## B. The diagnostic endpoints, and why they are safe here

Staging maps the same routes Development does — `/api/v1/dev/otp/{phoneNumber}` (reads back a
captured OTP) and `/api/v1/dev/payments/{orderId}/checkout|simulate|expire` (drives a fake
checkout). Those are genuinely dangerous on a network-reachable host: anyone who could reach them
could read any phone's sign-in code.

They are not reachable through the published port. `frontend/nginx.conf` has a dedicated
`location /api/v1/dev/ { return 404; }` block, so the public edge refuses them outright, in every
environment. Reach them from the server instead:

```bash
docker compose -f docker-compose.staging.yml --env-file .env.staging \
  exec api curl -s "http://localhost:8080/api/v1/dev/otp/%2B994501234567"
```

The OTP itself is also **never written to the log** — `DevelopmentSmsSender` logs that a message
was captured and its length, nothing more. The in-memory probe above is the only way to read one.

## C. First run

```bash
cp .env.staging.example .env.staging     # fill in every blank
docker compose -f docker-compose.staging.yml --env-file .env.staging build
docker compose -f docker-compose.staging.yml --env-file .env.staging run --rm migrate
docker compose -f docker-compose.staging.yml --env-file .env.staging up -d
```

Then seed, in this order:

```bash
# Categories, attributes, static pages, FAQ. Idempotent. Inserts no regions in Staging, by design.
docker compose -f docker-compose.staging.yml --env-file .env.staging run --rm api --seed

# The official 2024 classifier — 75 rows. Needs an Admin account; there is deliberately no API
# that grants Admin, so the first one is made directly in the database, exactly as in production:
docker compose -f docker-compose.staging.yml --env-file .env.staging \
  exec postgres psql -U ovcuprim -d ovcuprim \
  -c "UPDATE \"Users\" SET \"Role\" = 2 WHERE \"PhoneNumber\" = '<the operator's number>'"

curl -X POST "http://<staging-host>/api/v1/admin/regions/import" \
  -H "Authorization: Bearer <admin token>" -H "Content-Type: application/json" \
  --data @backend/src/Ovcuprim.Infrastructure/Persistence/Seed/Data/regions.official-2024.json
```

**Promotion packages are not seeded.** No package rows exist in source control — the commercial
catalog is live data an administrator creates through `/admin/packages`. Create a clearly-named
throwaway package if a payment flow needs exercising, and switch it off (`isActive: false`)
afterwards; packages are never deleted, by design.

## D. Topology

Identical to production (`docs/deployment.md` §A), with its own isolated Compose project so it can
never touch the dev or production stack:

| | Staging | Production |
|---|---|---|
| Compose project (`name:`) | `ovcupirim-staging` | `ovcupirim-prod` |
| Env file | `.env.staging` (via `--env-file`) | `.env` |
| Network subnet | `172.29.0.0/24` | `172.28.0.0/24` |
| Volumes | `ovcupirim-staging_postgres-data`, `_uploads-data` | `ovcupirim-prod_*` |
| Published port | `${STAGING_HTTP_PORT:-8080}` → 80 | 80 (and 443) |

`FORWARDED_KNOWN_NETWORK` in `.env.staging` must match the subnet above, or every visitor is rate
limited as one caller.

## E. Health, restart, logs, backup, rollback

All identical to production — see `docs/deployment.md` §E–§I. The one staging-specific note: the
published port is 8080 by default, so health checks are `http://<host>:8080/health` and
`http://<host>:8080/api/v1/health`.

## F. What a staging pass cannot prove

Two checks in `docs/deployment-verification.md` need inputs a staging rehearsal on a single machine
does not have, and neither should be marked passed without them:

- **§3, forwarded headers from two distinct client addresses.** The whole rate-limiting design
  rests on the API seeing the real caller rather than the proxy, and that can only be shown from
  two genuinely different source addresses.
- **§7, HTTPS.** Requires a real hostname and a certificate. `frontend/nginx.conf` ships the server
  block commented out for exactly this reason — no domain is invented anywhere in this repository.
