# Contracts — Phase 7: Trust & Compliance

## Sources

- [`services/backend_api/openapi.verification.json`](../../../../services/backend_api/openapi.verification.json) — 8 customer-tagged ops.
- [`services/backend_api/openapi.reviews.json`](../../../../services/backend_api/openapi.reviews.json) — 6 customer-tagged ops.

## Backend specs

- [`specs/phase-1D/020-verification/`](../../../../specs/phase-1D/020-verification/)
- [`specs/phase-1D/022-reviews-moderation/`](../../../../specs/phase-1D/022-reviews-moderation/)

## Endpoints

```text
POST   /api/customer/verifications              # Idempotency-Key REQUIRED
GET    /api/customer/verifications
GET    /api/customer/verifications/active
GET    /api/customer/verifications/schema
GET    /api/customer/verifications/{id}
POST   /api/customer/verifications/{id}/documents
POST   /api/customer/verifications/{id}/resubmit  # Idempotency-Key REQUIRED
POST   /api/customer/verifications/renew          # Idempotency-Key REQUIRED

POST   /v1/customer/reviews                     # Idempotency-Key REQUIRED
GET    /v1/customer/reviews/me
GET    /v1/customer/reviews/me/{id}
GET    /v1/customer/reviews/report-reasons
PATCH  /v1/customer/reviews/{id}
POST   /v1/customer/reviews/{id}/report
```

## Not consumed

- All `/api/admin/verifications/*`.
- All `/v1/admin/reviews/*`.
- `/v1/public/reviews/aggregates*` is consumed by Phase 2 (PDP rating block) and reused here only via the shared aggregates gateway.
