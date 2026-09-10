# Phase 1–9 cross-phase integration audit (payments & promotions)

Read-only audit of how the Phase 9 payment/promotion domain integrates with the rest of the system —
listing lifecycle, moderation, maintenance sweeps, admin panel, seller UI, deployment configuration and
operations documentation. It is the same handoff audit every earlier phase closed with; Phase 9 had a
security audit (`docs/payment-security-audit.md`) but not this one.

No production code, schema, data, configuration or dependency was changed to produce this document.
Every finding was verified against the current source, and the ones marked **confirmed by probe** were
reproduced with temporary tests (in-memory harness and real PostgreSQL) that were deleted afterwards.
Everything else in the Phase 1–8 surface was re-verified only where Phase 9 touches it.

Audit date: 2026-09-09.

## A. Verdict

The payment core is sound — the security audit's fourteen PASS items still hold, and the integration
points that were checked deliberately (routes, RBAC, rate limits, cache headers, ownership,
idempotency, concurrency tokens, migrations, Development-only simulator, E2E cleanup order) all pass.

One **HIGH** correctness defect was found in the callback path: a status re-check that comes back as
*not yet final* is treated as *failed*, which is terminal, so a genuinely captured payment can end up
unactivated and unrefundable through the system. It must be fixed before any production Epoint
credential is activated. Five MEDIUM and five LOW items follow; none blocks code sign-off, but M-1 and
M-3 belong in the same pre-credential batch as H-1.

## B. PASS — integration points re-verified

1. **Route layout.** `POST /api/v1/me/listings/{guid}/promotions/orders`, `GET /api/v1/me/payment-orders[/{guid}]`,
   `GET /api/v1/promotion-packages`, `POST /api/v1/payments/callback/{provider}`, and the five
   `/api/v1/admin/promotion-packages*` / `/api/v1/admin/payment-orders*` routes collide with nothing in
   `MeListingsController`, `MeStoreController`, `NotificationsController` or the other admin controllers.
2. **RBAC.** `AdminPaymentsController` carries the Admin policy at class level; `MePromotionsController`
   is `[Authorize]` and resolves everything through the caller's own id; the callback and the public
   catalog are anonymous by design and protected by signature verification / read-only content.
3. **Rate limits.** `PromotionOrderCreate` (20 / 10 min per address), `PaymentCallback` (120 / min per
   address — Epoint's own), `AdminAction` (300 / min per operator) are in `WindowPolicies` with
   Development overrides only in `appsettings.Development.json`.
4. **Cache headers.** Public catalog: `public, max-age=900` + content ETag via `CachedOk`. Every admin
   response: `no-store`. Seller order reads: no cache header (plain `Ok`), consistent with every other
   `/me` endpoint.
5. **Ownership / anti-enumeration.** Unchanged since the security audit; `CreateOrderAsync` still answers
   `NotFound` for another seller's listing and `Conflict` only for the caller's own ineligible listing.
6. **Idempotency and concurrency.** `IX_PaymentTransactions_CallbackIdempotency` and
   `IX_Promotions_ListingId_Active` still exist in the model; `PaymentOrder.Version` is bumped in
   `AppDbContext.SaveChangesAsync` alongside `Listing.Version` / `Store.Version`.
7. **Migrations.** `dotnet ef migrations has-pending-model-changes` → *No changes have been made to the
   model since the last migration.* Snapshot and the two Phase 9 migrations agree.
8. **Environment isolation.** `DevelopmentPaymentGatewayClient`, `IPaymentGatewaySimulator` and the three
   `/api/v1/dev/payments/*` endpoints exist only when `IsDevelopment()`; outside it the gateway is
   Epoint (both keys present) or `UnconfiguredPaymentGatewayClient`, which throws
   `InvalidOperationException` — deliberately not `PaymentGatewayException` — so a missing configuration
   surfaces as a 500, never as a quietly failed order.
9. **Listing state is never written by the promotion side.** Activation touches `Promotion` in one save
   and `Listing.BumpedAt` in a separate best-effort save; `Listing.Status` is untouched everywhere.
10. **Notification flush.** All four payment-side `NotifyAsync` call sites stage the row before a save
    that commits it (the bug fixed in the security-audit remediation has not regressed).
11. **E2E hygiene.** `global-setup.ts` deletes `PaymentTransactions → Promotions → PaymentOrders →
    PromotionPackages(e2e-bump-%)` before `Listings`, matching the `Restrict` foreign keys.
12. **Maintenance sweeps.** `ListingMaintenanceService` and `PromotionMaintenanceService` use separate
    scopes and separate `DbContext` instances; neither depends on the other's ordering.

## C. Findings

### HIGH

**H-1 — A non-final status re-check permanently fails the order, and a captured payment then has no
path to activation or refund.** *(confirmed by probe)*

`PaymentCallbackService.ReconcileAsync` decides `if (status.Status != PaymentGatewayPaymentStatus.Paid)
→ order.Status = Failed`. The gateway abstraction distinguishes `Pending` (Epoint `new`/`pending`),
`Unknown` (any status string the adapter has never seen) and `Failed`, but the callback service collapses
the first two into the third. `Failed` is terminal: the next delivery for the same order returns from the
"already decided" guard at the top of `ReconcileAsync` **without calling the gateway again**, and
`PaymentAdminService.RefundAsync` refuses anything that is not `Paid`/`PartiallyRefunded`.

Reproduced: callback → re-check says `new` → order `Failed`; gateway becomes consistent and delivers
again → re-check count stays at 1, order stays `Failed`, promotion stays `Pending`; admin refund →
`409 Yalnız ödənilmiş sifariş geri qaytarıla bilər.` The seller has paid, sees "Ödəniş uğursuz oldu",
and nobody can put it right without Epoint's dashboard. A re-check that comes back `Unknown` (a status
value Epoint adds tomorrow) has exactly the same effect.

