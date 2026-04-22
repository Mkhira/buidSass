# Feature Specification: Catalog (v1)

**Feature Branch**: `phase-1B-specs`
**Feature Number**: `005-catalog`
**Phase Assignment**: Phase 1B · Milestone 2 · Lane A (backend)
**Created**: 2026-04-22
**Input**: `docs/implementation-plan.md` §005; constitution v1.0.0 (Principles 5, 6, 8, 10, 15, 16, 20, 22, 23, 25, 26, 27, 28, 29); ADR-001, ADR-003, ADR-004, ADR-010.

---

## Clarifications

### Session 2026-04-22

- Q1: Attribute storage model → **A: Hybrid.** Fixed core columns (SKU, barcode, brand, category, restriction flag, price-hint) + a JSONB `attributes` bag for domain-specific fields (e.g. `active_ingredient`, `dosage`, `shaft_length_mm`). Enables SQL indexing on critical fields while keeping a low-ceremony path for long-tail attributes.
- Q2: Category tree model → **B: Closure table.** Proper hierarchical queries, arbitrary depth, stable re-parenting, fits Meilisearch facet generation better than nested-set.
- Q3: Media storage + variants → **A: Originals in storage abstraction (spec 003), 4 derived variants (thumb 96×96, card 320×320, detail 960w, hero 1600w) generated async via worker queue.** Web/Flutter clients request variant by name.
- Q4: Document linkage (MSDS, spec sheets, regulatory) → **A: Discrete `product_documents` table** with type enum (`msds`, `datasheet`, `regulatory_cert`, `ifu`, `brochure`) and locale. Not shoved into `attributes` JSONB.
- Q5: Publishing model → **C: Draft → Review → Published, with scheduled-publish support.** Review gate is admin role-scoped (catalog editor can submit; catalog publisher can publish). Scheduled publish unlocks Phase 1D marketing workflows.

---

## User Scenarios & Testing

### User Story 1 — Browse catalog as an unauthenticated visitor (Priority: P1)

A dentist in Riyadh opens the storefront, browses by category, filters to one brand, reads a product detail page in Arabic, sees the price, and understands which products require professional verification to purchase.

**Acceptance Scenarios** (Given/When/Then):
1. *Given* an anonymous visitor on the KSA storefront, *when* they navigate to a category page, *then* they see only `published` products for market `ksa`, localized to their selected locale (`ar` by default), with prices in SAR and VAT-inclusive copy.
2. *Given* an anonymous visitor viewing a restricted product, *when* the product detail page loads, *then* they see the price, a visible "requires professional verification to purchase" badge, and the add-to-cart action is disabled with a clear reason (not hidden — Principle 8).
3. *Given* an unpublished (draft) product, *when* any unauthenticated request queries its slug, *then* it returns `404`, regardless of whether the ID is guessable.
4. *Given* a product with Arabic + English content, *when* the visitor switches locale from `ar` to `en`, *then* every visible field (name, description, attributes, alt text) renders in the selected locale, falling back to the other locale with an `x-locale-fallback` header if a field is missing.

---

### User Story 2 — Catalog editor maintains the catalog (Priority: P1)

A catalog editor admin creates a new product with bilingual content, attaches media + a datasheet, assigns it to a category, marks it as restricted, and submits it for review. A catalog publisher reviews and publishes it. Audit captures every step.

**Acceptance Scenarios**:
1. *Given* a catalog-editor admin, *when* they POST a new product with `status=draft` and bilingual fields, *then* the product is created, not visible to customers, and an `audit_log_entries` row records the create.
2. *Given* a draft product, *when* the editor uploads 3 images via the media endpoint, *then* originals land in the storage abstraction and a background worker dispatches variant generation jobs for all 4 variant sizes.
3. *Given* a draft product with all required fields, *when* the editor transitions it `draft → review`, *then* the product enters `in_review` state and a publisher-facing queue surfaces it.
4. *Given* a product `in_review`, *when* a catalog publisher transitions it `review → published`, *then* the product becomes visible to customers in its configured markets and `published_at` is recorded.
5. *Given* a published product, *when* the editor marks it `status=archived`, *then* it disappears from customer listings but remains referenceable by historical orders, and search indexer removes it asynchronously.
6. *Given* any catalog mutation, *when* it completes, *then* the audit log carries actor, before/after snapshot of tracked fields, and correlation-id.

