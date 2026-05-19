# Contracts — Phase 4: Cart & Checkout

## Sources

- [`services/backend_api/openapi.checkout.json`](../../../../services/backend_api/openapi.checkout.json) — 8 customer-tagged ops.
- [`services/backend_api/openapi.pricing.json`](../../../../services/backend_api/openapi.pricing.json) — `POST /customer/pricing/price-cart` (reused from Phase 2).
- [`services/backend_api/openapi.inventory.json`](../../../../services/backend_api/openapi.inventory.json) — `GET /v1/customer/inventory/availability` (reused from Phase 2).

## Backend specs

- [`specs/phase-1B/010-checkout/`](../../../../specs/phase-1B/010-checkout/)
- [`specs/phase-1B/007-a-pricing-and-tax-engine/`](../../../../specs/phase-1B/007-a-pricing-and-tax-engine/)
- [`specs/phase-1B/008-inventory/`](../../../../specs/phase-1B/008-inventory/)
- [`specs/phase-1E/027-payments-integration/`](../../../../specs/phase-1E/027-payments-integration/) — payment provider integration (ADR-007 stack).

## Endpoints

```
POST   /v1/customer/checkout/sessions
GET    /v1/customer/checkout/sessions/{sessionId}/summary
GET    /v1/customer/checkout/sessions/{sessionId}/shipping-quotes
PATCH  /v1/customer/checkout/sessions/{sessionId}/address
PATCH  /v1/customer/checkout/sessions/{sessionId}/shipping
PATCH  /v1/customer/checkout/sessions/{sessionId}/payment-method
POST   /v1/customer/checkout/sessions/{sessionId}/submit            # Idempotency-Key REQUIRED
POST   /v1/customer/checkout/sessions/{sessionId}/accept-drift

POST   /customer/pricing/price-cart                                 # preview (Phase 2 contract)
GET    /v1/customer/inventory/availability                          # availability (Phase 2 contract)
```

## Not consumed

- `/v1/admin/checkout/*` (admin checkout console).
- `/v1/webhooks/payment-gateway/{providerId}` (provider → backend).
- All `/v1/internal/inventory/*` (reservations are server-internal).

## PCI scope

Per ADR-007 SAQ-A: app **never** stores PAN, CVV, or track data. The Phase 4 payment adapters in `apps/customer_flutter/lib/features/checkout/payment_adapters/` collect provider tokens only.