The trigger is the ordinary eventual-consistency gap between a PSP's webhook and its status endpoint,
plus the deliberately conservative "re-check before trusting" design — the two interact badly only
because the undecided outcomes are mapped to a terminal one.

Fix: treat `Pending`/`Unknown` as *undecided* — record the `StatusChecked` row, leave the order
`AwaitingPayment`, and make sure something re-checks later: answer the gateway with a non-2xx if Epoint
retries on non-2xx (its retry policy is not documented in `docs/payments-epoint.md` and must be
confirmed with the provider), and in any case have the expiry sweep re-poll orders that hold a provider
reference before expiring them (see M-1). Only `Failed`/`Refunded` may transition the order to
`Failed`. Regression tests: pending re-check leaves the order open and a later confirming delivery
activates; unknown re-check likewise; an existing `failed` re-check still fails.

### MEDIUM

**M-1 — Late payment after expiry is recorded but unrecoverable through the system.**

`ReconcileAsync`'s `Expired` branch writes `payment_order.late_payment_after_expiry` and returns; the
approved rule ("an expired order never activates") is honoured, but the money has been captured and:
the order cannot be refunded through `RefundAsync` (status is `Expired`, not `Paid`); the seller is not
notified; nothing tells an operator to act except an audit row nobody is paged on. Two things widen the
window: `PromotionMaintenanceService` expires `Created`/`AwaitingPayment` orders by the clock alone,
never asking the gateway first, so a lost or delayed callback for a customer who paid at minute 29
becomes this case; and the 30-minute `PendingOrderLifetime` has not been checked against how long
Epoint's hosted checkout session actually stays open.

This needs a product decision, not just code: either (a) a late-but-confirmed payment activates the
promotion if the listing is still `Active` (what a seller would expect), or (b) it is marked as a
captured-late order that is refunded automatically or by an operator. Either way `RefundAsync` must
accept an order with a confirmed capture regardless of the `Expired` label, and the expiry sweep should
re-check status with the gateway for orders that hold a provider reference before expiring them.

**M-2 — `Payments:FrontendBaseUrl` does not fail fast.**

`appsettings.json` ships it blank by design, like `AllowedHosts` — but unlike `AllowedHosts`, storage
and the secrets, nothing validates it at startup (`PaymentOptions` is bound with a plain
`Configure`, no `ValidateOnStart`). A production host with both Epoint keys set and this value blank
starts cleanly and sends Epoint `success_redirect_url = "/promotions/orders/{id}/return?outcome=success"`
— a relative URL that either fails at Epoint or strands the customer after paying. Fix: when the Epoint
gateway is registered, require an absolute `https` URL and refuse to start otherwise, with a
`StartupConfigurationTests` case; add the key to `deployment-verification.md` §1.

**M-3 — Operations have an API but no surface, and the ledger has neither.**