---

### User Story 3 — Restricted-product gate is consumable by downstream specs (Priority: P1)

Spec 008 (inventory reservation) and spec 009 (cart validation) need a single, stable query to answer "is this product restricted in this market, for this customer?" without duplicating logic.

**Acceptance Scenarios**:
1. *Given* a product marked `restricted=true` and an unverified customer, *when* spec 009 calls the restriction check API, *then* it returns `{ allowed: false, reasonCode: "catalog.restricted.verification_required" }`.
2. *Given* the same product and a verified customer, *when* the check runs, *then* `{ allowed: true }`. (Verification decisions come from spec 020 in Phase 1D; for launch, the check consumes the identity claim `professional_verified = true` seeded via spec 004 admin tooling.)
3. *Given* a product restricted in KSA but not EG, *when* the check runs for an EG-market customer, *then* `{ allowed: true }`.

---

### User Story 4 — Schedule a future publish (Priority: P2)

A marketing-leaning catalog publisher schedules a product to publish at 08:00 KSA time on launch day.

**Acceptance Scenarios**:
1. *Given* a product `in_review`, *when* the publisher submits a `published_at` in the future with `action=schedule`, *then* the product status becomes `scheduled` and a worker promotes it to `published` at the scheduled time.
2. *Given* a `scheduled` product, *when* the publisher cancels the schedule, *then* it returns to `in_review` and the scheduled job is cancelled.

---

### User Story 5 — Brand & manufacturer discipline (Priority: P2)

Catalog editors select brands and manufacturers from a curated list; ad-hoc free-text values are rejected so search facets stay clean.

**Acceptance Scenarios**:
1. *Given* a catalog editor creating a product, *when* they submit `brand_id` that doesn't exist, *then* the API returns `400 catalog.brand.unknown`.
2. *Given* a new brand needed, *when* the editor calls the brand-create endpoint (catalog-editor permission), *then* the brand is created and immediately usable on products.

---

### User Story 6 — Multi-vendor-ready catalog ownership (Priority: P2)

Every product, brand, and media asset carries `owner_id` and `vendor_id` (nullable) so that when marketplace expansion lands in Phase 2, vendor-scoped queries are a data-layer change, not a schema migration.

**Acceptance Scenarios**:
1. *Given* any product row, *when* it is created at launch, *then* `owner_id = platform` and `vendor_id = null`.
2. *Given* admin queries that list products, *when* executed, *then* queries filter by `vendor_id IS NULL OR vendor_id = @filter`, and the absence of a filter returns only platform-owned rows by default — ensuring a future vendor row never leaks to the single-vendor admin surface unless explicitly requested.

---

### Edge Cases
1. Product added to a market where the market config is missing → reject with `catalog.market.unconfigured`.
2. Product with zero active images at publish time → `400 catalog.publish.media_required` (minimum one media asset for customer-visible products).
3. Product with Arabic name but no English name at publish time → allowed if the configured market mandates Arabic only; otherwise `400 catalog.publish.locale_required`.
4. Circular category ancestry (catalog editor tries to make a node its own ancestor) → `400 catalog.category.cycle_detected`.
5. Archived product referenced by historical orders → MUST remain resolvable by ID; only listing/search removal is required.
6. Media variant job failing repeatedly → product can still publish with original, variants render as the original URL with a warning header; operator alert emitted.
7. Category rename → product URLs remain stable because slugs live on products, not categories.
8. Scheduled publish time in the past → `400 catalog.schedule.past_time`.
9. Bulk import of 10k products → streaming endpoint + idempotency key; partial failures are per-row, transaction-per-row to isolate failures.
10. Restricted-product status toggled after customer has product in cart → cart revalidation flag set; resolved in spec 009.

