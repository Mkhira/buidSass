# Implementation Plan — Phase 4: Cart & Checkout

## Module layout

```text
apps/customer_flutter/lib/features/
├── cart/
│   ├── data/
│   │   ├── cart_store.dart                # in-memory + shared_preferences
│   │   └── models/
│   ├── bloc/cart_bloc.dart
│   ├── screens/cart_screen.dart           # existing — verify
│   └── widgets/{cart_line.dart,coupon_input.dart,totals_panel.dart}
└── checkout/
    ├── data/
    │   ├── checkout_gateway.dart          # 8 endpoints
    │   ├── checkout_gateway_impl.dart
    │   └── models/
    ├── bloc/
    │   ├── checkout_start_bloc.dart
    │   ├── checkout_summary_bloc.dart
    │   ├── checkout_address_bloc.dart
    │   ├── checkout_shipping_bloc.dart
    │   ├── checkout_payment_bloc.dart
    │   ├── checkout_review_bloc.dart
    │   └── checkout_base_bloc.dart        # drift handling, idempotency helpers
    ├── screens/
    │   ├── checkout_screen.dart            # existing wrapper — verify; routes to step screens
    │   ├── checkout_summary_screen.dart
    │   ├── address_step_screen.dart
    │   ├── shipping_step_screen.dart
    │   ├── payment_step_screen.dart
    │   ├── review_screen.dart
    │   ├── drift_screen.dart               # existing — verify
    │   └── order_confirmation_screen.dart  # existing — verify
    └── widgets/{step_indicator.dart,payment_method_card.dart,redirect_webview.dart}
```

## Idempotency-Key handling

`CheckoutReviewBloc` generates one key on entry to the Review screen:

```dart
final idempotencyKey = const Uuid().v4();
```

The key is held in Bloc state, **never regenerated** until the user explicitly navigates away from the review screen (back-stack pop). All submit retries (network error, 5xx, user re-tap) reuse the same key.

The `IdempotencyInterceptor` (Phase 1) picks the key from `RequestOptions.extra['idempotencyKey']` when set by the gateway call.

## Drift handling

`CheckoutBaseBloc` (mixin or abstract class) provides `handleConflict(DioException e)` that:
1. Parses the 409 body for delta.
2. Emits `ConflictDriftDetected(delta)`.
3. Awaits user resolution via `acceptDrift()` (call S-4.9 endpoint) or `reviewDrift()` (refresh summary, route back to Summary).
4. On accept: re-runs the original request once.

## Payment provider integration

Each payment method has an adapter in `lib/features/checkout/payment_adapters/`:

```text
card_adapter.dart           # hosted-fields (provider SDK)
apple_pay_adapter.dart      # PassKit
mada_adapter.dart           # provider SDK
stc_pay_adapter.dart        # provider SDK
tabby_adapter.dart          # WebView SDK
tamara_adapter.dart         # WebView SDK
valu_adapter.dart           # WebView SDK
meeza_adapter.dart          # provider SDK
bank_transfer_adapter.dart  # no SDK; collects optional reference
cod_adapter.dart            # no SDK; just confirms intent
```

Each adapter exposes:
```dart
abstract class PaymentAdapter {
  Future<PaymentTokenResult> collectToken({required CheckoutSummary summary, required BuildContext context});
}
```

`CheckoutPaymentBloc` dispatches to the adapter matching `summary.payment.method`. The adapter returns a provider-issued token; the Bloc then PATCHes payment-method with that token. **No PAN or CVV ever lives in app memory** (BR SAQ-A from ADR-007).

## Routing additions

```text
/cart                                                    (existing)
/checkout                                                → CheckoutStartScreen
/checkout/{sessionId}/summary                            → CheckoutSummaryScreen
/checkout/{sessionId}/address                            → AddressStepScreen
/checkout/{sessionId}/shipping                           → ShippingStepScreen
/checkout/{sessionId}/payment                            → PaymentStepScreen
/checkout/{sessionId}/review                             → ReviewScreen
/checkout/{sessionId}/confirmation                       → OrderConfirmationScreen (existing — verify)
```

Session id is preserved in `SessionStore.checkoutSessionId` so a cold-start mid-flow can resume.

## Build sequence

1. CartStore + CartBloc + screen (T-4.1, T-4.2).
2. CheckoutGateway (T-4.3).
3. CheckoutBaseBloc + drift handler (T-4.4).
4. Start + Summary screens (T-4.5, T-4.6).
5. Address step (T-4.7).
6. Shipping step (T-4.8).
7. Payment step + adapters (T-4.9 … T-4.12).
8. Review + submit + idempotency (T-4.13).
9. Drift dialog wiring (T-4.14).
10. Confirmation screen verify (T-4.15).
11. 3DS / WebView return handler (T-4.16).
12. Resume mid-flow on cold start (T-4.17).
13. PCI scope guard (CI grep) (T-4.18).
14. Phase exit — analyze + tests (T-4.19), update overview doc (T-4.20).

## Risks specific to Phase 4

| # | Risk | Mitigation |
|---|---|---|
| 1 | Idempotency-Key reuse outside one user intent. | Generated only in `ReviewBloc`; cleared on back-stack pop. |
| 2 | Drift causes the user to lose progress. | `accept-drift` preserves the step; the user only re-confirms totals. |
| 3 | Provider WebView return drops context. | Use `flutter_web_auth_2` (or platform-specific equivalents) with custom URL scheme; Bloc holds the pre-redirect state in a `redirectCorrelationId`. |
| 4 | 3DS challenge times out (often 5 min). | Server-side session expiry; UI surfaces a "Resume" CTA that refreshes the summary and re-enters Review. |
| 5 | Cart corrupted by an app crash mid-write. | `CartStore` writes are atomic (write to temp + rename). |
| 6 | PCI scope drift: a stray `cardNumber` string in a log. | Lint rule + CI grep for `cardNumber\|cvv\|pan` outside the payment adapter folder. |

## Definition of Done

See `checklists/dod.md`.