What exists: package CRUD, `GET /admin/payment-orders?status=`, `POST /admin/payment-orders/{id}/refund`.
What does not: an order-by-id endpoint, any exposure of `PaymentTransactions` (the append-only ledger
that reconciliation and disputes are about is reachable only by SQL), filters by seller / listing /
provider reference / date, any payment metric on the dashboard, and any admin UI at all — `AdminLayout`
has no payments entry and `features/admin/api.ts` has no payment call. Today a refund means Swagger or
curl with a bearer token, and finding the right order means SQL. Recommended scope for the next phase:
an admin "Ödənişlər" page (orders table with filters, order detail with its transactions, refund dialog
reusing `ActionDialog`), a "Paketlər" page over the existing CRUD, and pending-refund / captured-late
counters on the overview.

**M-4 — The seller cannot see that a promotion is active, and the UI hides the server's reason when a
second purchase is refused.**

`SellerListingDto` / `ListingSummary` carry no promotion state, so `MyListingsPage` renders "İrəli çək"
on every `Active` listing, including one already promoted. Pressing it produces
`409 Bu elan artıq irəli çəkilib.`, which `PromotePackageDialog` reduces to the generic "Sifariş
yaradıla bilmədi. Yenidən cəhd edin." even though `ApiError.problem.detail` holds the real message. There
is also no purchase-history view although `GET /me/payment-orders` already exists. Fix: add
`promotion { status, expiresAt }` to the seller listing DTO and show "İrəli çəkilib · {tarix}-dək" instead
of the button; render `problem.detail` in the dialog; add a "Ödənişlərim" list under the cabinet.

**M-5 — A package edit changes what an already-placed order buys.** *(confirmed by probe)*

`PaymentOrder` snapshots `PriceAzn` but not `DurationDays`; both activation paths
(`PaymentCallbackService.TryActivatePromotionAsync`, `PromotionRecoveryService`) read
`order.PromotionPackage.DurationDays` at activation time. Reproduced: seller orders a 3-day package,
admin edits the package to 30 days before the callback lands, promotion activates for 30 days. The
reverse (30 → 3) shortens what the seller paid for. Fix: snapshot `DurationDays` onto `PaymentOrder` (or
`Promotion`) at order creation — one column, one migration, both activation paths read the snapshot.

### LOW

**L-1 — Required navigations to the filtered `Listing` entity (latent today).** *(confirmed by probe on
PostgreSQL)*

EF Core warns at every startup — event 10622, visible in the production JSON log — that `Listing` has a
global query filter and is the required end of `PaymentOrder.Listing` and `Promotion.Listing`. The
consequences were reproduced against a real database: `PaymentAdminService.GetOrdersAsync` silently
drops an order whose listing has `DeletedAt` set while `Total` still counts it (the page reports 2, shows
1); `PromotionRecoveryService` never sees such a deferred order, so its promotion stays `Pending`
forever with no error logged. `PaymentCallbackService` already guards this with `IgnoreQueryFilters()`
(and has a test for it); its two siblings do not. Latent because the only code that sets
`Listing.DeletedAt` today is `ListingService.DeleteAsync` for a `Draft` and the abandoned-draft sweep,
and a Draft can never hold an order — it becomes live the day account deletion or administrative
listing deletion is built. Fix: `IgnoreQueryFilters()` on every financial query, matching the callback
path; financial records must never vanish from a reconciliation view.

**L-2 — Listing and promotion lifecycles are independent — a decision to record, not a defect.**

Nothing in `ListingModerationService.BlockAsync`, `ListingService.MarkSoldAsync` / `DeleteAsync` or the
expiry sweep reads or writes `Promotions`. A paid promotion on a blocked, sold, deleted or expired
listing stays `Active` until its own `ExpiresAt`, with no reversal and no refund; a restored listing
carries it along. Tap.az does not refund bumps either, so this may well be the intended policy — but it
is not written down anywhere, and the seller is not told at purchase. Needs an explicit statement (and
purchase-dialog wording if the policy is "no refund"). If an administrative block should auto-refund,
that is a small addition to `BlockAsync` calling the existing refund path.

**L-3 — What the seeded durations actually buy.**