---

## Requirements

### Functional (FR-)

**Catalog hierarchy**
- **FR-001**: System MUST support a category tree via a closure-table model allowing arbitrary depth; every category has a slug unique within its parent, bilingual names, an active/inactive flag, and a display-order field.
- **FR-002**: System MUST reject operations that would create a cycle in the category tree.
- **FR-003**: Categories MUST support re-parenting as a single transactional operation that updates the closure table consistently.

**Brands & manufacturers**
- **FR-004**: System MUST maintain a curated `brands` entity and a curated `manufacturers` entity, each with bilingual names and optional logo media.
- **FR-005**: Every product MUST reference exactly one brand and MAY reference zero or one manufacturer.

**Product model**
- **FR-006**: Product MUST carry: `id`, `sku` (unique), `barcode` (optional, indexed), `brand_id`, `category_ids` (many-to-many, at least one primary), `market_codes` (array of `eg`/`ksa`), bilingual `name/description/short_description`, `slug` (unique within market), `status` (draft/review/scheduled/published/archived), `restricted` flag, `restriction_reason_code`, `attributes` JSONB bag, `owner_id` (`platform` at launch), `vendor_id` (null at launch).
- **FR-007**: Product `attributes` JSONB MUST conform to a per-category schema registered in `category_attribute_schemas`; unknown keys rejected at write time.
- **FR-008**: Product slugs MUST be immutable once first published.
- **FR-009**: Product `market_codes` MUST be a subset of configured markets (Principle 5) — write rejected otherwise.

**Publishing workflow**
- **FR-010**: Product status transitions MUST follow a state machine: `draft → in_review → (scheduled | published) → archived`, with `in_review → draft` and `scheduled → in_review` reverse paths.
- **FR-011**: `in_review → published` requires the `catalog.product.publish` permission; `draft → in_review` requires `catalog.product.submit`.
- **FR-012**: Scheduled-publish rows MUST be promoted by a worker at/after `published_at`, within 60 s of the scheduled time.
- **FR-013**: Archived products MUST remain resolvable by ID and SKU for order/invoice references but MUST NOT appear in customer listings or storefront search.

**Media + documents**
- **FR-014**: Product media MUST reference the storage abstraction from spec 003; original plus 4 variants (thumb, card, detail, hero) generated async.
- **FR-015**: Media MUST carry per-locale alt text; publish is blocked if primary media lacks alt text in every market-configured locale.
- **FR-016**: Product documents MUST live in `product_documents` keyed by type (`msds`, `datasheet`, `regulatory_cert`, `ifu`, `brochure`) and locale.
- **FR-017**: Media + document URLs MUST be content-addressed (hash-suffixed) so CDN caching is safe.

**Restriction enforcement**
- **FR-018**: System MUST expose a read API `GET /v1/catalog/restrictions/check` consumable by spec 008/009/010 returning `{ allowed, reasonCode }` given `(product_id, market_code, customer_verification_state)`.
- **FR-019**: Restriction evaluation is per-market; a single product MAY be restricted in KSA and not in EG.
- **FR-020**: Restriction decisions MUST be cached for ≤ 5 s per `(product_id, market_code, verification_state)` tuple in the API layer; invalidation occurs on product publish/status change.

**Search integration seam**
- **FR-021**: Every product publish/archive/field change MUST emit a domain event (`catalog.product.published`, `catalog.product.archived`, `catalog.product.field_updated`) that spec 006 subscribes to; the event carries enough fields to rebuild the Meilisearch index row without a DB round-trip.
- **FR-022**: Search is a separate service boundary (Principle 12/26); this spec MUST NOT directly call the search engine.

**Bulk import**
- **FR-023**: System MUST provide `POST /v1/admin/catalog/products/bulk-import` accepting a streaming JSON-Lines body, with an idempotency key per row; errors returned per row, successful rows committed.

