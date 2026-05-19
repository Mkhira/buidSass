# DoD Checklist — Phase 5: Orders

## Data

- [ ] `OrdersGateway` covers 5 endpoints with typed responses.

## Widgets

- [ ] `StatePills` widget always renders 4 pills (order / payment / fulfillment / refund).
- [ ] `TrackingTimeline` renders events; opens carrier URL externally.

## Screens

- [ ] S-5.1 List: filter chips driven by server enum; pagination; pull-to-refresh.
- [ ] S-5.2 Detail: four state pills; CTA gates server-driven; parallel return-eligibility.
- [ ] S-5.3 Cancel: reason + note; 409 path refreshes detail.
- [ ] S-5.4 Reorder: preview-then-confirm; cart merge.
- [ ] S-5.5 Tracking: timeline + external carrier URL.

## Cross-cutting

- [ ] Retry-payment routes through Phase 4 flow with same intent.
- [ ] No client-side state-merging logic; states always pulled from server.

## Phase exit

- [ ] `flutter analyze` clean.
- [ ] `flutter test` green.
- [ ] Smoke test recorded.
- [ ] §8 row → **Done**.