`PromotionType.Bump` sets `Listing.BumpedAt = now` once at activation; `DurationDays` only fixes
`Promotion.ExpiresAt` and therefore how long the one-active-promotion lock holds. Nothing re-bumps
during the paid days. So `bump-15d` (4 AZN) delivers the same single lift as `bump-3d` (1 AZN) plus a
longer lock, and a seller buying `bump-3d` five times in a row gets five lifts for 5 AZN over the same
15 days. The descriptions ("N gün ərzində irəli çəkilmiş elan kimi saxla") read as sustained placement,
which the mechanism does not provide; Tap.az's "İrəli çək" is N repeated bumps at a fixed interval.
Product decision, explicitly outside Phase 9's approved scope: either periodic re-bump for `Active`
promotions from `PromotionMaintenanceService` (set `BumpedAt = now` every N hours while active — a small
change plus tests, and the natural reading of the current descriptions), or reword the descriptions to
match a one-time bump.

**L-4 — Operations documentation does not know payments exist.**

`docs/production-runbook.md` has no payment section: the Epoint `result_url` must be configured on the
merchant dashboard to `POST /api/v1/payments/callback/epoint`; the three unverified endpoint paths must
be confirmed; there is no reconciliation or refund procedure; and the audit actions an operator must act
on (`payment_order.late_payment_after_expiry`, `payment_order.amount_mismatch`,
`payment_order.status_check_failed`, `promotion.activation_deferred`, `payment_order.refund_failed`) are
not listed anywhere. `docs/deployment-verification.md` §1 lacks the `Payments__*` keys and §8 has no
callback-reachability smoke test. The runbook's "Remaining launch blockers" predates the SMS and region
resolutions. `README.md`'s phase checklist still shows only Phase 1 complete.

**L-5 — Callback failures are answered 400/200 without distinguishing "retry later".**

`PaymentCallbackController` maps only `ResultError.Validation` to 400; a `status_check_failed`
(gateway unreachable during the re-check) comes back as `Conflict` and is answered **200**, which tells
Epoint the delivery was accepted. The order stays `AwaitingPayment`, so nothing is corrupted, but the
only thing that will ever complete it is another delivery Epoint has no reason to send — until the
expiry sweep turns it into M-1. Answering 5xx for a transient re-check failure (so a gateway that
retries on non-2xx does so) plus the sweep-side re-poll closes this; it is the same change H-1's fix
needs for the undecided case.

## D. What was not found

- No new IDOR, RBAC, rate-limit, cache-privacy or secret-logging regression.
- No route ambiguity introduced by the `/me` split across three controllers.
- No migration drift.
- No cross-sweep interference between listing and promotion maintenance.
- No regression in the 14 security-audit PASS items.

## E. Recommended order of work

1. **Before any production Epoint credential:** H-1, L-5 (same change), M-2, and the `RefundAsync`
   half of M-1 (a confirmed capture must always be refundable). One remediation batch with regression
   tests; re-run the payment security audit checklist afterwards.
2. **Product decisions to take now** (they change code and copy): M-1's late-payment rule, L-2's
   block/sold/delete policy, L-3's re-bump vs. rewording.
3. **Next implementation phase (admin & seller surfaces):** M-3, M-4, M-5, L-1 — all self-contained,
   none touches the payment security architecture.
4. **Documentation:** L-4, alongside whichever of the above lands first.

## F. Verification run

| Check | Result |
|---|---|
| `dotnet build` (backend solution) | succeeded |
| `dotnet ef migrations has-pending-model-changes` | no pending changes |
| Frontend `npm run lint` | clean |
| Frontend unit tests (`vitest`) | 199 passed / 25 files |
| Frontend `npm run build` | succeeded |
| Backend unit tests (`Ovcuprim.Application.UnitTests`) | 529 passed |
| Backend API integration tests (`Ovcuprim.ApiTests`) | 247 passed |
| Backend PostgreSQL tests (`Ovcuprim.PostgresTests`) | 42 passed |
| Playwright E2E (`npx playwright test`, 25 tests incl. the 7 promotion flows) | 25 passed (4.8 min) |
| **Total** | **1,042 passed, 0 failed, 0 skipped, 0 fixme** |

All probes were deleted before these runs; the working tree holds no test added by this audit.

