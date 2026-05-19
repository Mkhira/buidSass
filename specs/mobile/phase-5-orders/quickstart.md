# Quickstart — Phase 5: Orders

## Prerequisites

- Phase 4 complete (an order can be created end-to-end).
- Backend seeded with at least one completed order + one pending-payment order + one shipped order for the test account.

## Run

```sh
cd apps/customer_flutter
flutter pub get
flutter run
```

## Manual smoke (Phase 5 exit gate)

1. **Orders list.** Open Orders tab → list renders with the four state pills per row.
2. **Filter.** Tap "Pending" chip → list filters; tap "All" → list resets.
3. **Pagination + refresh.** Scroll → next page appends; pull-to-refresh resets to page 1.
4. **Order detail.** Tap any row → detail shows four state pills, timeline, items, payment, address, action toolbar.
5. **Cancel.** On a `canCancel=true` order → tap Cancel → pick reason → submit → states pill updates to `cancelled`; refund pill updates if payment was captured.
6. **Cancel 409.** Pause and try to cancel a recently shipped order in dev → 409 → banner + Refresh CTA refreshes the detail.
7. **Reorder.** Tap Reorder → preview shows available + unavailable lines → Confirm → routes to Cart with merged lines.
8. **Tracking.** Open a `shipped` order → timeline shows events; tap carrier URL → opens external browser.
9. **Retry payment.** Open a `failed` payment order → tap Retry Payment → routes to checkout flow.
10. **Return CTA gating.** Open an order where return-eligibility returns `anyEligible=false` → Return button hidden; open one where `anyEligible=true` → Return button visible and routes to Phase 6 wizard.

## Automated

```sh
flutter analyze
flutter test test/features/orders/
```

## Troubleshooting

- **Single combined pill instead of four:** widget regression; verify `StatePills` always emits 4 children.
- **Reorder mutates cart silently:** missing confirm step; review `ReorderBloc.addToCartConfirmed`.
