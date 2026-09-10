# Payment gateway — Epoint

`EpointPaymentGatewayClient` (`backend/src/Ovcuprim.Infrastructure/Payments/EpointPaymentGatewayClient.cs`)
is an `IPaymentGatewayClient` adapter for `epoint.az`. This document is the provenance for the
request/response contract it implements, what is confirmed vs. inferred about it, and the checklist
to work through before any production credential is ever set. See also
`docs/payment-integration-design.md` for the full architecture design this adapter implements.

## What this is, and is not

The adapter is a pure transport: given an order (amount, description, redirect URLs), it asks Epoint
to open a hosted checkout and returns a URL; given a callback, it verifies the signature and decodes
it; given a provider reference, it re-checks status or requests a refund. It does not decide when a
promotion activates — `PaymentCallbackService` does that, only after this adapter's `VerifyCallback`
passes **and** a fresh `GetOrderStatusAsync` call confirms `Paid`. It never stores card data; Epoint's
hosted checkout means card details never reach OvcuPrim's server at all.

## The gateway contract

Source: Epoint's own developer portal (`developer.epoint.az`), read directly — not from a
third-party SDK.

- **Authentication/signing**: every request signs a `data` parameter — `base64(JSON of params)` —
  with `signature = base64(sha1(private_key + data + private_key))`. The same scheme verifies an
  inbound callback: recompute the signature from the callback's own `data` and compare, and only
  trust the callback if it matches. **[A — confirmed in developer.epoint.az/authentication and
  /callbacks]**
- **Create Payment**: `public_key`, `amount`, `currency`, `language`, `order_id`, `description`,
  `success_redirect_url`, `error_redirect_url` → `{ status, redirect_url, transaction, trace_id }`.
  **[A]**
- **Get Status**: `public_key`, `transaction` → `{ status, code, message, transaction,
  bank_transaction, amount, rrn, trace_id }`. Status values: `success`, `failed`, `new`, `returned`,
  `error`, `server_error`. **[A]**
- **Callback**: POST of `data`+`signature` to whatever URL is configured as the account's
  `result_url`. Decoded body carries `order_id`, `status`, `code`, `transaction`,
  `bank_transaction`, `rrn`, `amount`, `trace_id`. **[A]**
- **Refund**: an endpoint exists (`/refund` in the developer portal's navigation), but its documentation
  page is a stub — a title and a masked endpoint, with no parameter table. This adapter's
  `RefundAsync` sends `public_key`, `transaction`, `amount`, mirroring the shape every other endpoint
  uses, but **the exact refund request/response contract is not independently confirmed**. **[C —
  verify against an authenticated merchant account before relying on it in production]**

## Exact endpoint paths — unverified

The developer portal masks its literal endpoint paths behind an authenticated "reveal" control this
research could not pass. `EpointGatewayOptions` defaults to the paths published by Epoint's own
community PHP SDKs (`rafoabbas/epoint-php`, `TuralAsgar/epoint`) and referenced in third-party
integration write-ups:

| Setting | Default | Confidence |
|---|---|---|
| `BaseUrl` | `https://epoint.az` | **[B]** — widely used by community SDKs, not independently confirmed |
| `CreateOrderPath` | `/api/1/request` | **[B]** |
| `StatusPath` | `/api/1/get-status` | **[B]** |
| `RefundPath` | `/api/1/refund-request` | **[C]** — no public reference found; inferred from the pattern of the other two |

**Action before production**: confirm these three paths against an authenticated Epoint merchant
account or direct provider contact, and update `Payments:Epoint:CreateOrderPath` /
`:StatusPath` / `:RefundPath` via configuration if they differ — no code change is needed either way,
since these are configuration-bound, not hardcoded.

## `success_url`, `error_url`, and `result_url`

Three distinct URLs are involved, and they are not interchangeable:

- **`success_redirect_url`** and **`error_redirect_url`** — sent as part of every `Create Payment`
  request (`PromotionOrderService.CreateOrderAsync`, via `PaymentGatewayOrderRequest`). These are
  **browser-return URLs only** — where Epoint's hosted checkout sends the customer's browser back
  to, currently `{Payments:FrontendBaseUrl}/promotions/orders/{orderId}/return?outcome=success|error`.
  `PromotionReturnPage` on the frontend never trusts the `outcome` query string as proof of anything —
  it polls the backend's own `GET /me/payment-orders/{id}` for the verified status. This is
  deliberate: a browser redirect can be skipped, replayed, or forged by the customer, so it carries no
  authority.
- **`result_url`** — the server-to-server callback URL, delivered independently of the browser. Per
  Epoint's Create Payment parameter table, **`result_url` is not a per-request parameter** — it does
  not appear anywhere in the documented request body. This strongly suggests it is configured **once,
  on the merchant account itself**, in whatever dashboard Epoint provides — not sent by this adapter
  at all. **[A/C — the absence from the request schema is directly observed; whether it is truly
  account-level and un-overridable per request is not independently confirmed]**.

**Action before production**: whoever sets up the Epoint merchant account must configure
`result_url` to point at this API's `POST /api/v1/payments/callback/epoint` (the exact production
hostname depends on deployment). If Epoint's account dashboard turns out to allow per-application
callback URLs rather than a single account-wide one, note that too — it would remove a constraint,
not create one.