**Local-environment caveat, worth knowing before trusting a green run on this machine.** Windows
Smart App Control (Code Integrity events 3077/3033) blocked the freshly built, unsigned
`OvcuprimPostgresTests.dll` from loading into the test host — and `dotnet test` at solution level
then reported "No test is available", **exit code 0**, and simply omitted that project from the summary.
A solution-level run can therefore look green while the PostgreSQL suite never executed. The 42/42
above came from rebuilding that one project with `-p:Deterministic=false` (a different hash gets a
different reputation verdict) and running it alone. CI on Ubuntu is unaffected; locally, check that
three `Passed!` lines appear, not two.

## G. Remediation status — batch 1 (2026-09-09)

Implemented immediately after the audit, as the pre-credential batch recommended in §E.1. Product
decisions were not taken: the approved rule "an expired order never activates a promotion" is
unchanged; late-payment handling below is the technical half of M-1 only.

| Finding | Status | What changed | Pinned by |
|---|---|---|---|
| H-1 | **Fixed** | `PaymentCallbackService.ReconcileAsync` treats `Pending`/`Unknown` as *undecided*: records the re-check, audits `payment_order.status_undecided`, leaves the order `AwaitingPayment`, returns `ResultError.Unavailable`. Only `Failed`/`Refunded` fail an order. A resent delivery whose idempotency claim is refused is still reconciled while the order is open. | `PaymentCallbackServiceTests` (pending → open → resend activates; unknown; outage), `PaymentTransactionPersistenceTests.A_resent_delivery_of_an_undecided_callback_still_reconciles_once_the_gateway_is_final` (real unique index) |
| L-5 | **Fixed** | `ResultError.Unavailable` → HTTP 503 (`ApiControllerBase`); `PaymentCallbackController` answers 503 for undecided/unreachable, 400 for a bad signature, 200 otherwise. | `PromotionEndpointsTests` (503 then 200 on resend; outage 503) |
| M-1 (technical half) | **Fixed** | New `PaymentOrderStatus.PaidAfterExpiry`: a capture confirmed after expiry is recorded, the seller is notified (`promotion.payment_late`), the order is refundable through `RefundAsync`, and `PromotionRecoveryService` ignores it. `PromotionMaintenanceService` re-checks status with the gateway (`IPaymentCallbackService.ReconcileOrderAsync`) before expiring an `AwaitingPayment` order. Frontend return page explains the outcome. | `PaymentCallbackServiceTests`, `PaymentAdminServiceTests`, `PromotionRecoveryServiceTests`, `PromotionEndpointsTests` (sweep completes a captured order / retires an undecided one; admin refund), `PromotionReturnPage.test.tsx`, E2E flow 10 "expired order" |
| M-1 (product half) | **Open** | Whether a late-but-confirmed capture should instead activate when the listing is still Active; whether the 30-minute window matches Epoint's checkout session. | — |
| M-2 | **Fixed** | `AddInfrastructure` refuses to start when the Epoint keys are set but `Payments:FrontendBaseUrl` is not an absolute http(s) origin. | `DeploymentHardeningTests` (blank / relative / schemeless / ftp refused; https and http accepted; not required without keys) |
| L-4 | **Done** | `docs/payments-epoint.md` gained the callback-response and late-capture section; `docs/production-runbook.md` gained §8 "Payments (Epoint) — operator reference"; `docs/deployment-verification.md` §1 lists the payment keys and §8 gained the callback-reachability smoke test; `.env.example`, `docs/payment-integration-design.md` §F and the README phase list updated. | — |

Unchanged: M-3, M-4, M-5, L-1, L-2, L-3 — the seller/admin surfaces and the two product decisions.

**Verification after batch 1 (fresh runs, 2026-09-09):** backend unit 538, API integration 261,
PostgreSQL 43, frontend unit 200 (lint and typecheck clean, build succeeded), Playwright E2E 25 —
**1,067 passed, 0 failed, 0 skipped.** No schema change was needed (`PaidAfterExpiry` is a new enum
value in an existing `smallint` column; `has-pending-model-changes` still reports none).

## H. Decisions taken and batch 2 (2026-09-09, same day)

The user delegated the open decisions ("hər şeyi sənə buraxıram"). Taken as follows, each on the
same basis the project has always used — Tap.az is the product reference, the mechanism must be
honest, and explicitly approved rules stay approved:

