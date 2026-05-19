# Contracts — Phase 3: Search

## Source

- [`services/backend_api/openapi.search.json`](../../../../services/backend_api/openapi.search.json) — 3 customer-tagged ops.

## Backend spec

- [`specs/phase-1B/006-search/`](../../../../specs/phase-1B/006-search/)

## Endpoints

```
POST /v1/customer/search/autocomplete
POST /v1/customer/search/products
POST /v1/customer/search/lookup
```

## Not consumed

- `/v1/admin/search/*` (admin reindex/health/jobs).
