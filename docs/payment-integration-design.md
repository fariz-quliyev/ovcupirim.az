# Paid listing promotions — payment integration design

**Status: implemented per the approved design below, no production credential activated.** Epoint was
approved as the provider (section B) and is fully implemented behind `IPaymentGatewayClient`, with
backend unit/PostgreSQL/API test coverage and a full E2E suite (`frontend/e2e/09-promotions.spec.ts`),
all using a deterministic Development-only simulated gateway — no real network call to Epoint has ever
been made. See `docs/payments-epoint.md` for the adapter's own provenance, what remains unconfirmed
about Epoint's exact contract, and the checklist before any real credential is set. No
`PromotionPackage` rows are seeded — the catalog (names, durations, prices) is still an open business
decision (section I), and the table exists empty, ready for admin management via
`AdminPaymentsController`. This document is retained as the architecture record the implementation
follows; where a fact could not be found in the providers' own public documentation, it is marked
**[C]** (needs direct confirmation from the provider) rather than guessed.

Confidence labels used throughout, matching the convention from the Bumer.az SMS audit: **[A]** fact
found directly in an authoritative source (the provider's own docs), **[B]** reasonable inference or
third-party context that is not authoritative, **[C]** open question that needs direct confirmation
from the provider or from OvcuPrim's own business/legal side.

---

## A. Epoint vs Payriff comparison

Sources: [docs.payriff.com](https://docs.payriff.com) (platform guide + Gateway API reference) and
[developer.epoint.az](https://developer.epoint.az) (API reference), both read directly via live
browser rendering since neither serves static HTML to a plain fetch. Supplementary: 
[epoint.az](https://epoint.az/en) (business site, FAQ, licence footer),
[epoint-php SDK](https://github.com/rafoabbas/epoint-php).

| # | Criterion | Epoint | Payriff |
|---|---|---|---|
| 1 | Merchant onboarding | Register online; both individual entrepreneurs and legal entities accepted **[A]**. Required: director/entrepreneur name, TIN, bank details, "to draw up the contract after registration" **[A]**. Formal activation gate (if any) not documented **[C]**. | Multi-step dashboard wizard: account (email/phone/password + OTP) → merchant application (only "Business" type shown — individual-entrepreneur support not confirmed **[C]**) → App Info → Company Details (incl. Tax ID) → Bank Details → **Development** status **[A]**. |
| 2 | API authentication | Per-request signing: `public_key`/`private_key` pair, every call signs a `data` payload — see #5 **[A]**. | Static secret key in `Authorization` header, no signing, "No Bearer prefix required" **[A]**. |
| 3 | Hosted checkout / redirect | Create Payment → `redirect_url` to bank page; `success_redirect_url`/`error_redirect_url` for browser return **[A]**. | `POST /v3/orders` → `paymentUrl` (e.g. `sbpay.payriff.com/r/{id}`); redirect-return URLs also present on the Invoice API **[A]**. |
| 4 | Server-to-server callback | POST of `data`+`signature` to `result_url` **[A]**. `result_url` is **not** a Create Payment request parameter in the documented schema — appears to be a merchant/account-level setting, not per-order **[A/C — inferred from its absence in the request schema; needs confirmation]**. | `callbackUrl` **is** a per-order Create Order field **[A]** — more natural fit for a platform that may want to route callbacks per concern later, though OvcuPrim only needs one endpoint today. |
| 5 | Signature/payment verification | **Documented and concrete**: `signature = base64(sha1(private_key + data + private_key))`, applied identically to outbound requests and inbound callbacks — recompute server-side and compare **[A]**. | **Not documented.** No HMAC/signature scheme appears anywhere in the API reference (checked every page). Payriff's own guidance instead is: "the merchant verifies the final status with `GET /v3/orders/:id` before fulfilling the order" — i.e., don't trust the callback body, always re-fetch **[A — stated Payriff guidance, not an inferred gap]**. |
| 6 | Transaction/status lookup | `POST` status endpoint with `public_key`+`transaction` (signed) → `status` (`success/failed/new/returned/error/server_error`), `code`, `amount`, `rrn`, `trace_id` — 6 status values **[A]**. | `GET /v3/orders/:id` → `paymentStatus`, a 14-value enum (`CREATED/APPROVED/CANCELED/DECLINED/REFUNDED/PREAUTH_APPROVED/EXPIRED/REVERSE/PARTIAL_REFUND/PARTIAL/ACCEPTED/REFUND_IN_PROGRESS/CASH/PENDING/PREAUTH_EXPIRED`) with numeric codes **[A]** — materially richer, useful if pre-auth/partial-refund is ever needed. |
| 7 | Idempotency support | No documented `Idempotency-Key` mechanism. `order_id` is caller-supplied (required, max 255 chars) but retry-safety of reusing it is undocumented **[C]**. | No documented idempotency mechanism either; Payriff **generates its own** `orderId` — Create Order takes no caller-supplied external id at all **[A]**. |
| — | *(conclusion for #7)* | **For both providers**, dedup of a duplicate order-creation call, and dedup of a re-delivered webhook, is entirely OvcuPrim's own responsibility. Neither provider's docs give us a shortcut here — see Section F. | |
| 8 | Refund support | `/refund` endpoint exists but its docs page is a stub (title + masked endpoint, no parameter table) — thinnest documentation gap found for either provider **[A page exists / C parameters undocumented]**. | Fully documented: `POST /v3/refund` — `amount` (full or partial), `orderId`, `refundReason`; explicit note that an uncaptured `PRE_AUTH` must use Reverse/Void instead **[A]**. |
| 9 | AZN support | AZN, USD, EUR, RUB **[A]**. | AZN, PKR, AED, SAR (reflects Payriff's Gulf/Pakistan expansion) **[A]**. Both support AZN as a first-class currency; the rest is irrelevant to an AZN-only marketplace. |
| 10 | Sandbox/test environment | **No mention found anywhere** in the 10 primary docs pages checked (searched each for "sandbox"/"test mode"/"test card" — zero hits) **[A — confirmed absence by direct text search, not just "didn't happen to see it"]**. Doesn't prove no sandbox exists — many AZ gateways hand out test credentials only after a signed agreement — but it's a real, concrete gap **[C]**. | **Explicit and immediate**: new accounts start in "Development" status with a published sandbox test card (`4000 0075 4601 2078`, CVV `893`, exp `04/29`, OTP `123456`) usable across "all Gateway API versions, including V3" **[A]**. |
| 11 | Fees/pricing | Not published. Marketing copy mentions a promotional **"0% bank commission on all online sales in the first month"** **[A]**, implying a commission applies afterward — rate never stated. | Not published anywhere checked **[A — confirmed absence]**. |
| — | *(market context, not provider-specific)* | Third-party, unverified: general Azerbaijani PSP commission commonly cited around 1.5–3.5%, card payments ~2.5–3.0% **[B — payatlas.com, third party, do not treat as either provider's actual rate]**. | |
| 12 | Production activation | No equivalent flow documented; onboarding described only as register → provide identity/TIN/bank details → "contract is drawn up" **[A]**. Whether there's a formal Development→Review→Online gate like Payriff's is **[C]**. | Explicit: sign the "Internet Acquiring Service Agreement" in-dashboard via **Asan İmza** or **Sima İmza** (Azerbaijan's national e-signature services) → status Development→Review → contact Payriff support → Online **[A]**. |
| 13 | Marketplace-specific constraints | Has a "Split Payment Request" endpoint in the checkout section (masked, mechanics undocumented) **[A exists / C mechanics]**; also has genuine card tokenization/recurring (`cardSave`, Card Registration + Execute Pay) **[A]** — out of scope per this phase's "no subscriptions" rule, but available later. | Split Payments is a **manual, dashboard-only wallet-to-wallet transfer** between two Payriff merchant wallets — the recipient must already hold their own registered Payriff wallet and signed contract; no API, no automatic per-order split ratio **[A]**. Not usable for (and not needed by) this phase's design, but relevant if a future seller-payout phase is ever built. |
| — | Regulatory status | Central Bank of Azerbaijan licence "№ ÖT-002" **[A — epoint.az footer]**. | EMI licence from the Central Bank of Azerbaijan, PCI DSS Level 1 **[A — docs.payriff.com]**. |
| — | Card-data handling | Hosted checkout; card data never touches the merchant server **[A]**. | Same — PCI DSS Level 1 hosted checkout **[A]**. Both structurally satisfy OvcuPrim's "never store card data" requirement regardless of which is chosen. |

---

## B. Recommended provider — with evidence, pending your approval

**No provider is chosen here.** This is a recommendation for you to approve or override, not a decision
already made — per your instruction not to choose silently.

**If a choice were needed today, the evidence leans toward Epoint**, for one specific reason: it is the
only one of the two whose callback/webhook has a **documented cryptographic signature scheme**
(`sha1(private_key + data + private_key)`, applied identically to requests and callbacks). That is the
sharpest concrete technical differentiator found in this research. Payriff's own documentation
instead tells integrators to skip signature verification and always re-confirm via `GET /v3/orders/:id`
— which is exactly what OvcuPrim's own requirement ("never trust payment success from the frontend";
"promotion activates only after verified server-side confirmation") would do anyway. So the practical
gap is narrower than it first looks: with either provider, the design in this document treats the
webhook purely as a "go check now" trigger and never as the source of truth by itself (Section F).
Epoint's HMAC still adds real defence-in-depth — it lets a forged or replayed callback be rejected
before the extra network round-trip to re-verify status, and it's the only one of the two mechanisms
that's actually specified in writing anywhere.

**Working against a same-day recommendation for Epoint specifically**: Payriff has a clearly documented
sandbox with an immediately usable test card, and Epoint's docs mention no sandbox at all — a real gap
for building and testing an integration before touching real money. Payriff's refund and status-lookup
APIs are also more thoroughly documented (Epoint's `/refund` page is a stub). And Epoint's callback URL
appears to be an account-level setting rather than a per-order parameter, which is a real question mark
for a platform application (see open decision #7 below) — though for a single-purpose promotions
feature with one callback path, that constraint may not matter in practice.

**Bottom line**: neither provider's public documentation resolves the questions that would actually
decide this — the real commission rate, the true KYC/onboarding requirements for OvcuPrim's specific
entity, and (for Epoint) whether a sandbox exists at all. Those need a direct conversation with each
provider before a final choice is safe to make. Section I lists this as an explicit open decision.

---

## C. Payment architecture

### Provider abstraction

```
Ovcuprim.Application.Abstractions.IPaymentGatewayClient
    Task<PaymentOrderCreationResult> CreateOrderAsync(PaymentOrder order, CancellationToken ct)
    Task<PaymentStatusResult> GetOrderStatusAsync(string providerOrderReference, CancellationToken ct)
    Task<PaymentRefundResult> RefundAsync(string providerOrderReference, decimal amount, string reason, CancellationToken ct)
```

One concrete adapter per provider in `Ovcuprim.Infrastructure.Payments` (e.g. `EpointPaymentGatewayClient`
or `PayriffPaymentGatewayClient`), registered through **exactly the opt-in pattern already proven by
`PoctgoyerciniSmsSender`**: `AddInfrastructure` checks the relevant config section is non-blank before
registering the real adapter; if it's blank, a safe placeholder (`UnconfiguredPaymentGatewayClient`,
mirroring `UnconfiguredSmsSender`) is registered instead and throws loudly on first use rather than
silently no-opping. Same `IHttpClientFactory` named-client pattern, same wire-contract records pinned
with `[JsonPropertyName]`, same dedicated exception type (`PaymentGatewayException`, mirroring
`SmsDeliveryException`) that never logs credentials/signing keys and always carries the provider's own
correlation id (`trace_id` for Epoint, `responseId` for Payriff) for support escalation.

### Two separate state machines (as required)

- **`PaymentOrder`/`PaymentTransaction`** — the Application-owned, provider-agnostic ledger of "did the
  seller pay". Provider-specific detail lives only inside `PaymentTransaction.PayloadJson` and the
  adapter; nothing above the adapter boundary knows Epoint's or Payriff's wire format.
- **`Promotion`** — the marketplace-owned "is this listing currently boosted" state. Connected to
  `PaymentOrder` by a nullable, unique `Promotion.PaymentOrderId` (nullable so a future non-paid,
  admin-granted promotion is possible without a redesign — see open decision #2), never merged into it.

### Existing entities/services to reuse

- **`ICurrentUser` + the `LoadOwnedAsync` ownership-check pattern** (`ListingService.cs:470-508`) — "seller
  may only purchase a promotion for their own listing" is the same anti-enumeration shape already used
  for listing edit/delete: an ownership mismatch returns `NotFound`, not `Forbidden`.
- **`Result`/`Result<T>`/`ResultError`** and `ApiControllerBase.FromResult`/`.Problem`/`.AdminOk` — no new
  error-handling convention needed.
- **`AuditLog` + a new typed append-only history table** (`PaymentTransaction`, playing the role
  `ModerationAction` plays for moderation) — written together, atomically, in the same
  `RecordAsync`-style method, exactly as `ListingModerationService.RecordAsync` does today.
- **`AuditPayload` helper** — mandatory for `PaymentTransaction.PayloadJson` (jsonb). The codebase
  learned this lesson the hard way once already (three services wrote plain strings into a jsonb column
  and passed on the in-memory test provider while breaking on real Postgres) — reusing the helper avoids
  repeating it.
- **The `ListingQuota` unique-index + `DbUpdateException`-catch pattern** — the direct template for
  webhook idempotency (Section F). Proven under real concurrent PostgreSQL load in
  `ListingQuotaPersistenceTests`; a payment feature needs the equivalent test.
- **`NotificationService`/`INotificationChannel`** — "promotion activated" / "payment failed" seller
  notifications reuse the exact `NotificationMessage` call shape `ListingModerationService` already uses
  for `"listing.approved"` etc., just with new `Type` strings (`"promotion.activated"`,
  `"promotion.payment_failed"`) and `EntityType = nameof(Promotion)`.
- **`Listing.BumpedAt`** — already exists, already drives every default sort order
  (`ListingSearchService`, `ListingSearchStore`'s raw SQL, three DB indexes), and its own doc comment
  already anticipates this: *"equals PublishedAt until the listing is renewed or promoted."* Nothing
  currently sets it that way — activating a "bump"-style promotion package should simply write
  `Listing.BumpedAt = now`, which needs **zero new query or sort-order code**. This is the single
  biggest piece of existing infrastructure this feature can reuse.
- **`ListingMaintenanceService`** (`BackgroundService`, hourly sweep, scoped `IAppDbContext`) — the
  direct template for a promotion-expiry sweep (`Promotion.Status = Active AND ExpiresAt <= now →
  Expired`), either as a new pass inside it or a sibling service of identical shape.
- **`decimal` + `HasPrecision(12,2)` + `Currency` fixed-length-3 "AZN" default** — the exact money
  convention `Listing.Price`/`Listing.Currency` already use; no new `Money` value type needed.
- **The `WindowPolicies` rate-limit table** (`AuthenticationSetup.cs`) — new named policies for order
  creation (seller-facing, per-client) and the callback endpoint (unauthenticated, per-address) slot in
  exactly like `ListingCreate`/`StoreApply` do today.
- **`AdminTaxonomyController`-style CRUD** — the template for `PromotionPackage` admin management
  (`AdminOk`/`AdminNoContent`, `Policies.Admin`, `no-store`).
- **`dotnet ef migrations add PhaseNPaymentsAndPromotions`**, same CLI/naming convention as every prior
  phase migration.

---

## D. Domain model (proposed — no code written)

### `PromotionPackage` (reference/catalog table — follows `Category`'s conventions)

```
int Id
string Code            // stable slug, e.g. "bump-7d" — not a marketing name
string NameAz
string? DescriptionAz
PromotionType Type      // enum, intentionally minimal for phase 1 (see open decision #2)
int DurationDays
decimal PriceAzn         // HasPrecision(12,2)
string Currency = "AZN"
bool IsActive
int SortOrder
CreatedAt / UpdatedAt    // AuditableEntity
```
No rows are seeded by this design — package names, durations, and prices are a business decision
(open decision #2), never invented here.

### `PaymentOrder` (Application-owned payment ledger — the aggregate root of the payment side)

```
Guid Id                        // Guid.CreateVersion7()
Guid SellerUserId
Guid ListingId
int PromotionPackageId
decimal AmountAzn               // snapshotted server-side from PromotionPackage.PriceAzn at
                                 // creation time — the frontend sends packageId only, never a price
string Currency = "AZN"
PaymentOrderStatus Status        // Created -> AwaitingPayment -> Paid | Failed | Expired | Canceled
                                 // Paid -> Refunded | PartiallyRefunded
string Provider                  // "Epoint" | "Payriff" — whichever is approved
string? ProviderOrderReference   // populated once CreateOrderAsync returns
DateTimeOffset? ExpiresAt        // unpaid-order timeout (open decision #3)
long Version                     // app-managed concurrency token, Listing.Version-style —
                                 // guards a webhook and a status-poll racing the same order
CreatedAt / UpdatedAt
```

### `PaymentTransaction` (append-only history — one row per gateway interaction, idempotency lives here)

```
Guid Id                          // Guid.CreateVersion7()
Guid PaymentOrderId
PaymentEventType EventType        // OrderCreated | CallbackReceived | StatusChecked |
                                  // RefundRequested | RefundConfirmed | VerificationFailed
string? ProviderReference         // the gateway's own transaction/rrn id where applicable
string? ProviderStatusRaw         // e.g. Epoint's "success"/"100", Payriff's "APPROVED"
decimal? AmountAzn
string PayloadJson                // jsonb, built exclusively through AuditPayload
CreatedAt
```
A unique index on `(PaymentOrderId, EventType, ProviderReference)` is the idempotency guard (Section F)
— a re-delivered callback for the same event/reference hits the constraint and is treated as
already-processed, not reapplied.

### `Promotion` (marketplace-owned state — deliberately separate from payment state)

```
Guid Id                          // Guid.CreateVersion7()
Guid ListingId
int PromotionPackageId
Guid? PaymentOrderId              // nullable + unique: one order funds at most one promotion;
                                  // nullable leaves room for a future non-paid grant without a
                                  // schema change (open decision #2)
PromotionStatus Status             // Pending | Active | Expired | Reversed
DateTimeOffset? ActivatedAt
DateTimeOffset? ExpiresAt
DateTimeOffset? ReversedAt
string? ReversedReason
CreatedAt
```
A unique partial index (`WHERE Status = 'Active'`) on `ListingId` enforces at most one active promotion
per listing at a time — whether stacking/extension should ever be allowed instead is open decision #4.

`Listing` itself gains **no new column** beyond reusing the existing `BumpedAt`. `Listing.Status` is
never touched by promotion logic — a listing stays `Active` (or whatever its moderation status already
is) regardless of promotion state, exactly as required.

---

## E. API contract (proposed — no controllers written)

**Public**
- `GET /api/v1/promotion-packages` — active packages only (`id`, `code`, `nameAz`, `durationDays`,
  `priceAzn`); cached like other public catalog reads (`CachedOk`).

**Seller-facing, `[Authorize]`, ownership-checked via `LoadOwnedAsync`-style scoping**
- `POST /api/v1/me/listings/{listingId}/promotions/orders` — body: `{ packageId }` **only**. Loads the
  package price server-side, creates `PaymentOrder`, calls `CreateOrderAsync`, returns
  `{ paymentOrderId, redirectUrl }`.
- `GET /api/v1/me/payment-orders/{id}` — seller polls their own order after the redirect back; this call
  is a UX convenience only — it never gates activation.
- `GET /api/v1/me/payment-orders` — the seller's own order history.

**Provider-facing — no JWT, protected by signature verification + rate limiting**
- `POST /api/v1/payments/callback/{provider}` — verifies signature (where the provider documents one),
  looks up the order, checks the `PaymentTransaction` idempotency index, re-confirms via
  `GetOrderStatusAsync` before ever transitioning to `Paid`, activates the `Promotion` in the same
  transaction, always returns `200` quickly (including on an already-processed duplicate) so the
  provider stops retrying.

**Admin-facing, `[Authorize(Policy = Admin)]`**
- CRUD on `PromotionPackage` (mirrors `AdminTaxonomyController`).
- `GET /api/v1/admin/payment-orders` — reconciliation view, filterable by status.
- `POST /api/v1/admin/payment-orders/{id}/refund` — staff-triggered refund; records a
  `PaymentTransaction`, may reverse the `Promotion` per whatever business rule is approved (open
  decision #5).

---

## F. Security / idempotency design

- **Signature verification before any DB write.** Epoint: recompute
  `sha1(private_key + data + private_key)` and compare. Payriff: no documented scheme, so the callback
  body is never trusted at all — it is only a trigger to call the authoritative `GET /v3/orders/:id`.
- **Idempotency via unique index, not an idempotency-key header** (neither provider documents one) —
  the `PaymentTransaction (PaymentOrderId, EventType, ProviderReference)` unique index, caught via
  `DbUpdateException`, is the exact `ListingQuota` pattern already proven under real PostgreSQL
  concurrency. A duplicate delivery for an already-decided order is logged and answered `200 OK`
  without reapplying any effect. A duplicate for an order that is still open — because the first
  processing found the gateway's status endpoint not yet final, or unreachable — is reconciled
  again: the claim is what makes activation happen at most once, not what stops a retry from
  finishing the job (integration audit H-1).
- **Only a final gateway answer decides anything.** The re-check's `Pending`/`Unknown` outcomes leave
  the order open and are answered `503`, so a gateway that retries on non-2xx comes back; the expiry
  sweep re-polls such an order once more before retiring it. `Failed`/`Refunded` fail the order;
  `Paid` activates it — or, if the order had already expired, records it as `PaidAfterExpiry` for a
  refund, never an activation.
- **Double-activation prevention.** `Promotion.Status` can only be set to `Active` from inside the same
  transaction as the `PaymentOrder → Paid` transition, gated by "is this order already `Paid`" — moving
  `Paid → Paid` is a idempotent no-op, mirroring `NotificationService.MarkReadAsync`'s "already in the
  target state = silent success" convention, not an error.
- **Payment failure never activates a promotion** — no other code path is permitted to set
  `Promotion.Status = Active`.
- **Refund reversing a promotion is explicit, not automatic** — recorded as a `PaymentTransaction`
  event; whether/when it flips `Promotion.Status = Reversed` is itself the open business rule in
  decision #5. The schema supports either policy without change.
- **Webhook attack/replay risks**:
  - Rate-limit the callback endpoint even though it's unauthenticated (per-address, reusing the
    `WindowPolicies` table pattern — it has no operator identity to partition by).
  - Never reveal via response content or timing whether a given `ProviderOrderReference` exists — a
    duplicate or unrecognised reference gets the same generic `200`.
  - Validate payload size/shape before parsing (the base64+JSON envelope on Epoint's side, the JSON body
    on Payriff's) — reject oversized/malformed bodies before they reach business logic.
  - Log only the provider's own correlation id (`trace_id`/`responseId`) — never signatures, private
    keys, or full raw payloads — the same convention `PoctgoyerciniSmsSender`/`SmsDeliveryException`
    already established for the SMS gateway.
- **Double-payment/double-order scenarios**: a seller double-clicking "buy" must not create two
  `PaymentOrder` rows for the same purchase intent — this is entirely OvcuPrim's responsibility with
  either provider (neither documents order-creation idempotency). Proposed guard: a short-lived
  application-level lock or a "one `AwaitingPayment` order per listing+package at a time" unique
  constraint, to be finalised at implementation time.

---

## G. Promotion lifecycle

```
Pending  (PaymentOrder created, awaiting payment)
   │  webhook or status-check confirms Paid, inside one transaction
   ▼
Active   (ActivatedAt = now, ExpiresAt = now + package.DurationDays,
          Listing.BumpedAt = now  — reuses existing sort-order infrastructure)
   │
   ├── hourly sweep (ListingMaintenanceService-style BackgroundService):
   │   ExpiresAt <= now  →  Expired
   │
   └── admin refund action, per approved business rule (open decision #5)  →  Reversed
```
`Listing.Status` is never read or written by any of the above — a listing stays exactly whatever
moderation status it already has; promotion is purely additive.

---

## H. Exact implementation plan (not started — for approval before any code is written)

1. Domain entities (`PromotionPackage`, `PaymentOrder`, `PaymentTransaction`, `Promotion`) + enums +
   `AuditableEntity`/index conventions.
2. EF Core configurations (`Configurations/PaymentConfigurations.cs`) + `IAppDbContext`/`AppDbContext`
   `DbSet` additions + one migration (`dotnet ef migrations add PhaseXPaymentsAndPromotions`).
3. `IPaymentGatewayClient` abstraction in `Ovcuprim.Application.Abstractions` + the chosen provider's
   adapter in `Ovcuprim.Infrastructure.Payments`, opt-in DI exactly like `PoctgoyerciniSmsSender`, plus
   `UnconfiguredPaymentGatewayClient` placeholder.
4. `PromotionPackageService` + `AdminPromotionPackagesController` (admin CRUD, `AdminTaxonomyController`
   template).
5. `PromotionOrderService` (Application) — server-priced order creation, ownership-checked.
6. `PaymentCallbackService` — signature verification → authoritative status re-check → idempotent
   `Paid` transition → `Promotion` activation → notification dispatch, all one transaction.
7. `PromotionMaintenanceService` (or an added pass inside `ListingMaintenanceService`) — expiry sweep.
8. Controllers: public packages, seller orders/history, provider callback, admin ops/refund.
9. New rate-limit policies (`PromotionOrderCreate`, `PaymentCallback`) in the `WindowPolicies` table.
10. Tests:
    - `Ovcuprim.Application.UnitTests/Payments/` — service logic, in-memory provider.
    - `Ovcuprim.PostgresTests` — the idempotency unique-index race test, `ListingQuotaPersistenceTests`
      style (two concurrent callback deliveries for the same reference, exactly one takes effect).
    - `Ovcuprim.ApiTests` — HTTP contract: bad-signature rejection, cross-seller ownership rejection,
      frontend-supplied-price rejection (only `packageId` is ever accepted), duplicate-callback returns
      `200` without double-activating.
11. `docs/payments-{provider}.md` — integration contract record, `docs/sms-poctgoyercini.md` template.
12. `.env.example` + `appsettings.json` config section, `(DEPLOYMENT)`-tagged, opt-in-blank-safe.

All 12 steps are implemented, including the migration (`Phase9PaymentsAndPromotions`, applied to the
local development database) and full test coverage in all three backend suites plus a new E2E spec.
Nothing has been committed, pushed, or deployed, and no production Epoint credential has been set
anywhere.

---

## I. Open business decisions requiring your approval

1. **Epoint vs Payriff — final choice.** Section B gives a lean toward Epoint on documented-signature
   grounds, but real commission rate, true KYC requirements, and (for Epoint) sandbox availability are
   all **[C]** — unresolved without contacting each provider directly.
2. **What promotion packages actually exist** — names, durations, on-site effects (bump-to-top only?
   featured badge? homepage placement?), and their **prices**. Nothing is invented in this document;
   `PromotionPackage` is an empty, unseeded table in the proposal.
3. **Unpaid `PaymentOrder` expiry window** — no default is assumed here; needs a number.
4. **Can a listing hold only one active promotion at a time, or can packages stack/extend an existing
   one?** The proposed schema assumes "one at a time" (a unique partial index) but this is a business
   call, not a technical constraint.
5. **The exact refund → promotion-reversal rule** — does any refund reverse the promotion? Only a full
   refund? Is it time-prorated? The domain model supports any of these; none is chosen here.
6. **OvcuPrim's own onboarding details** — legal entity type, TIN, director identity, bank account for
   settlement — needed for either provider's merchant application regardless of which is chosen.
7. **Callback routing**: Payriff's per-order `callbackUrl` vs Epoint's apparent account-level
   `result_url` — worth confirming directly with Epoint whether it's actually configurable per request,
   since the docs only show it absent from the request schema, not explicitly stated as account-level.
8. **Direct provider contact still required** before any final decision: actual commission/fee
   percentage (neither publishes one), confirmed sandbox availability (Epoint), and the production
   activation process end-to-end for whichever is chosen.