| Decision | Outcome | Why |
|---|---|---|
| M-1 (product half) — late-but-confirmed capture | **Keep the approved rule: never activates.** Recorded as `PaidAfterExpiry`, refunded by an operator. | The rule was explicitly approved; with the sweep now re-checking the gateway before expiry the case is rare; an automatic gateway refund would lean on Epoint's unverified refund contract. Revisit only if `PaidAfterExpiry` turns out to be frequent in production. |
| L-2 — promotion when the listing is blocked, sold, deleted or expires | **No automatic reversal, no refund; the seller is told before paying.** `PromotePackageDialog` now shows: "Elan silinsə, satılsa və ya qaydaları pozduğuna görə bloklansa, ödəniş geri qaytarılmır." An operator can still refund case-by-case from the panel. | Tap.az's rule for paid services; the listing that leaves the site simply lets its promotion run out. |
| L-3 — what a package's days buy | **Periodic re-bump: every 8 hours while the promotion is Active** (`PromotionStateMachine.BumpInterval`), applied by `PromotionMaintenanceService`; the public catalog and the seller UI say so (`bumpIntervalHours`). The seeded descriptions ("N gün ərzində irəli çəkilmiş elan kimi saxla") are now true. | Tap.az's "İrəli çək" is a repeated bump at a fixed interval; a single bump plus a lock made the longer packages worse value than the shortest. |

### Batch 2 — implemented

| Finding | Status | What changed | Pinned by |
|---|---|---|---|
| M-5 | **Fixed** | `PaymentOrder.DurationDays` snapshot (migration `Phase10PromotionDurationSnapshot`, backfilled from packages); both activation paths and the seller/admin DTOs read it via `PromotionDurations.For`. | `PaymentCallbackServiceTests.A_package_edit_after_purchase_does_not_change_what_the_seller_bought` |
| L-1 | **Fixed** | `IgnoreQueryFilters()` on the admin reconciliation query, the order detail, the refund lookup and the recovery sweep. | `PaymentAdminServiceTests` (soft-deleted listing still listed), `PromotionRecoveryServiceTests` (still recovered) |
| L-3 | **Done** | 8-hourly re-bump of Active promotions' listings; `BumpIntervalHours` on `PromotionPackageDto` and `ListingPromotionDto`. | `PromotionEndpointsTests.The_sweep_re_bumps_an_active_promotions_listing_every_interval_until_it_expires` |
| M-3 | **Done** | Admin API: `GET /admin/payment-orders/{id}` (order + promotion + full `PaymentTransactions` ledger), `?q=` search (listing №, gateway reference, seller name/phone), `LateCaptures` on the overview. Admin UI: "Ödənişlər" page (filters, table, detail pane with ledger, refund dialog with optional partial amount) and "Paketlər" page (create/edit, retire by switching off); sidebar badge and dashboard card for late captures. | `PromotionEndpointsTests` (detail, search, no-store), `AdminPanel.test.tsx` (payments + packages), E2E flow 10 "operator … refunds it" |
| M-4 | **Done** | `ListingSummaryDto.Promotion` for the seller's own list; "Mənim elanlarım" shows "İrəli çəkilib · until · every N hours" and hides the buy button while one runs; the dialog shows the server's own refusal reason, the bump interval and the refund policy; new "Ödənişlərim" page (`/kabinet/odenisler`) over `GET /me/payment-orders`. | `PromotionEndpointsTests.The_seller_sees_the_running_promotion_on_their_own_listing`, `MyListingsPage.test.tsx`, `MyPaymentsPage.test.tsx`, E2E flow 10 (seller row + payment history) |
| L-2 | **Decided + copy** | Policy sentence in the purchase dialog; no code path reverses a promotion on block/sold/delete. | `MyListingsPage.test.tsx` |

Remaining after batch 2: nothing from this audit. Production go-live inputs (Epoint credentials and
account questions, SMS credentials, hostnames, storage volume, backup cadence, ImageSharp licence
status) are unchanged and listed in `docs/production-runbook.md` and `docs/deployment-verification.md`.

**Verification after batch 2 (fresh runs, 2026-09-09):** backend unit 542, API integration 264,
PostgreSQL 43, frontend unit 208 (lint and typecheck clean, build succeeded), Playwright E2E 26 (one
new flow: the operator finds, inspects and refunds an order from the panel) — **1,083 passed,
0 failed, 0 skipped.** One migration added (`Phase10PromotionDurationSnapshot`, applied to the dev
database; `has-pending-model-changes` reports none). Nothing committed, pushed or deployed.
