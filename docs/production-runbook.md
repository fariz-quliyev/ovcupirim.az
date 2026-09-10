# Production runbook

Operational reference for running OvcuPrim.az in production: storage, logging, health checks,
backup and restore, and the performance validation behind the current index set. Companion to
`docs/deployment-verification.md`, which is the pre-launch and post-config-change checklist; this
document is what an operator reaches for afterwards.

---

## 1. Media storage

**Decision:** local disk, behind `IFileStorage`, on a persistent volume mounted at a path outside
the deployed application directory. This is deliberate and production-usable for a **single API
instance**, not a placeholder — see `StorageSetup`'s remarks for the reasoning.

- Configure `Storage__Local__RootPath` to an **absolute** path, e.g. `/data/uploads`, mounted as a
  persistent volume in the deployment. The host refuses to start otherwise (verified live —
  see §5).
- `Storage__Local__PublicBaseUrl` is the URL prefix those files are served under; point it at
  wherever the reverse proxy / CDN serves the mounted volume from.

**Open blocker — do not resolve without an infrastructure decision:** this storage backend does
not survive running more than one API instance (a second instance never sees the first instance's
uploads) or a deployment without a persistent volume (an ephemeral container filesystem loses every
upload on redeploy). Reaching either point requires an S3-compatible object storage provider behind
the same `IFileStorage` interface — which provider, which region, and its credentials are a
deployment decision this repository does not make. Until that decision lands, keep the deployment to
one instance with a persistent volume, and back the volume up (§4).

## 2. Structured logging

Production writes one JSON object per log line to stdout (`Serilog.Formatting.Json.JsonFormatter`),
carrying `Timestamp`, `Level`, `MessageTemplate`, `Properties` (request path, status code, elapsed
time, exception details where present) and, for a request-scoped log, `TraceId`/`SpanId`. This is
what lets a log aggregator (CloudWatch, Loki, whatever the deployment uses) query fields instead of
parsing a text template. No specific aggregator is wired up — that is a deployment choice, not an
application one; point the container's stdout at whatever the deployment already collects logs with.

Development keeps the human-readable console template (see `appsettings.Development.json`).

Verified live on 2026-09-03: a Production-mode run emitted lines of the shape
`{"Timestamp":"...","Level":"Information","MessageTemplate":"HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms","Properties":{"RequestMethod":"GET","RequestPath":"/health","StatusCode":200,"Elapsed":63.6,...}}`.

## 3. Health and readiness

Two endpoints, deliberately different:

| Endpoint | Checks | Use |
|---|---|---|
| `GET /api/v1/health` | Nothing but that the process is answering requests | Liveness probe |
| `GET /health` | Database reachability (`AddDbContextCheck<AppDbContext>`) | Readiness probe |

Point a liveness probe (restart-on-failure) at `/api/v1/health` and a readiness probe
(remove-from-load-balancer-on-failure) at `/health` — a database outage should take the instance
out of rotation, not restart it.

Verified live on 2026-09-03:

- With the database reachable: `GET /health` → `200 Healthy`.
- With the database unreachable (connection string pointed at a closed port): `GET /health` → `503`
  after the connection timeout (~10s), logged as `HTTP GET /health responded 503`.

## 4. Backup and restore

The database is the source of truth; the uploads volume (§1) is backed up separately by whatever
mechanism backs up the persistent volume itself (a snapshot, typically) — this section covers the
database only.

### Procedure

```bash
# Backup (custom format — compressed, restorable selectively if ever needed)
pg_dump -h <host> -U <user> -d ovcuprim -Fc -f ovcuprim-$(date +%Y%m%d-%H%M).dump

# Restore into a NEW database — never directly over a live one
createdb -h <host> -U <user> ovcuprim_restored
pg_restore -h <host> -U <user> -d ovcuprim_restored ovcuprim-<timestamp>.dump
```

Cutting a live deployment over to a restored database is a deliberate, separate decision (repoint
`ConnectionStrings__Default`, restart the API) — this procedure only proves the backup is usable, it
does not perform a cutover.

