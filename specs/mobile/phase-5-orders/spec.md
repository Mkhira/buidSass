# Spec — Phase 5: Customer Mobile Orders

> **Phase:** 5 of 8 · **Owner:** mobile + orders · **Last updated:** 2026-05-19
> **OpenAPI source:** [`openapi.orders.json`](../../../services/backend_api/openapi.orders.json)
> **Endpoint count:** 5 customer-tagged (legacy quotations move to Phase 8).
> **Depends on:** Phase 4 (confirmation hands off here).

---

## 1. Goal

Deliver the post-purchase orders surface: list, detail with separated state machines (order / payment / fulfillment / refund), reorder, cancel, tracking timeline, and a payment retry entry point.

## 2. User roles

| Role | Phase 5 scope |
|---|---|
| Authenticated customer | Full list + detail + cancel + reorder + retry. |
| B2B buyer / approver | Same surface; orders carry company context where applicable. |

## 3. Business rules

| BR | Rule | Reference |
|---|---|---|
| BR-1 | Order detail renders FOUR separate state pills: `orderState`, `paymentState`, `fulfillmentState`, `refundState`. Never merged. | Principle 24 |
| BR-2 | Cancel-eligibility is server-driven; UI never decides by inspecting state alone. The Cancel CTA is gated by a field in the order detail response. | Principle 24 |
| BR-3 | Reorder rehydrates the cart with the items that are still available; lines that became unavailable surface a banner with a per-line note. | Principle 11 |
| BR-4 | Return-eligibility query (`/return-eligibility`) drives entry to the Phase 6 return wizard. | Principle 17 |
| BR-5 | Payment retry routes back to the Phase 4 Payment step using the original checkout session if still alive; otherwise routes to a fresh session with same items + same idempotency intent. | Principle 13 |
| BR-6 | Tracking timeline reads from `order.fulfillment.events[]`; no shipping-provider direct calls from mobile (server aggregates). | Principle 14 |

## 4. Screens

### S-5.1 Orders list

