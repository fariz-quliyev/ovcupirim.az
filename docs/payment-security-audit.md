# Payment/promotion system — security audit

Read-only audit of the Phase 9 payment/promotion implementation (`docs/payment-integration-design.md`,
`docs/payments-epoint.md`). No code was changed to produce this document. Every finding below was
verified by reading the actual current source — `PromotionOrderService.cs`, `PaymentCallbackService.cs`,
`EpointPaymentGatewayClient.cs`, `PaymentAdminService.cs`, `PaymentConfigurations.cs`, the controllers,
and the existing test suites — not recalled from having written them.

## A. PASS

1. **Amount integrity.** `CreatePromotionOrderRequest` carries a `packageId` only — there is no field
   for a client to send a price through. `PromotionOrderService.CreateOrderAsync` loads
   `PromotionPackage.PriceAzn` server-side and snapshots it onto `PaymentOrder.AmountAzn` at creation.
   The only place a provider-reported amount is ever compared against anything is
   `PaymentCallbackService.ReconcileAsync`'s mismatch check, which **rejects** a disagreement rather
   than trusting it.
2. **Ownership / IDOR.** `CreateOrderAsync`, `GetMineByIdAsync`, and `GetMineAsync` all scope by the
   caller's own `UserId` in the query itself and return `NotFound` (never `Forbidden`) on a mismatch —
   the same anti-enumeration shape used everywhere else on this API. Confirmed both by code reading
   and by a passing E2E test (`a cross-user purchase attempt is rejected`).
3. **Signature verification.** `EpointSigning.Sign`/`EpointPaymentGatewayClient.VerifyCallback`
   implement Epoint's own documented scheme (`base64(sha1(private_key + data + private_key))`,
   confirmed against developer.epoint.az) and compare with `CryptographicOperations.FixedTimeEquals`
   — a genuine constant-time comparison, not `==` or `SequenceEqual`. An invalid signature is rejected
   before any database write beyond a single audit-log entry.
