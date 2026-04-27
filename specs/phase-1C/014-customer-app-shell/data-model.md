# Phase 1 Data Model: Customer App Shell

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)
**Date**: 2026-04-27

> **Scope reminder**: This spec is **UI only** (FR-031). No new server-side entities are introduced; everything below is **client-side view-models** and **state machines** that consume Phase 1B contracts. Server-side entities (Account, Cart, Order, etc.) live in their owning Phase 1B specs.

---

## Client-side view-models

Each view-model is an immutable Dart class produced from a server response and consumed by widgets. View-models exist to (a) decouple widget code from generated OpenAPI types so a contract change in 1B is one mapping-file edit, and (b) carry UI-only fields (e.g., `isRestrictedAndUnverified`) that don't exist on the wire.

### `Session`

| Field | Type | Source | Notes |
|---|---|---|---|
| `accessToken` | `String?` | spec 004 sign-in / refresh | Null when guest. |
| `refreshToken` | `String?` | spec 004 sign-in / refresh | Null when guest. Stored only in `flutter_secure_storage`, never in memory after read. |
| `expiresAt` | `DateTime?` | derived from JWT `exp` | Null when guest. |
| `customerId` | `String?` | spec 004 sign-in | Null when guest. |
| `marketCode` | `String` | spec 004 (account) OR `MarketResolver` | Always non-null — guests get the resolver default. |
| `email` | `String?` | spec 004 | Null when guest. |
| `displayName` | `String?` | spec 004 | Null when guest. |
| `isVerified` | `bool` | spec 020 (when wired) | False until 020 confirms; treated as "unverified" gate per FR-021. |

### `CartViewModel`

| Field | Type | Source | Notes |
|---|---|---|---|
| `revision` | `int` | spec 009 cart response | Server-side monotonic revision used for the "cart updated elsewhere" notice (FR-022a). |
| `lines` | `List<CartLineViewModel>` | spec 009 | One entry per cart line. |
| `restrictedEligibility` | `Map<String, bool>` | spec 009 | Per-product-id eligibility flag. |
| `stockSignals` | `Map<String, StockSignal>` | spec 009 | `inStock` / `low` / `outOfStock` per line. |
| `totals` | `PriceBreakdown` | spec 007 | Subtotal, discounts (per source), tax (per market rate), shipping placeholder, grand total. |
| `tokenKind` | `enum {anonymous, authenticated}` | client | Anonymous before login, authenticated after. |

### `CartLineViewModel`

| Field | Type | Source | Notes |
|---|---|---|---|
| `productId` | `String` | spec 009 | |
| `sku` | `String` | spec 009 | |
| `name` | `String` | spec 009 / 005 | Localized — comes from spec 005 product `name_<locale>`. |
| `quantity` | `int` | spec 009 | |
| `unitPriceMinor` | `int` | spec 007 | Minor units (halalas / piasters). |
| `lineSubtotalMinor` | `int` | spec 007 | |
| `mediaThumbUrl` | `String?` | spec 005 | Null if product has no media. |
| `restrictedAndUnverified` | `bool` | derived | `true` iff product is restricted AND `Session.isVerified == false`. Drives the verification CTA per FR-021. |

### `CheckoutViewModel`

| Field | Type | Source | Notes |
|---|---|---|---|
| `sessionId` | `String` | spec 010 — `POST /checkout/sessions` | One per browser tab / app launch / explicit start. |
| `address` | `AddressViewModel?` | spec 010 + spec 004 (saved addresses) | |
| `selectedShippingQuoteId` | `String?` | spec 010 — `GET /checkout/sessions/{id}/quotes` | |
| `selectedPaymentMethodId` | `String?` | spec 010 — `GET /checkout/sessions/{id}/payment-methods` (per-market) | |
| `idempotencyKey` | `String` | client-generated UUID v4 | Reused on retries; rotated when the cart revision changes. |
| `driftDetectedAt` | `DateTime?` | spec 010 — submit response | Triggers the drift screen state. |
| `submittedOutcome` | `CheckoutOutcome?` | spec 010 — submit response | Order, payment, fulfillment, refund states (Principle 17). |

