# Implementation Plan — Phase 5: Orders

## Module layout

```text
apps/customer_flutter/lib/features/orders/
├── data/
│   ├── orders_gateway.dart                # 5 endpoints
│   ├── orders_gateway_impl.dart
│   └── models/
├── bloc/
│   ├── orders_list_bloc.dart
│   ├── order_detail_bloc.dart
│   ├── cancel_order_bloc.dart
│   └── reorder_bloc.dart
├── screens/
│   ├── orders_list_screen.dart            # existing — verify
│   ├── order_detail_screen.dart           # existing — verify
│   ├── cancel_order_screen.dart           # new
│   └── reorder_screen.dart                # new
└── widgets/
    ├── state_pills.dart                   # the four state pills row
    ├── tracking_timeline.dart             # S-5.5
    └── order_card.dart                    # list row
```

## Bloc structure

Standard sealed-state shape. `OrderDetailBloc` orchestrates two parallel calls (`order/{id}` + `return-eligibility`), but renders progressively (order data first, eligibility refines the Return CTA when its call returns).

## Routing additions

```text
/orders                                                  (existing)
/orders/{id}                                             (existing)
/orders/{id}/cancel                                      → CancelOrderScreen (new)
/orders/{id}/reorder                                     → ReorderScreen (new)
```

Tracking renders inline within `/orders/{id}` via `TrackingTimeline`.

## Build sequence

1. OrdersGateway (T-5.1).
2. OrdersListBloc + screen verify (T-5.2).
3. OrderDetailBloc + screen verify + four-state-pill widget (T-5.3, T-5.4).
4. CancelOrderBloc + screen (T-5.5).
5. ReorderBloc + screen with cart merge (T-5.6).
6. TrackingTimeline widget (T-5.3 — co-delivered with state-pills widget).
7. Retry-payment hand-off (T-5.7).
8. Tests + exit (T-5.8, T-5.9).

## Risks specific to Phase 5

| # | Risk | Mitigation |
|---|---|---|
| 1 | Client merging the four states into one pill for "simplicity". | Lint review + spec test: pill widget always emits 4 pills regardless of state. |
| 2 | Reorder mutates cart before the user agrees. | Preview-then-confirm pattern; cart write only on explicit CTA. |
| 3 | Retry-payment when original session expired. | `OrderDetailBloc.retryPayment()` checks server's `actions.canRetryPayment` and routes to `/checkout` with a fresh session if needed. |
| 4 | Tracking URL may open in-app WebView vs external browser inconsistently. | Always open external via `url_launcher` with `LaunchMode.externalApplication`. |

## Definition of Done

See `checklists/dod.md`.
