# Research — Catalog v1 (Spec 005)

**Date**: 2026-04-22. All NEEDS CLARIFICATION resolved.

## R1 · Category tree model
**Decision**: Closure table with `(ancestor_id, descendant_id, depth)` plus self-references (`depth=0`). App-level maintenance (no triggers) to keep migrations portable. Re-parent is a single transaction that rewrites the descendant subtree's ancestor rows.
**Rejected**: Nested-set (bad under re-parent), adjacency-list (bad for ancestor queries), materialized path (breaks on category rename or ID change).

## R2 · Attribute storage
**Decision**: Hybrid. Fixed columns for SKU, barcode, brand, restriction, market_codes, locale names/descriptions. `attributes` JSONB validated per-category via NJsonSchema on write. GIN index on `(category_primary_id, attributes)` for facet-assist queries; Meilisearch owns the actual facet answer (spec 006).
**Rejected**: Pure EAV (query nightmare), pure JSONB (no indexed SKU lookup).

## R3 · Attribute schema authoring
**Decision**: Schemas authored as YAML files under `services/backend_api/Modules/Catalog/AttributeSchemas/` (one per category), compiled into the `category_attribute_schemas` table by `CategoryAttributeSchemaSeeder`. Schema changes ship via PR → seeder re-run. Admin UI for authoring schemas deferred to Phase 1.5.
**Rejected**: DB-authored schemas at launch — lacks code review, diff tooling, and version control.

## R4 · Media variant generator
**Decision**: `SixLabors.ImageSharp` v3. Variants: thumb 96×96 (avatar/product-card hero on storefront), card 320×320 (category grid), detail 960w (product-detail desktop), hero 1600w (product-detail high-DPI). JPEG + WebP output per variant. Async via `MediaVariantWorker`. Originals retained.
**Fallback**: `Magick.NET-Q8-AnyCPU` if SLPL license becomes an issue. Both can plug behind `IImageVariantGenerator`.
**Rejected**: On-demand generation (CDN thrashing), imgproxy sidecar (extra ops surface, deferred until scale requires it).

## R5 · Storage seam
**Decision**: Reuse spec 003's `IStorageProvider`. Content-addressed paths: `catalog/{product_id}/{sha256(original_bytes)[:16]}/{variant_name}.{ext}`. Spec 003's provider maps to Azure Blob in Staging/Prod, MinIO in Dev.
**Rejected**: New catalog-specific storage — duplicates spec 003 infra.

## R6 · Product state machine
**Decision**: `ProductStatus ∈ { draft, in_review, scheduled, published, archived }`. Transitions:
- `draft → in_review` (catalog.product.submit)
- `in_review → draft` (catalog.product.submit or publisher reject)
- `in_review → scheduled` (catalog.product.publish + `published_at` future)
- `in_review → published` (catalog.product.publish + `published_at` null or past)
- `scheduled → published` (worker auto, at/after `published_at`)
- `scheduled → in_review` (catalog.product.publish — cancel schedule)
- `published → archived` (catalog.product.archive)
- `archived → draft` (catalog.product.unarchive — operator-only, deferred)

Every transition audit-emits with before/after snapshot.

## R7 · Restriction evaluation + cache
**Decision**: `RestrictionEvaluator.Check(productId, marketCode, verificationState) → { allowed, reasonCode }`. In-process sliding cache keyed by `(productId, marketCode, verificationState)`, 5-second TTL. Invalidated on product publish/status-change via an in-proc event bus. Cache hit-rate target ≥ 95 % (SC-006).
**Rejected**: Redis cache — Postgres + in-proc is sufficient for launch volume.

## R8 · Outbox → spec 006
**Decision**: `catalog_outbox` table with `{ id, event_type, payload_json, committed_at, dispatched_at NULL }`. `CatalogOutboxDispatcherWorker` polls every 2 s, dispatches via `ICatalogEventSubscriber` (implemented by spec 006's indexer). At-least-once semantics; consumers idempotent. Transactional-outbox pattern: outbox row written in the same EF transaction as the catalog mutation.
**Rejected**: Direct HTTP call to Meilisearch (blocks publish on search-engine outage), message broker (infra we don't have yet).

## R9 · Bulk import
**Decision**: Streaming JSON-Lines endpoint. Each line parsed → validated → committed in its own short transaction. Per-row idempotency key in `X-Idempotency-Key` header hashed with row index. Partial failures returned as a JSON-Lines response with `{ rowIndex, error }`. Rate-limited to 1 concurrent bulk-import per admin.
**Rejected**: CSV (locale encoding headaches), single-transaction bulk (one bad row blocks 9999 good rows).

## R10 · Locale fallback
**Decision**: `Accept-Language` preferred. If preferred locale missing for a field, fall back to the other market-configured locale and set response header `x-locale-fallback: {field: originalLocale}`. Blocks publish only when BOTH locales missing (FR-008, P4).
**Rejected**: Hard-block on missing preferred locale — frustrating for partial translations mid-rollout.

## R11 · Search seam event shape
**Decision**: `ProductPublishedEvent` payload includes full search-indexable projection: `{ id, sku, barcode, slug, names, descriptions_short, brand, categories, attributes, restriction, price_hint, market_codes, media_primary_urls }`. Avoids spec 006 round-tripping to catalog on every event.
**Rejected**: Thin event (`{ id }`) — forces spec 006 to re-read catalog on every publish; slower.

## R12 · Testing
**Decision**: Testcontainers Postgres; no SQLite fallback. Property tests for closure-table re-parent invariants. Golden-file tests for customer DTO. Contract tests per Acceptance Scenario.

## Open items handed to tasks
- Attribute schema YAML templates for launch categories (dentist-specific, medical-specific) — task-level.
- Brand + manufacturer seed CSV — task-level.
- ZATCA/ETA VAT calculation split lives in spec 007-a, not here.