### `OrderViewModel`

| Field | Type | Source | Notes |
|---|---|---|---|
| `id` | `String` | spec 011 | |
| `orderNumber` | `String` | spec 011 | Localized presentation handled by `OrderNumberFormatter`. |
| `orderState` | `String` | spec 011 | Independent signal (FR-025). |
| `paymentState` | `String` | spec 011 | Independent signal. |
| `fulfillmentState` | `String` | spec 011 | Independent signal. |
| `refundState` | `String` | spec 011 + spec 013 | Independent signal. |
| `placedAt` | `DateTime` | spec 011 | |
| `lines` | `List<OrderLineViewModel>` | spec 011 | |
| `totals` | `PriceBreakdown` | spec 011 (snapshot) | Snapshot at order creation; not re-priced. |
| `tracking` | `TrackingInfo?` | spec 011 | Carrier reference + URL when fulfillment.handed_to_carrier+. |
| `timeline` | `List<TimelineEvent>` | spec 011 | One entry per state transition across all four streams. |
| `refundEligibility` | `RefundEligibility` | spec 013 | Whether the customer can submit a return-request from the detail screen. |

### `AddressViewModel`

| Field | Type | Source | Notes |
|---|---|---|---|
| `id` | `String` | spec 004 | |
| `label` | `String` | spec 004 | "Home", "Office" — user-supplied. |
| `recipient` | `String` | spec 004 | |
| `line1` / `line2` / `city` / `region` / `country` / `postalCode` | `String` | spec 004 | |
| `marketCode` | `String` | spec 004 | Must match the customer's active market (FR-011). |
| `phone` | `String` | spec 004 | E.164 format. |
| `isDefault` | `bool` | spec 004 | At most one default per customer. |

### `LocaleAndMarket`

| Field | Type | Source | Notes |
|---|---|---|---|
| `language` | `enum {ar, en}` | client (LocaleBloc) | |
| `marketCode` | `enum {ksa, eg}` | client (MarketResolver) | |
| `currency` | `enum {SAR, EGP}` | derived from `marketCode` | |
| `direction` | `enum {ltr, rtl}` | derived from `language` | |

---

## Client-side state machines

Each Bloc backs one of these. Every state, every transition trigger, every failure path is enumerated below — Constitution Principle 24 is satisfied client-side too.

### SM-1: `AuthSession`

States: `Guest`, `Authenticating`, `Authenticated`, `Refreshing`, `RefreshFailed` (terminal until user re-authenticates), `LoggingOut`.

| From | To | Trigger | Actor | Failure handling |
|---|---|---|---|---|
| `Guest` | `Authenticating` | `LoginRequested(email, password)` / `RegisterRequested(...)` | User | n/a |
| `Authenticating` | `Authenticated` | spec 004 returns access + refresh | server | — |
| `Authenticating` | `Guest` | spec 004 401 / 422 | server | Bloc emits `AuthFailureEffect` carrying the reason code; UI renders error state in active locale. |
| `Authenticated` | `Refreshing` | access token expired AND refresh present | client interceptor | n/a |
| `Refreshing` | `Authenticated` | spec 004 refresh returns new pair | server | — |
| `Refreshing` | `RefreshFailed` | spec 004 refresh 401 | server | UI prompts re-auth; original request fails. |
| `RefreshFailed` | `Authenticating` | `LoginRequested(...)` | User | — |
| `Authenticated` | `LoggingOut` | `LogoutRequested` (more menu) | User | — |
| `LoggingOut` | `Guest` | secure storage cleared + spec 004 `revoke` returns | client + server | If `revoke` fails, secure storage is still cleared; warn via toast. |

### SM-2: `CartSync`

States: `Empty`, `Loading`, `Loaded`, `OutOfSync` (server has newer revision), `Mutating`, `Error`.

