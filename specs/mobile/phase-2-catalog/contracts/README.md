# Contracts — Phase 2: Catalog

## Sources

- [`services/backend_api/openapi.catalog.json`](../../../../services/backend_api/openapi.catalog.json) — 4 customer-tagged ops consumed.
- [`services/backend_api/openapi.pricing.json`](../../../../services/backend_api/openapi.pricing.json) — `POST /customer/pricing/price-cart` (preview).
- [`services/backend_api/openapi.inventory.json`](../../../../services/backend_api/openapi.inventory.json) — `GET /v1/customer/inventory/availability`.
- [`services/backend_api/openapi.reviews.json`](../../../../services/backend_api/openapi.reviews.json) — `GET /v1/public/reviews/aggregates` (batch + single).

## Backend specs

- [`specs/phase-1B/005-catalog/`](../../../../specs/phase-1B/005-catalog/)
- [`specs/phase-1B/007-a-pricing-and-tax-engine/`](../../../../specs/phase-1B/007-a-pricing-and-tax-engine/)
- [`specs/phase-1B/008-inventory/`](../../../../specs/phase-1B/008-inventory/)
- [`specs/phase-1D/022-reviews-moderation/`](../../../../specs/phase-1D/022-reviews-moderation/)

## Mobile-callable endpoints (8)

```
GET   /v1/customer/catalog/categories
GET   /v1/customer/catalog/brands
GET   /v1/customer/catalog/categories/{slug}/products
GET   /v1/customer/catalog/products/{slug}
POST  /customer/pricing/price-cart
GET   /v1/customer/inventory/availability
GET   /v1/public/reviews/aggregates
GET   /v1/public/reviews/aggregates/{product_id}
```

## Explicitly NOT consumed by Phase 2

- All admin catalog/pricing/inventory/review endpoints.
- `POST /v1/internal/catalog/restrictions/check` (internal).
- `POST /internal/pricing/calculate` (internal).
- All inventory `/v1/internal/inventory/*` endpoints (used by checkout backend, not mobile).
- All `/v1/customer/reviews/*` endpoints (consumed by Phase 7).