**Audit**
- **FR-024**: Every catalog write MUST emit an audit event via spec 003's `IAuditEventPublisher` with actor, correlation-id, before/after snapshot of changed fields (`attributes` diff field-by-field, not as a blob).

**Multi-vendor-ready**
- **FR-025**: Every product, brand, and media row MUST carry `owner_id` (default `platform`) and nullable `vendor_id`; all admin list queries default to filtering `vendor_id IS NULL`.

**DTOs**
- **FR-026**: Customer-facing product DTO MUST omit draft, review, scheduled, and archived products; MUST return localized fields per `Accept-Language`; MUST include a `restrictionBadge` field when `restricted = true`.
- **FR-027**: Admin-facing product DTO MUST include full workflow state, locale variants side-by-side, attribute schema validation errors, media variant generation status, and audit-tail pointer.

### Key Entities

- **Category** — tree node; closure-table references.
- **CategoryAttributeSchema** — per-category JSON-Schema applied to product `attributes`.
- **Brand** — curated brand, bilingual.
- **Manufacturer** — curated manufacturer, bilingual.
- **Product** — the main aggregate.
- **ProductCategory** — m2m product↔category (with `is_primary` flag).
- **ProductMedia** — per-product media asset reference + variants + alt text.
- **ProductDocument** — datasheet/MSDS/etc.
- **ProductStateTransition** — audit row per status transition.
- **ScheduledPublish** — queued work row (consumed by worker).
- **CatalogEvent** — outbox row emitted to spec 006.

---

## Success Criteria (SC-)

- **SC-001**: A catalog editor can create, submit, and publish a fully-populated product in ≤ 10 minutes end-to-end on a warm cache.
- **SC-002**: Customer category-page p95 ≤ 250 ms for a 500-product category, including facet counts, excluding search (which is spec 006).
- **SC-003**: Product detail page p95 ≤ 200 ms for a warm-cache product with 6 media variants.
- **SC-004**: Scheduled-publish promotion happens within 60 s of `published_at`.
- **SC-005**: 100 % of catalog writes emit an audit event with before/after snapshots.
- **SC-006**: Restriction check p95 ≤ 20 ms (served from cache); miss-rate ≤ 5 %.
- **SC-007**: Zero customer-visible leakage of draft/review/archived products, verified by an integration test that probes every non-published status by ID.
- **SC-008**: Every required field at publish time has Arabic + English values; publish is blocked otherwise.
- **SC-009**: Bulk import can land 10,000 products in ≤ 10 minutes with per-row error reporting.
- **SC-010**: Media variant generation success rate ≥ 99.5 % within 10 minutes of upload.

---

## Dependencies

- Spec 004 (identity) — for admin auth, RBAC permissions (`catalog.*`), audit emission actor.
- Spec 003 — storage abstraction, audit event publisher, MessageFormat.NET, correlation-id.
- A1 environments — Postgres 16, object storage (Azure Blob or MinIO in Dev).
- Spec 006 (search) — consumes events emitted by this spec (search is not a dependency of catalog; catalog is a dependency of search).

## Assumptions

- Category and brand counts at launch ≤ 500 each; schema can handle 10× without redesign.
- Product count at launch 5k–20k; designed for 200k without rework.
- Attribute schemas authored manually by product leadership and committed to a YAML file compiled into the `category_attribute_schemas` seeder; UI for editing these schemas is deferred (Phase 1.5).
- Media variants use `ImageSharp` or `Magick.NET-Q8-AnyCPU`; picked in research phase.
- Draft/review gating uses the RBAC surface from spec 004; reviewers for launch are super-admin + a new `catalog.publisher` role.

## Out of Scope

- **Search** — spec 006.
- **Pricing** — spec 007-a (price is separate; catalog does not own price values).
- **Inventory** — spec 008.
- **Admin UI** — spec 016 (catalog admin web).
- **Customer app screens** — spec 014.
- **Vendor onboarding** — Phase 2 marketplace expansion.
- **Synonym management UI** — Phase 1.5.
- **Category-level marketing banners** — consumed from spec 022 CMS.