| From | To | Trigger | Actor | Failure handling |
|---|---|---|---|---|
| `Empty` / `Loaded` | `Loading` | `CartRefreshed` (manual or post-login) | User / system | — |
| `Loading` | `Loaded` | spec 009 cart returns | server | — |
| `Loading` | `Error` | spec 009 5xx / network | server | Renders cart error state; user can retry. |
| `Loaded` | `Mutating` | `LineQuantityChanged` / `LineRemoved` / `CouponApplied` | User | — |
| `Mutating` | `Loaded` | spec 009 mutation succeeds with revision++ | server | — |
| `Mutating` | `OutOfSync` | spec 009 returns `cart.revision_mismatch` | server | UI shows "cart updated elsewhere" banner; auto-reload pulls latest then returns to `Loaded`. |
| `Loaded` | `OutOfSync` | a non-mutating fetch returns a higher revision than the local view-model | server | Same banner UX. |

### SM-3: `CheckoutFlow`

States: `Idle` (no session yet), `Drafting` (session open, fields not all set), `Ready` (all fields set, submit enabled), `Submitting`, `Submitted` (success), `DriftBlocked` (server reported drift between Ready and Submit), `Failed` (recoverable), `FailedTerminal` (e.g., expired session).

| From | To | Trigger | Actor | Failure handling |
|---|---|---|---|---|
| `Idle` | `Drafting` | spec 010 `POST /checkout/sessions` | client | — |
| `Drafting` | `Ready` | all of {address, shipping quote, payment method} set | User | — |
| `Ready` | `Submitting` | `SubmitTapped` | User | Idempotency key generated. |
| `Submitting` | `Submitted` | spec 010 submit returns the four-state outcome | server | Outcome view-model carries order, payment, fulfillment, refund states. |
| `Submitting` | `DriftBlocked` | spec 010 submit returns drift code | server | UI shows the drift screen with diff; user accepts new prices and re-enters `Ready`. |
| `Submitting` | `Failed` | spec 010 submit 5xx / payment provider error | server | UI surfaces the recovery state per FR-005; same idempotency key reused on retry. |
| `Failed` | `Submitting` | `RetryTapped` | User | Same idempotency key. |
| any | `FailedTerminal` | spec 010 returns `checkout.session_expired` | server | UI starts a fresh session; user re-enters checkout. |

### SM-4: `OrderListFilter`

States: `Idle`, `Loading`, `Loaded`, `Empty`, `Error`.

Triggers: `FilterChanged(market | dateRange | state)`, `RefreshTapped`, `PageRequested`.

Failure handling: standard per-screen error state; pagination is cursor-based per spec 011.

### SM-5: `LocaleAndDirection`

States: `EN_LTR`, `AR_RTL`.

Triggers: `LanguageToggled` (from more menu) — on first launch, the initial state is set from device locale per R12. The transition is instantaneous (no async work) and is mirrored on the next API request via the `Accept-Language` header.

---

## Validation rules (client-side echo of server contracts)

Client-side validation is **best-effort only** — every field is re-validated by the owning Phase 1B service. The client validates eagerly to keep the UX snappy and to satisfy Q5's resend timer + retry semantics.

| Field | Rule | Owner spec |
|---|---|---|
| Email | Standard RFC 5322 lite (`x@y.tld`) | spec 004 |
| Phone | E.164 via `libphonenumber` (Dart port) | spec 004 |
| OTP code | 6 digits exactly | spec 004 |
| Password | ≥ 12 chars, ≥ 1 letter, ≥ 1 digit, not in HIBP top-100k (server-only) | spec 004 |
| Address postal code | per-market regex (KSA: 5 digits, EG: 5 digits) | spec 004 |
| Quantity | ≥ 1, ≤ per-product `max_qty_per_order` (spec 005) | spec 005 / 009 |

---

## Forward-compat reservations (Principle 6 multi-vendor)

The view-models above intentionally **do not** include vendor-specific fields. When 1B contracts gain a `vendor_id` (Phase 2), the upgrade path is:

1. Regenerate OpenAPI clients.
2. Add `vendorId` to `CartLineViewModel` and `OrderLineViewModel`.
3. Add a vendor-grouping option to the order list filter (`OrderListFilter`).
4. No state-machine changes.
