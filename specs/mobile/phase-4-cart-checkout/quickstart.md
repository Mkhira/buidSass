# Quickstart — Phase 4: Cart & Checkout

## Prerequisites

- Phases 1–2 complete.
- Backend checkout module + payments module deployed (specs 010 + 027).
- Provider sandbox credentials in `.env` for at least: HyperPay (KSA card), Paymob (EG card). Other providers may be stubbed during dev.

## Run

```sh
cd apps/customer_flutter
flutter pub get
flutter run
```

## Manual smoke (Phase 4 exit gate)

1. **Cart open empty.** From any tab → Cart → empty state with "Continue shopping".
2. **Add item.** Open a PDP, add to cart → Cart tab badge increments.
3. **Cart pricing.** Open Cart → totals match price engine; qty change debounces 300 ms then totals refresh.
4. **Coupon apply.** Enter valid coupon → discount appears; invalid coupon → inline 422 error.
5. **Restricted item.** Add a restricted product (if not blocked) → drift line warning on cart open; Remove works.
6. **Proceed.** Tap Proceed → checkout session created → Summary screen.
7. **Address step.** Saved address pre-fills; edit + submit succeeds → Shipping step.
8. **Shipping step.** Quotes list → pick one → submit → Payment step.
9. **Payment — Card (KSA HyperPay sandbox).** Enter test card → token returned → PATCH succeeds → Review.
10. **Payment — Apple Pay (iOS only).** Tap Apple Pay button → PassKit sheet → confirm → token returned → Review.
11. **Review + submit.** Idempotency-Key visible in dev logs. Submit succeeds → 3DS challenge (sandbox) → return resumes → Confirmation.
12. **Bank transfer.** Switch to bank transfer → submit → Confirmation shows reference + IBAN; copy both.
13. **COD (EG only).** Switch market to EG and use COD-eligible address → submit → Confirmation with COD note.
14. **Drift retry.** In dev, bump a product price after session creation → on submit, 409 ConflictDialog appears with delta → Accept and Pay → submit retries with same idempotency key → success.
15. **Cold start mid-flow.** Background the app during Shipping step → kill and reopen → land on `/checkout/{sessionId}/summary` and continue.
16. **Cart cleared on confirmation.** From Confirmation, navigate back to Cart → empty state.

## Automated

```sh
flutter analyze
flutter test test/features/cart/
flutter test test/features/checkout/
```

## Troubleshooting

- **Submit hangs:** Check that Idempotency-Key header is being sent (Dio logs). Re-tap must reuse the same key, not regenerate.
- **WebView return doesn't resume:** Verify deep-link scheme registration on both platforms.
- **Cart persists after sign-out:** Subscribe `CartStore` to `SessionStore.stateStream` and clear on `Anonymous`.
