# Ovcuprim.az

Specialised classifieds marketplace for hunting, fishing, camping and outdoor equipment in Azerbaijan.

> Full analysis, architecture and roadmap: [`docs/OvcuPrim-Analysis-and-Implementation-Plan.pdf`](docs/OvcuPrim-Analysis-and-Implementation-Plan.pdf)

## Stack

| Layer    | Technology |
| -------- | ---------- |
| Frontend | React 19 · Vite · TypeScript (strict) · React Router 7 · TanStack Query · Tailwind v4 |
| Backend  | .NET 10 · ASP.NET Core Web API · EF Core 10 · FluentValidation · Serilog |
| Database | PostgreSQL 16 |
| Storage  | `IFileStorage` — local disk in development, S3-compatible in production |

## Layout

```
backend/            .NET solution (Domain · Application · Infrastructure · Api)
frontend/           React + Vite application
docs/               Plan and design reference (the original design canvas)
docker-compose.yml  PostgreSQL for local development
```

## Getting started

Requires .NET SDK 10, Node 20+, and Docker.

```bash
# 1. database
docker compose up -d

# 2. backend  → http://localhost:5080 (Swagger at /swagger)
cd backend
dotnet run --project src/Ovcuprim.Api

# 3. frontend → http://localhost:5173
cd frontend
npm install
npm run dev
```

Copy `.env.example` to `.env` before the first run. Secrets never belong in source control —
use `dotnet user-secrets` locally and environment variables in production.

## Running behind a reverse proxy

Every rate limit on the API partitions on the caller's IP address, so the API has to be told which
proxies are allowed to speak for a caller. It never guesses: with nothing configured, forwarded
headers are ignored and the connection address is used.

That gives two failure modes worth naming. Behind a proxy with nothing configured, every visitor
shares one bucket — "5 OTP requests per 15 minutes" becomes five for the whole site. Trusting
forwarded headers from anywhere is worse: `X-Forwarded-For` is then a header an attacker sets, and
every limit is free to bypass. So the trusted set is deployment configuration, supplied per
environment:

```jsonc
"ForwardedHeaders": {
  "KnownProxies":  ["10.0.0.4"],      // individual proxy addresses
  "KnownNetworks": ["10.0.0.0/16"],   // or the networks they sit in
  "ForwardLimit": 1                   // how many proxies stand in front of the API
}
```

Or as environment variables: `ForwardedHeaders__KnownNetworks__0=10.0.0.0/16`.

Notes:

- `ForwardLimit` must match the number of proxies in the chain. Only that many entries are read
  from the right of `X-Forwarded-For`, so a client-supplied prefix is never mistaken for the caller.
- A malformed address or CIDR fails at startup rather than being silently ignored.
- In Development, loopback is trusted automatically when nothing is configured, so local runs work
  unchanged.
- Outside Development, an empty configuration logs a warning at startup. That is legitimate for a
  host with no proxy in front of it; if there is one, the warning is telling you the limits are
  sharing a bucket.
- **Verify after deploying**: request a throttled endpoint (e.g. `/api/v1/auth/register`) from two
  different client addresses through the proxy and confirm they are metered separately.

## Phase status

Each phase closed with a read-only cross-phase audit and an approved remediation; the audit
documents live in `docs/`.

- [x] **Phase 1** — Project foundation
- [x] **Phase 2** — Authentication & users (phone + one-time code, refresh-token rotation)
- [x] **Phase 3** — Categories, attributes & regions (official 2024 region classifier in `docs/region-dataset.md`)
- [x] **Phase 4** — Listings & media
- [x] **Phase 5** — Search, filters, favourites & restricted-category gate
- [x] **Phase 6** — Stores / storefronts
- [x] **Phase 7** — Admin & operations panel
- [x] **Phase 8** — Pre-launch hardening (`docs/production-runbook.md`, `docs/deployment-verification.md`; remaining launch inputs listed there)
- [x] **Phase 9** — Paid listing promotions via Epoint (`docs/payment-integration-design.md`, `docs/payment-security-audit.md`, `docs/phase9-integration-audit.md`)
- [x] **Phase 10** — Admin payments/packages panel, seller promotion state and payment history, 8-hourly re-bump, duration snapshot (`docs/phase9-integration-audit.md` §G–H)
- [ ] Launch — production inputs only: Epoint and SMS credentials, hostnames, storage volume, backup cadence, ImageSharp licence status (`docs/production-runbook.md`, `docs/deployment-verification.md`)