### Restore drill — executed and verified, 2026-09-03

Run against the development container (`ovcuprim-postgres`), not a production instance:

1. Recorded baseline row counts: `Listings`=3, `Users`=6, `Categories`=58.
2. `pg_dump -Fc` → a 93 KB dump.
3. Restored into a throwaway database (`ovcuprim_restore_drill`) via `pg_restore`.
4. Verified: row counts matched exactly (`Listings`=3, `Users`=6, `Categories`=58,
   `Notifications`=4); `__EFMigrationsHistory` carried all 8 applied migrations; every index —
   including `ListingQuotas`' unique `(UserId, CategoryId, PeriodStart)` constraint and both new
   `Notifications` indexes — was present on the restored copy.
5. Dropped the throwaway database and removed the dump file.

**Not yet exercised:** a restore drill against a production-scale database (this drill was ~93 KB;
production will be larger) and a timed drill establishing an actual recovery-time figure. Both are
straightforward re-runs of the same procedure once there is a production database to draw from —
recorded here as follow-up, not invented as a number.

## 5. Secrets and configuration fail fast

See `docs/deployment-verification.md` §1 for the full table and how each was verified. Summary: the
host refuses to start in production if `ConnectionStrings__Default`, `Auth__Jwt__SigningKey`,
`Auth__Security__HashingKey` are missing/too short, if `AllowedHosts` is unset or `*`, or if
`Storage__Local__RootPath` is left at its relative development default. All five were verified live
by deliberately breaking each on a scratch instance.

## 6. Production-scale query performance

Every hand-written catalogue query already carries a PostgreSQL test proving it reaches its intended
index against a small corpus (`Ovcuprim.PostgresTests`); this section is that same claim re-checked
at the volume production will actually see, using the built-in synthetic data generator
(`dotnet run -- --seed-listings <count>`, development-only, refuses outside Development).

**Run: 2026-09-03, 100,000 listings, development container.**

| Query | Plan | Execution time |
|---|---|---|
| Category browse, newest first, page 1 | Index Scan, `IX_Listings_CategoryId_Status_BumpedAt` | 0.19 ms |
| Category browse, deep pagination (page 20) | Same index | 1.46 ms |
| Price range within a category | Same index | 0.72 ms |
| Trigram text search (`SearchKey ILIKE`) | Bitmap Index Scan, `IX_Listings_SearchKey` | 0.70 ms |
| Attribute numeric range (`weight` between 1 and 5) | Bitmap Index Scan, `IX_Listings_Attr_weight` | 5.12 ms |
| Capped total count, broadest filter (`Status = 2` only) | Index Only Scan | 0.27 ms |
| Facets: per-category counts across ~80k active rows | Index Only Scan | 17.19 ms |

