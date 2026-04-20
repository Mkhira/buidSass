# Phase 0 Research — Catalog (005)

**Feature**: `specs/phase-1B/005-catalog/spec.md`
**Plan**: `./plan.md`

Each decision below lists the chosen approach, why it was chosen, and the alternatives that were evaluated and rejected.

---

## 1. Category tree storage: materialized path + parent_id

- **Decision**: Store categories with both a `parent_id` pointer and a `path` column (materialized path, e.g. `0001.0003.0008`) kept in sync by the `CreateCategory` / `MoveCategory` handlers. Sibling ordering uses an integer `position` column; reorders are done in a single transaction that rewrites all siblings in the affected parent.
- **Rationale**: Gives O(1) parent/child queries via `parent_id` and O(1) subtree queries via `path LIKE '0001.0003.%'`. Matches EF Core 9 + Postgres naturally (no extensions needed). Cheap to compute on moves because the max depth is 6 (FR-001).
- **Alternatives**: (a) Pure adjacency list → requires recursive CTEs for subtree reads, which the customer listing hits on every category page; (b) `ltree` Postgres extension → extra provider setup for marginal gain; (c) Nested sets (left/right) → painful reorder cost that scales with total tree size.

## 2. Cycle detection on category move

- **Decision**: In the `MoveCategory` handler, read the target parent's full `path`; reject if the path contains the moving node's id. Also reject if the resulting depth > 6.
- **Rationale**: O(1) check using the materialized path. No graph traversal needed.
- **Alternatives**: Recursive traversal of parent chain — strictly more expensive, same correctness.

## 3. Product → Variant grain

- **Decision**: Products own 1..N `product_variants`. Variant owns SKU, optional barcode, position, `active`, `xmin` row-version, typed axis values (`product_variant_axes` typed via `TaxonomyKey`), optional media overlay and attribute overlay. Restriction metadata is declared at the product level; variants inherit.
- **Rationale**: Per Clarification Q1 — dental catalog reality (pack sizes, gauges, shades) requires variants from day one. Downstream inventory (spec 008) and orders (spec 011) key off a single SKU column, so putting SKU on the variant makes both integrations clean.
- **Alternatives**: Single-SKU product (rejected in clarifications); sibling products linked by group id (rejected — duplicates content and breaks the customer-facing picker UX).

## 4. SKU uniqueness: partial unique index

- **Decision**: `CREATE UNIQUE INDEX ix_product_variants_sku_active ON catalog.product_variants (sku) WHERE status <> 'archived';`
- **Rationale**: Per Clarification Q3 — archiving a variant frees its SKU for reuse. Partial unique index enforces this at the database level with zero application logic. The reuse event is still audited via FR-026.
- **Alternatives**: Full unique index (rejected — creates admin dead-ends); application-level check (rejected — race-prone).

## 5. Attribute typing: taxonomy-key reference table

- **Decision**: `taxonomy_keys` stores `key` (PK), `value_type` (`string` | `number` | `boolean` | `enum`), `unit` (optional, e.g. `mm`), `display_label_ar`, `display_label_en`, `enum_values` (jsonb, only when `value_type = enum`). Product attributes store `(product_id, key, value_text, value_num, value_bool, enum_code)` with a CHECK constraint that exactly one typed column is populated based on the referenced key's `value_type`.
- **Rationale**: Per Clarification Q5 — taxonomy is migration-seeded only at launch. Reference integrity is guaranteed by FK; mixed-type drift (Edge Case) is rejected by CHECK.
- **Alternatives**: Freeform `jsonb` attribute bag — rejected because FR-010 explicitly rejects free-form keys; a fully-typed separate table per value_type — over-engineered for Phase 1B scale.

## 6. Optimistic concurrency: Postgres `xmin`

- **Decision**: Use Postgres system column `xmin` as the row-version on `products`, `product_variants`, `categories`, `brands`, `manufacturers`. EF Core maps it via `[Timestamp]`-equivalent `IsRowVersion()`.
- **Rationale**: Zero schema cost; matches the pattern already used in spec 004 for `admin_perm_version`. Conflict errors surface as `DbUpdateConcurrencyException`, mapped to a localized 409 envelope.
- **Alternatives**: Explicit `row_version bigint` incremented in handlers — more code, same outcome.

## 7. Media upload pipeline

- **Decision**: Admin submits a multipart request to `/admin/catalog/products/{productId}/media`. The handler (a) validates MIME ∈ {JPEG, PNG, WebP, AVIF} and size ≤ 8 MB using ImageSharp pre-parse, (b) stores via `IObjectStorage.UploadAsync` (spec 003), (c) calls `IVirusScanner.ScanAsync` (spec 003), (d) inserts a `product_media` row with `virus_scan_verdict = clean` only on success. Documents follow the same flow with MIME ∈ {PDF, PNG} and ≤ 20 MB. Video is URL-only — no upload, no scan, stored as an external reference on `product_documents` with `type_tag = external-video-link`.
- **Rationale**: Per Clarification Q2. Keeps the scanner boundary honest: a row never exists in `product_media` / `product_documents` until the verdict is clean. Rejected uploads leak nothing.
- **Alternatives**: Insert-then-scan (rejected — creates window where rejected asset is technically linked); sidecar scanner queue (deferred to Phase 1.5 when volume justifies async pipeline).

## 8. Customer DTO vs admin DTO separation

