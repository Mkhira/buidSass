# Contracts — Phase 5: Orders

## Source

- [`services/backend_api/openapi.orders.json`](../../../../services/backend_api/openapi.orders.json) — 5 customer-tagged ops consumed here (legacy quotations move to Phase 8).

## Backend spec

- [`specs/phase-1B/011-orders/`](../../../../specs/phase-1B/011-orders/)

## Endpoints

```text
GET    /v1/customer/orders
GET    /v1/customer/orders/{id}
POST   /v1/customer/orders/{id}/cancel
POST   /v1/customer/orders/{id}/reorder
GET    /v1/customer/orders/{id}/return-eligibility
```

## Phase boundary

- `/v1/customer/quotations*` (4 endpoints) belong to Phase 8 (B2B legacy quotations).
- All admin and internal endpoints out of scope.
