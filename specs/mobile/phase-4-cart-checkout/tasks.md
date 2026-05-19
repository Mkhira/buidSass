# Tasks — Phase 4: Cart & Checkout

## Block A — Cart

### T-4.1 · CartStore + models
- **Files:** `features/cart/data/cart_store.dart` + `models/`.
- **Steps:** in-memory state + atomic shared_preferences persistence; clear-on-sign-out hook subscribes to SessionStore.
- **DoD:** unit tests including atomic-write under simulated crash.

### T-4.2 · CartBloc + screen (S-4.1) — verify
- **Files:** `features/cart/bloc/cart_bloc.dart`, `features/cart/screens/cart_screen.dart`, widgets.
- **Steps:** debounced price-cart on qty change; availability batch; drift-line UX; coupon entry with optimistic UI.
- **DoD:** S-4.1 acceptance criteria green.

## Block B — Checkout core

### T-4.3 · CheckoutGateway
- **Files:** `features/checkout/data/{checkout_gateway,checkout_gateway_impl}.dart`.
- **DoD:** unit tests for each of the 8 endpoints.

### T-4.4 · CheckoutBaseBloc + drift handler
- **Files:** `features/checkout/bloc/checkout_base_bloc.dart`.
- **DoD:** unit tests for 409 detection + accept/review branching.

## Block C — Steps

### T-4.5 · CheckoutStartBloc + screen (S-4.3)
- **DoD:** S-4.3 criteria green.

### T-4.6 · CheckoutSummaryBloc + screen (S-4.4)
- **DoD:** S-4.4 criteria green.

### T-4.7 · Address step (S-4.5)
- **DoD:** S-4.5 criteria green; address picker reads saved addresses from `me`.

### T-4.8 · Shipping step (S-4.6)
- **DoD:** S-4.6 criteria green; empty-quotes branch.

### T-4.9 · Payment step (S-4.7) — base Bloc
- **DoD:** S-4.7 criteria green at the orchestration level.

### T-4.10 · Card / Apple Pay / Mada / STC Pay adapters
- **Files:** `features/checkout/payment_adapters/{card,apple_pay,mada,stc_pay}_adapter.dart`.
- **DoD:** each adapter returns a provider token; no raw PAN ever leaves the hosted fields.

### T-4.11 · Tabby / Tamara / Valu adapters (WebView SDK)
- **DoD:** WebView return resumes the session; cancellation routes back to S-4.7.

### T-4.12 · Meeza / Bank transfer / COD adapters
- **DoD:** offline methods don't open WebView.

### T-4.13 · Review + submit (S-4.8)
- **Steps:** generate Idempotency-Key once; submit; handle 3DS redirect.
- **DoD:** S-4.8 criteria green; same key on retry verified by test.

### T-4.14 · Drift dialog (S-4.9)
- **Files:** `core/widgets/conflict_dialog.dart` (already from Phase 1) + integration via `CheckoutBaseBloc`.
- **DoD:** S-4.9 criteria green.

### T-4.15 · Order confirmation (S-4.10) — verify existing
- **Files:** `features/checkout/screens/order_confirmation_screen.dart`.
- **DoD:** S-4.10 criteria green; cart cleared on entry; bank transfer reference copyable.

## Block D — Cross-cutting

### T-4.16 · 3DS / WebView return handler
- **Files:** `features/checkout/widgets/redirect_webview.dart`, deep-link route `/checkout/return`.
- **DoD:** redirect round-trip verified on iOS + Android in dev.

### T-4.17 · Resume mid-flow on cold start
- **Steps:** if `SessionStore.checkoutSessionId` is set, route to `/checkout/{sessionId}/summary` from splash; cleared on confirmation.
- **DoD:** integration test.

### T-4.18 · PCI scope guard (CI grep)
- **Files:** `scripts/ci/check-mobile-pci.sh`.
- **DoD:** CI greps for forbidden tokens (cardNumber/cvv/pan) outside `payment_adapters/`.

## Block E — Phase exit

### T-4.19 · Analyze + tests
- **DoD:** zero warnings; all tests green.

### T-4.20 · Update overview doc
- **DoD:** Phase 4 row → **Done**.

## Screen ↔ task map

| Screen | Tasks |
|---|---|
| S-4.1 Cart | T-4.1, T-4.2 |
| S-4.3 Checkout start | T-4.5 |
| S-4.4 Summary | T-4.6 |
| S-4.5 Address | T-4.7 |
| S-4.6 Shipping | T-4.8 |
| S-4.7 Payment | T-4.9 – T-4.12 |
| S-4.8 Review/submit | T-4.13 |
| S-4.9 Drift | T-4.14 |
| S-4.10 Confirmation | T-4.15 |
| Cross-cutting | T-4.16 – T-4.18 |
| Exit | T-4.19, T-4.20 |
