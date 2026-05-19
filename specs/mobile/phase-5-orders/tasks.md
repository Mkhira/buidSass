# Tasks — Phase 5: Orders

## T-5.1 · OrdersGateway
- **Files:** `features/orders/data/{orders_gateway,orders_gateway_impl}.dart`, `models/`.
- **DoD:** unit tests for 5 endpoints.

## T-5.2 · OrdersListBloc + screen (S-5.1) — verify existing
- **DoD:** S-5.1 criteria green; filter chips driven by server enum.

## T-5.3 · State-pills widget + TrackingTimeline widget
- **Files:** `features/orders/widgets/state_pills.dart`, `tracking_timeline.dart`.
- **DoD:** golden tests in AR + EN; pill widget renders 4 pills regardless of input.

## T-5.4 · OrderDetailBloc + screen (S-5.2) — verify existing
- **Steps:** four-state pills; parallel return-eligibility; CTA gating via `actions.*`.
- **DoD:** S-5.2 criteria green.

## T-5.5 · CancelOrderBloc + screen (S-5.3)
- **DoD:** S-5.3 criteria green.

## T-5.6 · ReorderBloc + screen (S-5.4) with cart merge
- **DoD:** S-5.4 criteria green; integration test for merge.

## T-5.7 · Retry-payment hand-off
- **Goal:** wire `OrderDetailBloc.retryPayment()` to checkout flow.
- **DoD:** integration test from a failed-payment order.

## T-5.8 · Analyze + tests
- **DoD:** zero warnings; tests green.

## T-5.9 · Update overview doc
- **DoD:** Phase 5 → **Done**.

## Screen ↔ task map

| Screen | Tasks |
|---|---|
| S-5.1 List | T-5.2 |
| S-5.2 Detail | T-5.3, T-5.4 |
| S-5.3 Cancel | T-5.5 |
| S-5.4 Reorder | T-5.6 |
| S-5.5 Tracking (component) | T-5.3 |
| Retry-payment wiring | T-5.7 |
| Exit | T-5.8, T-5.9 |
