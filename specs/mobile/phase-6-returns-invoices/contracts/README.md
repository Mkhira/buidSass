# Contracts — Phase 6: Returns & Invoices

## Sources

- [`services/backend_api/openapi.returns.json`](../../../../services/backend_api/openapi.returns.json) — 4 customer-tagged ops.
- [`services/backend_api/openapi.invoices.json`](../../../../services/backend_api/openapi.invoices.json) — 2 customer-tagged ops.
- [`services/backend_api/openapi.orders.json`](../../../../services/backend_api/openapi.orders.json) — `/return-eligibility` (reused from Phase 5).

## Backend specs

- [`specs/phase-1B/013-returns/`](../../../../specs/phase-1B/013-returns/)
- [`specs/phase-1B/012-tax-invoices/`](../../../../specs/phase-1B/012-tax-invoices/)

## Endpoints

```
GET    /v1/customer/returns
GET    /v1/customer/returns/{id}
POST   /v1/customer/returns/photos
POST   /v1/customer/orders/{orderId}/returns       # Idempotency-Key REQUIRED

GET    /v1/customer/orders/{orderId}/invoice
GET    /v1/customer/orders/{orderId}/invoice.pdf

GET    /v1/customer/orders/{orderId}/return-eligibility   # reused from Phase 5
```

## Not consumed

- All admin returns/refund endpoints.
- All admin invoices endpoints (render queue, export, regenerate).
- All internal credit-note + issue-on-capture endpoints.
