# HTTP Contract — Catalog v1 (Spec 005)

**Base**: `/v1/`. Customer read surface under `/v1/customer/catalog/*`, admin write surface under `/v1/admin/catalog/*`. Errors use RFC 7807 with `reasonCode` extension (e.g. `catalog.restricted.verification_required`).

---

## Customer (read)

### GET /v1/customer/catalog/categories?market=ksa
Returns the category tree for the given market, active only.
Response: `{ "categories": [{ "id", "slug", "nameAr", "nameEn", "children": [...], "depth" }] }`.

### GET /v1/customer/catalog/categories/{slug}/products?market=ksa&page=1&pageSize=24&sort=relevance|price-asc|price-desc|newest&brand=&priceMin=&priceMax=&restricted=any|only-unrestricted
Returns a page of published products for the category. Includes facet counts for brand, price-range buckets, restriction status. Returns localized fields per `Accept-Language`.

### GET /v1/customer/catalog/products/{slug}?market=ksa
Returns the product detail DTO: names, descriptions, media variants, documents, brand, categories breadcrumb, restriction badge, price-hint (may be superseded by spec 007-a at cart time).
- `404` if not `published` or not in market.

### GET /v1/customer/catalog/brands?market=ksa
List active brands with product counts.

### POST /v1/internal/catalog/restrictions/check (internal; called by specs 008/009/010/011)
Auth: service-to-service token (issued by spec 004 platform JWT).
Request: `{ "productId", "marketCode", "verificationState": "unverified"|"verified" }`.
Response: `{ "allowed": bool, "reasonCode": "catalog.restricted.verification_required"|"ok" }`.
p95 ≤ 20 ms (SC-006).

---

## Admin (write)

All require admin JWT (spec 004) with relevant permission claim.

### Categories
- `POST /v1/admin/catalog/categories` — create. Permission: `catalog.category.write`. Body: `{ parentId?, slug, nameAr, nameEn, displayOrder? }`.
- `PATCH /v1/admin/catalog/categories/{id}` — update.
- `POST /v1/admin/catalog/categories/{id}/reparent` — body `{ newParentId }`. Rejects cycle with `catalog.category.cycle_detected`.
- `DELETE /v1/admin/catalog/categories/{id}` — soft-delete; rejects if products still reference it (`catalog.category.in_use`).

### Brands / Manufacturers
- `POST /v1/admin/catalog/brands` — permission `catalog.brand.write`.
- `PATCH /v1/admin/catalog/brands/{id}`.
- Same shape for manufacturers.

### Products
- `POST /v1/admin/catalog/products` — create draft. Permission: `catalog.product.write`. Body contains full payload; omit fields become default.
- `PATCH /v1/admin/catalog/products/{id}` — update draft or in-review (published products require the transition endpoints instead).
- `POST /v1/admin/catalog/products/{id}/submit-for-review` — permission: `catalog.product.submit`.
- `POST /v1/admin/catalog/products/{id}/publish` — permission: `catalog.product.publish`. Body: `{ publishAt? }` (optional; sets scheduled if future).
- `POST /v1/admin/catalog/products/{id}/cancel-schedule` — permission: `catalog.product.publish`.
- `POST /v1/admin/catalog/products/{id}/archive` — permission: `catalog.product.archive`.

### Media & documents
- `POST /v1/admin/catalog/products/{id}/media` — multipart upload. Returns `{ mediaId, variantStatus: "pending" }`.
- `PATCH /v1/admin/catalog/products/{id}/media/{mediaId}` — update alt text, display order, primary flag.
- `DELETE /v1/admin/catalog/products/{id}/media/{mediaId}`.
- `POST /v1/admin/catalog/products/{id}/documents` — multipart; body includes `docType`, `locale`, `titleAr`, `titleEn`.

### Bulk import
- `POST /v1/admin/catalog/products/bulk-import` — `Content-Type: application/x-ndjson`; body is JSON-Lines, one product-create payload per line. Response is JSON-Lines: `{ rowIndex, status: "ok"|"error", productId?, error? }`.

### Reads for admin
- `GET /v1/admin/catalog/products?status=&q=&page=&pageSize=` — full admin DTO, shows all statuses.
- `GET /v1/admin/catalog/products/{id}` — admin DTO with workflow state, locale variants side-by-side, validation errors, variant-generation status, audit-tail pointer.

---

## Reason codes (subset)

`catalog.product.not_found`, `catalog.product.invalid_transition`, `catalog.publish.media_required`, `catalog.publish.locale_required`, `catalog.publish.market_unconfigured`, `catalog.restricted.verification_required`, `catalog.brand.unknown`, `catalog.category.cycle_detected`, `catalog.category.in_use`, `catalog.schedule.past_time`, `catalog.attributes.schema_violation`, `catalog.slug.immutable`, `catalog.bulk.row_idempotent_duplicate`.