4. **The callback body is never trusted for status or amount.** `verified.Status`/`verified.Amount`
   (decoded from the callback's own `data`) are used only for the `PaymentTransaction` record — the
   Paid/Failed decision comes exclusively from a fresh `gateway.GetOrderStatusAsync(...)` call, and
   that call uses **our own stored** `order.ProviderOrderReference`, never anything the callback itself
   claims. A forged or stale callback cannot point the status check at a different order or transaction.
5. **Idempotency (identical-delivery replay).** Proven under real concurrent PostgreSQL load
   (`PaymentTransactionPersistenceTests.Two_concurrent_deliveries_of_the_same_callback_race_and_only_one_is_claimed`):
   two racing deliveries of the same reference produce exactly one `CallbackReceived` row, exactly one
   `Paid` transition, exactly one `promotion.activated` audit entry.
6. **The idempotency index is correctly scoped.** `IX_PaymentTransactions_CallbackIdempotency` filters
   on `EventType = 1` (CallbackReceived) specifically — a legitimate repeated `StatusChecked` (a seller
   polling, an admin reconciling) is never blocked. Verified in both the index definition and a
   dedicated unit test asserting a second callback after `Paid` is a safe no-op.
7. **Expired and Failed orders cannot activate a promotion.** `ReconcileAsync` branches on `Expired`
   before ever calling the gateway, and only ever sets `Promotion.Status = Active` from
   `PromotionStatus.Pending` — never from `Expired` or `Reversed`. Verified in unit tests, PostgreSQL
   tests, and E2E (`an expired order cannot activate a promotion even with a confirming callback`).
8. **Refund unconditionally reverses the promotion.** Both `PaymentAdminService.RefundAsync`'s code
   and its tests (unit + E2E) confirm a full or partial refund always sets
   `Promotion.Status = Reversed` when the promotion was Active or Pending, with no path that leaves a
   refunded order's promotion Active.
9. **One active promotion per listing, enforced twice.** Once at the service layer
   (`CreateOrderAsync`'s pre-check) and once by the database itself
   (`IX_Promotions_ListingId_Active`, a partial unique index on `Status = 1`) — the database-level
   enforcement is proven directly against real PostgreSQL
   (`A_second_active_promotion_for_the_same_listing_is_refused_by_the_database_itself`).
10. **Promotion activation touches only `BumpedAt`.** Grepped the entire `Payments` folder for any
    write to `Listing.Status` — none exists. A listing's moderation status is never read or written by
    any payment code path.
11. **Secrets/logging.** Read every `logger.Log*` call in `EpointPaymentGatewayClient.cs` — none logs
    the public/private key, the raw `data` payload, or the signature; only the request path, the HTTP
    status code, and the gateway's own status/code strings.
12. **Admin RBAC and rate limiting.** `AdminPaymentsController` carries
    `[Authorize(Policy = AuthenticationSetup.Policies.Admin)]` and
    `[EnableRateLimiting(AuthenticationSetup.RateLimits.AdminAction)]` at the controller level, covering
    package CRUD, order listing, and refund uniformly.
13. **Audit coverage.** Every state transition inspected writes an audit entry: order created/failed,
    paid, failed, amount-mismatch, late-payment-after-expiry, status-check-failed,
    refund-requested/confirmed/failed, `promotion.activated`, `promotion.reversed`, package
    created/updated, signature-invalid, order-id-unrecognised, order-not-found.
14. **Credentials are environment-only.** `EpointGatewayOptions.PublicKey`/`PrivateKey` have no default
    value in code; `appsettings.json` ships them blank; registration is opt-in
    (`DependencyInjection.cs` only wires the real client when both are non-blank, otherwise
    `UnconfiguredPaymentGatewayClient` throws loudly). No real credential exists anywhere in this
    repository.

## B. SECURITY GAP

1. **[Meaningful] No listing-status/eligibility check on order creation.** `CreateOrderAsync` checks
   ownership only — never `listing.Status`. A seller can create and pay for a promotion order against
   a listing that is `Draft`, `PendingModeration`, `Rejected`, `Blocked`, `Sold`, or `Expired`, not just
   `Active`. Because the public search/browse ordering only ever reads `BumpedAt` for listings already
   filtered to `Status = Active`, the immediate practical effect is a wasted purchase rather than a
   privilege escalation — but it directly misses the audit's own ask ("blocked/expired/sold listing
   interactions are safe and **consistent with existing listing rules**"), and it lets a
   currently-Blocked listing hold `Promotion.Status = Active` with nothing reconciling the two, which
   would become a real inconsistency the moment any future feature displays a "promoted" badge without
   independently checking listing status.
2. **[Serious] A rare cross-order race can leave a genuinely-paid order stuck as unpaid, with no
   automatic recovery.** `CreateOrderAsync`'s "no existing active promotion / no existing pending
   order" checks are two ordinary `AnyAsync` reads with no locking — a classic TOCTOU window. Two
   concurrent requests for the *same listing* (two tabs, a double-click, or a scripted race) can both
   pass both checks and each create their own `PaymentOrder`+`Promotion` pair. If both are then paid,
   both reach `ReconcileAsync`'s final activation step; the database's own
   `IX_Promotions_ListingId_Active` correctly refuses the second one — but as a generic
   `DbUpdateException` (a unique-constraint violation), not the `DbUpdateConcurrencyException` that
   `ReconcileAsync`'s `catch` block actually handles. The exception propagates unhandled, the whole
   `SaveChangesAsync` (including the `order.Status = Paid` assignment) rolls back, and
   `GlobalExceptionHandler` turns it into a bare 500. Because `TryClaimCallbackAsync` already
   **durably** recorded the `CallbackReceived` row in its own earlier, separate save, any retry of that
   same callback (which is exactly what a gateway does after a 500) is now recognised as
   "already claimed" and **never reaches `ReconcileAsync` again**. The order is left permanently
   `AwaitingPayment` even though Epoint genuinely captured the money, with no path back to a correct
   state short of an operator noticing and intervening by hand.
3. **[Moderate] No cumulative-refund tracking — repeated partial refunds can together exceed the
   original payment.** `PaymentAdminService.RefundAsync` validates `amount > order.AmountAzn` against
   the **original total** every time, not against what remains after a prior partial refund. Nothing on
   `PaymentOrder` or elsewhere tracks how much has already been refunded. Two sequential partial
   refunds (e.g., 8 AZN then another 8 AZN out of a 10 AZN order) both pass this check. Whether
   Epoint's own API would independently reject the second call is unconfirmed —
   `docs/payments-epoint.md` already flags the refund endpoint's exact contract as unverified — so this
   cannot be relied on as the only safeguard. This requires Admin-level access to trigger; it is not
   reachable by an ordinary seller or an unauthenticated caller.
4. **[Minor, not fixable here] SHA1 is the callback-signing algorithm** — this is Epoint's own
   mandated scheme (confirmed against their documentation), not a choice this codebase made. Recorded
   for completeness against the audit's callback-security question, and already tracked as an open
   question for Epoint in `docs/payments-epoint.md`.
5. **[Minor, already tracked] `EpointGatewayOptions`'s default `BaseUrl` and three endpoint paths are
   unverified community-SDK conventions**, not confirmed against Epoint's own authenticated reference —
   already documented in `docs/payments-epoint.md`. An availability/reliability risk (wrong path means
   every production request fails at the HTTP layer) more than a confidentiality one; repeated here
   because it is directly relevant to "callback security" being asked about in the same breath.

## C. Missing tests

1. No test proves two partial refunds cannot together exceed the original payment amount — because the
   guard itself does not exist (B.3), a test asserting this would currently fail, which is exactly the
   point of writing one.
2. No PostgreSQL test exercises **two different `PaymentOrder`/`Promotion` pairs for the same listing**
   both racing to activate (as opposed to two deliveries of *one* callback, which *is* tested). This is
   the scenario that would expose B.2's unhandled-exception path directly.
3. No test asserts that a promotion order is refused for a listing that is not `Active` — consistent
   with that check not existing (B.1).
4. No "credentials/signature never appear in a logged line" test for `EpointPaymentGatewayClient`,
   unlike the precedent already set for the SMS gateway
   (`PoctgoyerciniSmsSenderTests.The_credential_and_the_message_never_appear_in_a_logged_line_on_any_path`).
   The current code is correct by inspection (A.11), but a regression here would go undetected without
   a dedicated test the way the SMS one exists specifically to catch that class of mistake.
5. No test exercises `VerifyCallback` with a signature of the wrong length (confirms the length
   short-circuit ahead of `FixedTimeEquals` still rejects cleanly rather than throwing) or a
   non-base64 `data` value from an otherwise-plausible request.
6. No test documents the accepted behaviour when Epoint's status-check response omits `amount` — the
   mismatch check is silently skipped when `AmountAzn` is null. Worth an explicit test recording this
   as intended, not overlooked.
7. No test exercises a payment callback arriving for an order whose listing has since been
   soft-deleted — `IgnoreQueryFilters()` in `ProcessCallbackAsync` exists specifically for this, but
   nothing currently proves it works or would catch a regression if that call were ever "cleaned up."

## D. Fix-required items

1. **(High)** Gate `PromotionOrderService.CreateOrderAsync` on `ListingStateMachine.IsPubliclyVisible(listing.Status)`
   (or an equivalent explicit check) before allowing an order — addresses B.1, directly named in the
   audit scope.
2. **(High, but narrow trigger)** In `PaymentCallbackService.ReconcileAsync`'s final `SaveChangesAsync`,
   also catch the unique-constraint case (a plain `DbUpdateException`, distinct from
   `DbUpdateConcurrencyException`) and record the order in a distinguishable, operator-visible state
   instead of silently rolling back to `AwaitingPayment` — addresses B.2. The trigger condition is rare
   (requires the TOCTOU race in item 1 to actually produce two paid orders for one listing), but the
   failure mode when it does happen is a stuck, unreconciled payment.
3. **(Medium)** Track cumulative refunded amount on `PaymentOrder` (or derive it from prior
   `RefundConfirmed` transactions) and validate a new refund against the **remaining** balance, not the
   original total — addresses B.3.
4. **(Recommended, not blocking)** Add the credentials-never-logged regression test named in C.4.

## E. Overall payment security verdict

The paths reachable by an external attacker or an ordinary seller acting maliciously are sound: amount
integrity, ownership/IDOR, signature verification, "never trust the callback body," and single-delivery
idempotency are all correctly implemented and independently verified (code reading + unit + PostgreSQL
+ E2E). Someone without a valid Epoint signature can affect nothing; someone without a seller's own
session can affect nothing about another seller's listing or order.

The three gaps found all sit behind meaningfully higher bars than "attacker with no special access":
B.1 needs a seller acting on their own non-eligible listing (a business-logic/consistency issue, not an
authorization bypass); B.2 needs a specific, narrow concurrent-request race that ordinary single-click
usage will essentially never hit, and even then it **fails closed** — no promotion is wrongly granted
and no money is lost, an operator simply has to notice and manually reconcile a stuck order; B.3
requires Admin-level access, already the most trusted role in the system.

**Verdict: conditionally safe.** Safe to continue toward defining the commercial catalog now; B.1 and
B.2 should be fixed before any production Epoint credential is ever activated, since B.2 in particular
carries a real financial-reconciliation cost even though it cannot be used by anyone to steal a
promotion or a payment.

## F. Safe to proceed to the commercial `PromotionPackage` catalog?

**Yes.** Defining package names, durations, and prices is a business decision orthogonal to all three
gaps found — none of them relate to how the catalog itself is structured, and populating the catalog
would not make any of them better or worse. Recommend fixing B.1 and B.2 before production credentials
are set, in parallel with (not blocking) catalog definition.
