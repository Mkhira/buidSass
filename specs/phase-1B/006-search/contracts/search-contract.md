# HTTP Contract — Search v1 (Spec 006)

**Base**: `/v1/`. Errors use RFC 7807 + `reasonCode` (e.g. `search.engine_unavailable`).

---

## Customer (read)

### POST /v1/customer/search/products
Request:
```json
{
  "query": "قفازات",
  "marketCode": "ksa",
  "locale": "ar",
  "filters": {
    "brandIds": ["…"],
    "categoryIds": ["…"],
    "priceMinMinor": 0,
    "priceMaxMinor": 99900,
    "restricted": "any",
    "availability": "any"
  },
  "sort": "relevance",
  "page": 1,
  "pageSize": 24
}
```
Response:
```json
{
  "hits": [ /* ProductSearchProjection subset */ ],
  "facets": {
    "brandId": { "…": 42 },
    "categoryId": { "…": 18 },
    "priceBucket": { "0-99": 3, "100-499": 17, "500-1999": 42, "2000+": 5 },
    "restricted": { "true": 4, "false": 58 },
    "availability": { "in_stock": 50, "backorder": 8, "out_of_stock": 4 }
  },
  "totalEstimate": 62,
  "queryDurationMs": 18,
  "engineLatencyMs": 12,
  "localeFallbackApplied": false
}
```

- `503 search.engine_unavailable` (`Retry-After: 5`) when Meilisearch is down.
- `400 search.invalid_sort` on unknown sort key.

### POST /v1/customer/search/autocomplete
Request: `{ query, marketCode, locale, limit? (default 5) }`.
Response: `{ "suggestions": [{ "productId", "name", "thumbUrl", "restricted" }], "noResultsReason": "no_matches"|"restricted_market"|null }`.

### POST /v1/customer/search/lookup
Exact SKU/barcode shortcut. Request: `{ code, marketCode, locale }`.
Response: `{ "hit": ProductSearchProjection | null }`.

---

## Admin (write)

All require admin JWT (spec 004) with `search.reindex` or `search.read` permissions.

### POST /v1/admin/search/reindex?index=products-ksa-ar
Permission: `search.reindex`.
Response: `202 Accepted` + `{ "jobId": "uuid" }`, then SSE on `/v1/admin/search/reindex/{jobId}/stream` emitting:
```
event: progress
data: { "docsWritten": 1000, "docsExpected": 20000, "elapsedMs": 12000 }

event: completed
data: { "docsWritten": 20000, "elapsedMs": 280000 }
```
- `409 search.reindex.in_progress` with body `{ "activeJobId": "uuid" }` on conflict.

### GET /v1/admin/search/health
Permission: `search.read`.
Response:
```json
{
  "indexes": [
    {
      "name": "products-ksa-ar",
      "docCount": 19872,
      "lastSuccessAt": "2026-04-22T09:41:12Z",
      "lagSeconds": 3,
      "status": "healthy"
    }
  ],
  "engineStatus": "available",
  "enginePingMs": 4
}
```

### GET /v1/admin/search/jobs?index=&status=&page=&pageSize=
Lists reindex jobs. Permission: `search.read`.

---

## Reason codes
`search.engine_unavailable`, `search.reindex.in_progress`, `search.invalid_sort`, `search.invalid_locale`, `search.invalid_market`, `search.market_locale_index_missing`.