## Configuration

| Key | Environment variable | Required outside Development |
|---|---|---|
| `Payments:Epoint:PublicKey` | `Payments__Epoint__PublicKey` | Yes, to activate this gateway |
| `Payments:Epoint:PrivateKey` | `Payments__Epoint__PrivateKey` | Yes, to activate this gateway |
| `Payments:Epoint:BaseUrl` | `Payments__Epoint__BaseUrl` | No — defaults to `https://epoint.az` |
| `Payments:Epoint:CreateOrderPath` / `StatusPath` / `RefundPath` | matching env vars | No — see the unverified-paths table above |
| `Payments:FrontendBaseUrl` | `Payments__FrontendBaseUrl` | Yes — see below |

**Activation is opt-in, not automatic.** `AddInfrastructure`
(`backend/src/Ovcuprim.Infrastructure/DependencyInjection.cs`) registers `EpointPaymentGatewayClient`
only when both `PublicKey` and `PrivateKey` are present and non-blank outside Development. If either
is missing, `UnconfiguredPaymentGatewayClient` stays registered and throws loudly on the first attempt
to create an order, rather than silently failing. **No production credential has been set anywhere in
this repository at any point during this work.**

`Payments:FrontendBaseUrl` has no safe default outside Development (it is blank in
`appsettings.json`, the same convention `AllowedHosts` uses) — deploying without it produces a
relative, likely-wrong redirect URL. Development overrides it to `http://localhost:5173` in
`appsettings.Development.json`.

## Sandbox vs. production credentials

**No sandbox environment is documented anywhere in Epoint's developer portal** — ten primary pages
were checked directly for "sandbox", "test mode", and "test card"; none were found. This is a real,
confirmed gap in the public documentation (see docs/payment-integration-design.md, section A #10),
not merely something this research happened to miss. **This must be confirmed directly with Epoint
before any integration testing against their real API can happen** — whether test credentials are
issued separately, whether the production credentials themselves have a "test mode" flag, or whether
sandbox access requires a signed agreement first.

Until that is resolved, this backend uses **`DevelopmentPaymentGatewayClient`**
(`backend/src/Ovcuprim.Infrastructure/Payments/DevelopmentPaymentGatewayClient.cs`) for all local
development and automated testing (unit, PostgreSQL, API, and E2E). It never makes a network call,
never reads a real credential, and simulates the full checkout → callback → verification →
activation path deterministically, including genuine signature verification against a
Development-only fixed key. **Development never activates production credentials** — the two
gateway clients are mutually exclusive, chosen by `AddInfrastructure` based on the hosting
environment, never by configuration alone.

## Required Epoint merchant onboarding / KYC values

From Epoint's public FAQ (epoint.az), confirmed **[A]**:

- Both individual entrepreneurs and legal entities are accepted.
- Required to draw up the contract: **name, surname, patronymic, TIN (tax id), and bank details of
  the director/entrepreneur**.
- Cards accepted: Visa, Visa Electron, Mastercard, Maestro (Azerbaijani or international banks); a
  separate "AMEX Payment" endpoint exists in the API reference, suggesting Amex is handled distinctly.

**Not found publicly** — needs direct confirmation with Epoint or whoever manages OvcuPrim's own
business registration:

- Whether a formal "Development → Review → Online" application-status gate exists the way Payriff's
  does, or whether Epoint's activation is a single step.
- Any document checklist beyond the FAQ's summary (registration certificate, ID copies, etc. — typical
  for AZ payment gateways but not itemized in what was checked).

## Remaining Epoint business questions — none resolved by this implementation

None of the following were answered by public research and are **explicitly not assumed** anywhere in
this adapter or its configuration:

- **Standard commission / fee rate.** Not published anywhere. A marketing page mentions a promotional
  "0% commission in the first month," implying a normal rate applies afterward — the rate itself is
  never stated. Third-party, non-authoritative market context put general Azerbaijani PSP commissions
  around 1.5–3.5%; this is not Epoint's actual rate and must not be treated as one.
- **Settlement timing** — how long after a payment funds actually reach OvcuPrim's bank account. Not
  documented publicly.
- **Refund fees/rules** — whether Epoint charges a fee for processing a refund, any time limit on
  when a payment can be refunded, and the exact refund request contract (see "Exact endpoint paths"
  above).