**Status:** **Done** — `features/orders/screens/orders_list_screen.dart`; verify.
**Route:** `/orders` · **Bottom nav:** visible (Orders tab)
**Wireframe:** [`#phase-5-orders-list`](../../../docs/mobile-screens-wireframes.md#phase-5-orders-list--s-51-orders-list)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/orders | mount + filter change + pagination + pull-to-refresh | safe | filters: status, market, from, to, page, pageSize |

#### Response data shape
```json
{
  "items": [
    {
      "id": "uuid",
      "orderNumber": "2026-05-000123",
      "placedAt": "iso8601",
      "totals": { "grandTotal": "253.00", "currency": "SAR" },
      "orderState": "confirmed | placed | cancelled | completed",
      "paymentState": "captured | pending | failed | refunded",
      "fulfillmentState": "pending | picking | packed | shipped | delivered",
      "refundState": "none | requested | approved | issued",
      "itemPreview": [{ "imageUrl": "...", "name": "..." }]
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 12
}
```

#### UI states
loading skeleton → list with filter chips ([All][Pending][Delivered] etc.) → empty ("No orders yet" + Continue shopping) → error/offline.

#### Bloc scaffold
- `OrdersListBloc`.
- Events: started, filterChanged(status), pageRequested, refreshed.
- States: standard.

#### Acceptance criteria
- [ ] Filter chips drive the `status` query (use server's enum).
- [ ] Pagination append; pull-to-refresh resets.
- [ ] Each row shows ALL four state pills compactly.
- [ ] AR + EN.
- [ ] Tests.

#### Edge cases
- Order canceled while list is open ⇒ pull-to-refresh resolves; otherwise the stale row remains until next fetch.

---

### S-5.2 Order detail

**Status:** **Done** — `features/orders/screens/order_detail_screen.dart`; verify.
**Route:** `/orders/{id}` · **Bottom nav:** visible
**Wireframe:** [`#phase-5-order-detail`](../../../docs/mobile-screens-wireframes.md#phase-5-order-detail--s-52-order-detail)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/orders/{id} | mount + pull-to-refresh | safe | |
| GET | /v1/customer/orders/{id}/return-eligibility | when Return CTA is about to render | safe | gates the Return button |

#### Response data shape
```json
{
  "id": "uuid",
  "orderNumber": "...",
  "placedAt": "iso8601",
  "states": {
    "orderState": "...",
    "paymentState": "...",
    "fulfillmentState": "...",
    "refundState": "..."
  },
  "actions": {
    "canCancel": true,
    "canReorder": true,
    "canRetryPayment": false,
    "canReturn": true                  // confirmed against return-eligibility endpoint, but server may also surface here
  },
  "lines": [{ "productId": "uuid", "name": "...", "qty": 2, "unitPrice": "...", "lineTotal": "...", "imageUrl": "..." }],
  "address": { /* AddressDto */ },
  "shipment": {
    "method": "string",
    "tracking": { "carrier": "Aramex", "trackingNumber": "AWB123", "url": "https://..." },
    "events": [
      { "kind": "picked | packed | shipped | delivered", "occurredAt": "iso8601", "label": "string" }
    ]
  },
  "payment": {
    "method": "card | ...",
    "providerRef": "string?",
    "bankTransfer": { "reference": "string", "iban": "string" }
  },
  "totals": { /* same as list */ }
}
```

#### UI states
loading → loaded (four state pills, timeline, items, payment, shipment, action toolbar) → error/offline.

#### Bloc scaffold
- `OrderDetailBloc`.
- Events: started(id), refreshed, cancelRequested, reorderRequested, retryPaymentRequested.
- States: loading, loaded(order, returnEligibility), failure.

#### Acceptance criteria
- [ ] Four state pills always rendered separately (BR-1).
- [ ] Cancel CTA gated by `actions.canCancel` (BR-2).
- [ ] Reorder CTA routes to a confirmation screen with the line list (S-5.4).
- [ ] Return CTA routes to Phase 6 Return wizard (S-6.2) only when `return-eligibility` returns at least one eligible line (BR-4).
- [ ] Retry Payment CTA routes per BR-5.
- [ ] Tests.

#### Edge cases
- Order moves to `cancelled` between list and detail open ⇒ detail still loads and shows cancelled state with disabled CTAs.

---

### S-5.3 Cancel order

**Status:** Planned (CTA exists; dedicated cancel screen new)
**Route:** `/orders/{id}/cancel` · **Bottom nav:** hidden
**Wireframe:** [`#phase-5-cancel`](../../../docs/mobile-screens-wireframes.md#phase-5-cancel--s-53-cancel-order)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/orders/{id}/cancel | submit | yes | server validates current state |

#### Response data shape
Returns refreshed order detail.

#### UI states
form (reason dropdown + optional note) → loading → success routes back to order detail with updated states + toast → error-409 (no longer cancellable) shows banner + Refresh CTA.

#### Bloc scaffold
- `CancelOrderBloc`. Events: started, reasonChanged, noteChanged, submitted. States: form, loading, success, failure.

#### Acceptance criteria
- [ ] Reason list comes from a server enum (or hardcoded fallback list documented in data-model).
- [ ] 409 path refreshes the underlying order detail Bloc.
- [ ] AR + EN.
- [ ] Tests.

#### Edge cases
- Order has multiple shipments, only some cancellable ⇒ server returns per-shipment outcome; UI surfaces the partial-cancel state in the result toast.

---

### S-5.4 Reorder

**Status:** Planned
**Route:** `/orders/{id}/reorder` · **Bottom nav:** hidden
**Wireframe:** [`#phase-5-reorder`](../../../docs/mobile-screens-wireframes.md#phase-5-reorder--s-54-reorder)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/orders/{id}/reorder | submit | yes | server returns available + unavailable lines |

#### Response data shape
```json
{
  "available": [{ "productId": "uuid", "qty": 2, "name": "...", "priceHint": "..." }],
  "unavailable": [{ "productId": "uuid", "name": "...", "reason": "out_of_stock | discontinued | market_blocked" }]
}
```

#### UI states
loading → result preview with available + unavailable sections → Add to Cart CTA → routes to `/cart` with cart populated; toast "X items added, Y skipped".

#### Bloc scaffold
- `ReorderBloc`. Events: started, addToCartConfirmed. States: loading, loaded(available, unavailable), confirming, done, failure.

#### Acceptance criteria
- [ ] Preview before mutating cart.
- [ ] Cart merge with existing lines: bump qty if line exists, else append.
- [ ] Unavailable lines listed clearly with reason chip.
- [ ] Tests.

#### Edge cases
- All lines unavailable ⇒ render full "Nothing to reorder" state with Search CTA.

---

### S-5.5 Tracking timeline (sub-view of S-5.2)

**Status:** Planned (component)
**Wireframe:** [`#phase-5-tracking`](../../../docs/mobile-screens-wireframes.md#phase-5-tracking--s-55-tracking-timeline)

Renders `shipment.events[]` as a vertical timeline. Top event live, prior events dimmed. Tap-to-open carrier URL (external browser).

Component lives at `lib/features/orders/widgets/tracking_timeline.dart`. No Bloc.

#### Acceptance criteria
- [ ] AR mirrors timeline anchor (right-aligned in AR, left in EN).
- [ ] Empty events ⇒ "Tracking will appear once your order ships".
- [ ] Tests.

---

## 5. Acceptance criteria — phase-wide

- [ ] 4 screens + 1 component pass per-entry DoD.
- [ ] Order detail always renders four state pills (BR-1).
- [ ] Cancel + Return + Retry CTAs all server-gated, never client-decided.
- [ ] Reorder previews before mutating cart.
- [ ] `flutter analyze` + `flutter test` green.
- [ ] §8 row → **Done**.

## 6. Dependencies

- Phase 4 (Cart + checkout return path for retry payment).
- Phase 6 (Return wizard entry from order detail).

## 7. Out of scope

- Order-edit (change address mid-fulfillment) — not in launch.
- Subscriptions / recurring orders — not in launch.
- Multi-shipment timeline UI — single timeline at launch; partial states surface via the `fulfillmentState` pill.

## 8. References

- Principles 11, 13, 14, 17, 24, 27, 28.
- ADR-002 (Bloc), ADR-008 (shipping).