No sequential scan appeared in any plan. All seven are inside any reasonable release latency budget
by a wide margin — the tightest, the facets aggregation, is still under 20 ms scanning the full
active set. Per the approved performance rule ("no unbounded or unjustified sequential scan on a hot
request path that causes the measured performance threshold to fail"), there is nothing here to fix:
the planner reached for the intended index in every case, and every case comfortably clears the
budget. Data was purged after the run (`dotnet run -- --purge-listings`); the database returned to
its baseline row count.

## 7. Rate limits, cache and concurrency contracts

Unchanged by G3/G4 except where a decision explicitly asked for a new one (the listing quota, B-1).
Re-verified by the full regression suite after every change in this phase — see the test totals in
the phase report. Nothing in the rate-limit windows, the public cache/privacy rules, or the
`Listing.Version`/`Store.Version` concurrency tokens was touched.

## 8. Payments (Epoint) — operator reference

Companion to `docs/payments-epoint.md`, which holds the gateway contract and the pre-credential
checklist; this section is what an operator does once payments are live.

### Configuration and fail-fast

| Key | Effect |
|---|---|
| `Payments__Epoint__PublicKey` / `Payments__Epoint__PrivateKey` | Both set outside Development ⇒ the Epoint adapter is active. Either blank ⇒ the refusing placeholder stays and any purchase attempt fails loudly (500), never silently. |
| `Payments__FrontendBaseUrl` | Absolute public origin of the frontend. **Required whenever the keys are set** — the host refuses to start otherwise (a blank value would send Epoint a relative browser-return URL). |
| Epoint merchant dashboard: `result_url` | Must point at `https://<api-host>/api/v1/payments/callback/epoint`. Not configurable from this side. |

### What runs on its own

- Every minute, `PromotionMaintenanceService`: re-checks each `AwaitingPayment` order whose 30 minutes
  are up with Epoint **before** expiring it (a captured one completes; an undecided one expires),
  retires promotions whose paid duration ran out, and retries deferred activations.
- Every callback: signature check → authoritative status re-check → activate / fail / leave open.
  Answers `503` when Epoint's own status endpoint cannot decide, so Epoint retries.

### What needs a person

| Signal | Where | Action |
|---|---|---|
| Orders in status `PaidAfterExpiry` | `GET /api/v1/admin/payment-orders?status=PaidAfterExpiry` | Money captured after the order expired; the promotion will never activate. Refund: `POST /api/v1/admin/payment-orders/{id}/refund` with a reason (full refund when `amount` is omitted). The seller has already been told a refund follows. |
| `payment_order.amount_mismatch` in the audit log | `GET /api/v1/admin/audit` | Epoint confirmed an amount different from the order. Never activated. Reconcile against the Epoint dashboard; refund if captured. |
| A burst of `payment_order.status_check_failed` | audit log / structured logs | Epoint unreachable. Self-healing (retries + sweep), but sustained means an outage on their side. |
| Seller complaint "paid but not promoted" | `GET /api/v1/admin/payment-orders?status=Paid` / `AwaitingPayment` | A `Paid` order with a `Pending` promotion is a deferred activation (another promotion held the slot) and recovers on its own; an `AwaitingPayment` order past its window is waiting for the sweep. Anything else: check the order's audit rows. |

All of the above is available in the panel: **Ödənişlər** (`/admin/payments`) lists every order with a
status filter and a search box (listing №, gateway reference, seller name or phone), opens one with
the promotion it funded and its complete ledger (`PaymentTransactions`), and refunds it — fully, or a
partial amount validated against what is left. The dashboard card and the sidebar badge count the
`PaidAfterExpiry` orders waiting. **Paketlər** (`/admin/packages`) creates and edits the catalog;
nothing is ever deleted — a package that funded an order is referenced by it forever — so retiring
one means switching it off. The raw API calls above remain valid for scripting.

### What a package delivers

An Active promotion lifts its listing to the top of the default ordering at activation and again
**every 8 hours** (`PromotionStateMachine.BumpInterval`) until its paid duration runs out — the
repeated bump Tap.az sells, not a single one. Price and duration are frozen on the order at purchase;
editing a package afterwards changes only future orders. A listing that is blocked, sold, deleted or
expires keeps its promotion until the promotion's own end with no automatic refund — the seller is told
so before paying. Refund case-by-case from the order's detail pane if a situation warrants it.

## Remaining launch blockers (deployment/infrastructure input required)

- **Media storage beyond one instance** — §1. Needs an object-storage provider decision.
- **Production SMS provider** — not configured; `UnconfiguredSmsSender` throws outside Development
  by design, so a production deployment without one fails fast rather than silently not sending
  codes. Needs a provider decision and credentials.
- **Production proxy/hostname values** — `AllowedHosts` and `ForwardedHeaders:KnownProxies` /
  `KnownNetworks` are deployment facts (the real domain names, the real load balancer addresses)
  and are deliberately left unset in source control. Configure at deploy time.
- **Prohibited-item screening content** — see `docs/screening-blockers.md`.
- **Production-scale backup/restore timing** — §4's "not yet exercised" note.