- **Sandbox access** — see above; unresolved.
- **Production activation process end-to-end** — whether it mirrors Payriff's digital-signature
  contract flow (Asan İmza / Sima İmza) or is simpler; not documented for Epoint.

**Before setting real values in production**, confirm all of the above directly with Epoint, and:

- [ ] `PublicKey`/`PrivateKey` are the production credentials for whichever entity holds the
      merchant account — never a value copied from any test or sample.
- [ ] `result_url` is configured on the Epoint merchant dashboard to point at this API's
      `/api/v1/payments/callback/epoint`.
- [ ] `CreateOrderPath`/`StatusPath`/`RefundPath` are confirmed against the authenticated developer
      portal or direct provider contact, not left on their unverified defaults.
- [ ] `Payments:FrontendBaseUrl` is set to the real production frontend origin.
- [ ] Commission rate, settlement timing, and refund policy are understood well enough to represent
      them accurately to sellers, if OvcuPrim ever surfaces that information in its own UI.

## Callback responses, retries and late captures

What `POST /api/v1/payments/callback/epoint` answers, and what each answer means to the gateway:

| Response | When | What the gateway should do |
|---|---|---|
| `400` | Signature does not verify | Nothing — the delivery was not from the configured key |
| `503` | Signature verified, but the authoritative status re-check could not decide: Epoint's own status endpoint was unreachable, or reported a non-final state (`new`/`pending`, or a value this adapter has never seen) | Retry later — the order is left open, nothing was changed |
| `200` | Everything else: processed, already processed, an order id that does not exist, or a late callback for an order that had already expired | Stop retrying |

**Only a final answer decides anything.** `success` activates (or records a late capture, below);
`failed`/`error`/`server_error`/`returned` fail the order; anything else leaves it exactly where it
was. This is deliberate (integration audit H-1): the gap between Epoint's webhook and its status
endpoint is ordinary eventual consistency, and reading it as a failed payment would be terminal —
the payment completing a moment later would be captured with no way to activate or refund it.

**A resend of an undecided delivery is processed again.** The `(PaymentOrderId, CallbackReceived,
ProviderReference)` idempotency row is written on the first delivery; on a resend the unique index
refuses it, but an order that is still open is reconciled again rather than answered "already done".
Activation still happens at most once — the order's own concurrency token and the one-active-
promotion index guarantee that, not the callback claim.

**The expiry sweep asks first.** `PromotionMaintenanceService` re-checks status with Epoint once for
every `AwaitingPayment` order whose 30 minutes are up before expiring it, so a lost or delayed
callback for a customer who actually paid completes the order instead of stranding it.

**Late captures — `PaidAfterExpiry`.** If Epoint confirms a capture only after the order was swept to
`Expired`, the approved rule still holds — the promotion never activates — but the money moved, so the
order is recorded as `PaidAfterExpiry`, the seller is told a refund follows, and an operator refunds
it through the ordinary path:

```bash
# Find them
GET  /api/v1/admin/payment-orders?status=PaidAfterExpiry
# Refund one (full refund when amount is omitted)
POST /api/v1/admin/payment-orders/{id}/refund   { "reason": "Captured after the order expired" }
```

`PromotionRecoveryService` ignores `PaidAfterExpiry` by construction (it only looks at `Paid`). The
same actions are available in the panel under **Ödənişlər** (filter "Gec ödəniş — refund gözləyir",
open the order, "Geri qaytar"); the dashboard and the sidebar badge count what is waiting.

**What an Active promotion does.** Beyond the lift at activation, `PromotionMaintenanceService` moves
the listing's `BumpedAt` to now every 8 hours (`PromotionStateMachine.BumpInterval`) until the paid
duration ends — the repeated bump Tap.az sells. The duration is frozen on the order at purchase
(`PaymentOrder.DurationDays`); editing a package afterwards affects only future orders.

**Audit actions an operator should watch** (all in `AuditLogs`, `EntityType = PaymentOrder`):
`payment_order.late_payment_after_expiry` (refund needed), `payment_order.amount_mismatch` (the gateway
confirmed a different amount than was ordered — never activated, needs a human), `payment_order.
status_check_failed` (Epoint unreachable during a re-check — self-healing on retry/sweep, but a burst
means an outage), `payment_order.status_undecided` (normal in small numbers; a large number means
Epoint's status endpoint is lagging badly), `promotion.activation_deferred` (self-healing — see the
security audit's B.2).

**Still to confirm with Epoint before go-live:** whether it retries a callback that was answered
`503` (and on what schedule), and how long its hosted checkout session stays open relative to the
30-minute `PendingOrderLifetime` — if the session can outlive the order, `PaidAfterExpiry` refunds will
be routine rather than rare, and the lifetime should be revisited.