- **Decision**: Two distinct MediatR query handlers: `GetCustomerProductDetailQuery` and `GetAdminProductDetailQuery`. Each has its own DTO record type (`CustomerProductDetail`, `AdminProductDetail`). The customer DTO returns `published + active` only, picks `AR` or `EN` fields based on `Accept-Language`, and exposes restriction flag + rationale but never drafts. The admin DTO returns everything including `row_version`, draft state, and both locales.
- **Rationale**: FR-021 is explicit that the shapes diverge. Two handlers keep each read path simple and snapshot-testable. Common projections live in `Features/Catalog/Shared/Projections/`.
- **Alternatives**: One DTO with nullable fields — the customer surface would leak draft metadata structurally; error-prone.

## 9. Eligibility endpoint integration with spec 004

- **Decision**: `GET /products/{productId}/eligibility?customerId=...` (or inferred from auth). The handler reads the product, resolves the restriction mapping (`restriction_reason_code → policy_key`), and calls the spec-004 `/internal/authorize` endpoint with `policy_key` and `customerId`. Decision flows back as `allowed | blocked(reason_code, reason_copy_ar, reason_copy_en)`. Non-restricted products short-circuit to `allowed = true`.
- **Rationale**: FR-018 requires the catalog to own the mapping but delegate the policy. Keeps spec 004 as the single source of truth for identity + verification state. Spec 009 (cart) and spec 010 (checkout) call the same endpoint.
- **Alternatives**: Inline the policy in catalog (rejected — duplicates identity logic); issue a signed capability token (over-engineered for Phase 1B).

## 10. Domain events for search reindex

- **Decision**: Emit MediatR `INotification` events: `ProductCreated`, `ProductUpdated`, `ProductPublished`, `ProductArchived`, `ProductVariantChanged`, `ProductMediaChanged`, `CategoryTreeChanged`, `BrandChanged`. Spec 006 subscribes to these in-process. Every event carries `productId`, `correlationId`, `occurredAt`, and the tenant/market code. SC-008 tests that spec 006 receives the event within 2 seconds.
- **Rationale**: MediatR keeps it in-process (per ADR-003) and avoids a message bus in Phase 1B. Future bus swap is out of scope.
- **Alternatives**: Outbox table + background dispatcher (deferred until we actually have a bus); direct reindex call (rejected — couples catalog to the search module).

## 11. Soft delete semantics

- **Decision**: `categories`, `brands`, `manufacturers`, `products`, `product_variants`, `product_media`, `product_documents` each carry a `deleted_at` column. Query filters in `CatalogDbContext` exclude soft-deleted rows globally; admin surfaces that need to see trash use `IgnoreQueryFilters()` on a per-handler basis. Hard delete is never offered.
- **Rationale**: Preserves referential integrity with inventory (spec 008) and orders (spec 011) which reference variant SKU. Matches ADR-004 convention.
- **Alternatives**: Hard delete with FK cascade — rejected because orders MUST retain the variant they reference.

## 12. Variant SKU generator strategy

- **Decision**: SKU is admin-supplied (free text, validated `^[A-Z0-9][A-Z0-9-]{2,31}$`). The system does NOT auto-generate SKUs; the admin UI MAY offer a generator helper but the API accepts only the provided string. Collisions return a localized 409.
- **Rationale**: Dental supply SKUs typically follow vendor/manufacturer conventions (e.g. `3M-E01-100`). Auto-generation would break the human-readable convention already used by suppliers.
- **Alternatives**: Auto-generate from brand + category + counter (rejected — fights existing supplier conventions).

## 13. Availability (`available` boolean) computation

- **Decision**: Customer DTO reads `available` from a join view `catalog.v_variant_availability` that spec 008 owns (populated from its own `stock_levels` table). If spec 008 is not yet deployed, the view returns `true` for all active variants. The catalog module defines the interface (`IVariantAvailabilityReader`) and a `StaticTrueAvailabilityReader` default; spec 008 replaces the registration.
- **Rationale**: Keeps the delegation contract explicit and lets spec 005 ship before spec 008 without a hard dependency.
- **Alternatives**: Catalog computes stock itself (rejected — violates spec 008 ownership).

## 14. Price token contract with spec 007-a

- **Decision**: Catalog returns `price_token: { product_id, variant_id, market_code }` — no numeric price. Spec 007-a consumes the token + caller context (customer tier, currency, VAT) and returns the rendered price. The token is a structured record, not a bearer token; it's purely a key.
- **Rationale**: Keeps pricing authoritative in 007-a and lets catalog stay content-only.
- **Alternatives**: Include base price in catalog DTO — rejected because pricing rules (tier, B2B, promotion) require 007-a context anyway, so the base price would be misleading.

## 15. Taxonomy-key seed scope for launch

- **Decision**: Seed a minimum viable set of 12 keys covering dental-commerce essentials: `pack_size` (number, unit `count`), `material` (enum), `sterile` (boolean), `size_mm` (number, unit `mm`), `gauge` (enum), `shade` (enum), `color` (enum), `brand_model_code` (string), `origin_country` (enum), `expiry_critical` (boolean), `single_use` (boolean), `prescription_only` (boolean). Seed is a YAML file checked into the repo and applied by an EF migration.
- **Rationale**: Concrete enough to validate FR-010 at launch; small enough to review editorially.
- **Alternatives**: Seed a larger catalog — rejected because review burden grows; start tight, expand in Phase 1.5.

---

**All NEEDS CLARIFICATION resolved**. Five items (variants grain, media limits, SKU reuse, category depth, taxonomy self-service) were locked in the `/speckit-clarify` session on 2026-04-20; the remaining 10 decisions above are plan-level defaults drawn from constitution principles + the spec-004 pattern.
