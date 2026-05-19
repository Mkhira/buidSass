# Contracts — Phase 8: B2B

## Sources

- [`services/backend_api/openapi.b2b.json`](../../../../services/backend_api/openapi.b2b.json) — 20 customer-tagged ops.
- [`services/backend_api/openapi.orders.json`](../../../../services/backend_api/openapi.orders.json) — 4 legacy customer quotation ops.

## Backend specs

- [`specs/phase-1D/021-quotes-and-b2b/`](../../../../specs/phase-1D/021-quotes-and-b2b/)
- [`specs/phase-1B/011-orders/`](../../../../specs/phase-1B/011-orders/) — legacy quotations co-located.

## Endpoints

```text
# Quotes (b2b)
GET    /api/customer/quotes
GET    /api/customer/quotes/awaiting-my-approval
POST   /api/customer/quotes/from-cart                 # Idempotency-Key REQUIRED
POST   /api/customer/quotes/from-product              # Idempotency-Key REQUIRED
GET    /api/customer/quotes/{id}
POST   /api/customer/quotes/{id}/submit-acceptance
POST   /api/customer/quotes/{id}/finalize-acceptance
POST   /api/customer/quotes/{id}/reject-acceptance
POST   /api/customer/quotes/{id}/request-revision
POST   /api/customer/quotes/{id}/withdraw
POST   /api/customer/quotes/{id}/save-as-template
GET    /api/customer/quotes/{quoteId}/versions/{versionId}/documents/{locale}

# Companies (b2b)
POST   /api/customer/companies                        # Idempotency-Key REQUIRED
GET    /api/customer/companies/{id}
PATCH  /api/customer/companies/{id}
POST   /api/customer/companies/{id}/branches
DELETE /api/customer/companies/{id}/branches/{branchId}
POST   /api/customer/companies/{id}/invitations
POST   /api/customer/companies/invitations/{token}/accept
POST   /api/customer/companies/invitations/{token}/decline
PATCH  /api/customer/companies/{id}/memberships/{membershipId}
DELETE /api/customer/companies/{id}/memberships/{membershipId}

# Legacy quotations (orders module)
GET    /v1/customer/quotations
GET    /v1/customer/quotations/{id}
POST   /v1/customer/quotations/{id}/accept
POST   /v1/customer/quotations/{id}/reject
```

## Not consumed

- All admin b2b endpoints (`/api/admin/quotes/*`, `/api/admin/companies/*`).
- All admin legacy quotation endpoints.
