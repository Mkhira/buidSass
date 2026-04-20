# Feature Specification: Catalog

**Feature Branch**: `005-catalog`
**Created**: 2026-04-20
**Status**: Draft
**Input**: User description: "Phase 1B spec 005 · catalog — product/brand/category/media/document model with restriction metadata (per docs/implementation-plan.md §Phase 1B)"

**Phase**: 1B — Core Commerce
**Depends on**: 004 (identity-and-access) — admin auth + RBAC for catalog editors; verified-professional policy hook consumed by restriction enforcement. Also Phase 1A (003 storage + PDF + localization).
**Enables**: 006 (search indexing source of truth), 008 (inventory per-SKU), 009 (cart line-item snapshot), 010 (checkout restricted-product gate enforcement), 016 (admin-catalog UI), 022 (reviews link products to verified deliveries), 024 (CMS featured-section referencing products).
**Constitution anchors**: Principles 2 (real operational depth), 4 (AR + EN editorial), 6 (multi-vendor-ready ownership fields), 7 (brand palette honored in media/UI via spec 003), 8 (restricted products remain visible with prices, purchase gated), 11 (inventory-ready data shape via SKU), 12 (search-ready attributes), 15 (reviews eligibility linkage), 25 (audit on catalog edits), 27 (every UX state captured — loading, empty, restricted, error), 28 (AI-build standard), 29 (required spec output standard).

---

## Clarifications

### Session 2026-04-20

- Q: Does a Product own one SKU, or does a Product have many Variants each with their own SKU? → A: Product → 1..N Variants. Variant owns SKU, barcode, attributes-overlay, media-overlay; Product carries shared content (name, description, brand, category, base restriction). Customer detail page renders a variant picker.
- Q: What hard limits should catalog enforce per-asset at upload time? → A: Image ≤ 8 MB (JPEG, PNG, WebP, AVIF); PDF ≤ 20 MB (document uploads accept PDF + PNG); video is link-only, no direct upload.
- Q: Can an archived/inactive variant's SKU be reused by a new active variant? → A: Yes — SKU is unique only among non-archived variants (partial unique index). Archiving frees the SKU; the reuse is audited via FR-026.
- Q: What is the maximum allowed category tree depth? → A: 6 (root + 5 sub-levels).
- Q: At launch, can catalog-editor admins create new taxonomy keys through the admin UI, or is taxonomy-key authoring migration-only? → A: Migration-seeded only at launch. The admin UI may view taxonomy keys (read-only); creation, edit, and delete ship via migration. Self-service authoring is deferred to Phase 1.5.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Customer browses the catalog by category and sees prices for every product (Priority: P1)

An unauthenticated or authenticated visitor opens the storefront, selects a category from the tree, scrolls through a paged product listing, and opens a product detail page. They see localized name, description, images, specs, price, availability, and — for restricted products — a clearly marked restriction badge with an explanation that purchase requires professional verification. Prices remain visible regardless of verification status per Constitution Principle 8.

**Why this priority**: Without customer-facing catalog read surface, no storefront exists. Every other commerce story (cart, checkout, order, review, B2B, CMS featured sections) depends on a live, browsable catalog. This is the MVP for spec 005.

**Independent Test**: Seed one category, one brand, one active non-restricted product and one active restricted product, both with AR + EN content. The customer-facing listing and detail endpoints return the expected payloads (price visible on both; restriction badge + rationale on the restricted one). Fully testable without any authenticated action.

**Acceptance Scenarios**:

1. **Given** an active category with N active products, **When** an unauthenticated customer requests the category listing, **Then** the system returns the products with localized name, price, primary image, availability indicator, and restriction badge where applicable.
2. **Given** a product whose `restricted_for_purchase = true`, **When** any visitor (authenticated or not, verified or not) requests the product detail, **Then** the product is visible, the price is shown, and a restriction reason is included in the payload in the requested locale (FR-017).
3. **Given** a product marked `active = false`, **When** a customer-facing endpoint lists or details it, **Then** the product is absent from customer responses while remaining visible in admin endpoints.
4. **Given** a product with AR + EN content, **When** the request carries `Accept-Language: ar`, **Then** the response returns Arabic fields with RTL-ready text and falls back to EN only for fields explicitly missing an AR translation (with a flag marking the fallback).
5. **Given** a category with no products, **When** a customer requests the listing, **Then** the system returns an empty-state payload (total=0, items=[]) so the UI can render an empty state rather than an error.

---

### User Story 2 — Admin creates a product with brand, category, media, documents, and restriction metadata (Priority: P1)

A catalog-editor admin logs into the admin surface, picks a brand and one or more categories, fills in AR + EN name and description, adds SKU and barcode, enters structured attributes (dimensions, material, pack size, etc.), uploads product images via the storage abstraction, uploads documents (spec sheet PDF, IFU), marks whether the product is restricted-for-purchase and why, and saves it as a draft. A second action publishes it. Every edit is audited with actor + before + after.

**Why this priority**: Without admin authoring, the catalog cannot be populated. The authoring surface is also the source-of-truth that 006 search, 008 inventory, and 016 admin-catalog consume.

**Independent Test**: A seeded catalog-editor admin (via spec 004 seed) creates a brand, a category, and a product through the admin endpoints, uploads one image and one PDF, toggles the restriction flag, saves as draft, then publishes. Listing on the customer surface shows the product only after publish. Every step produces an auditable event (FR-026).

**Acceptance Scenarios**:

1. **Given** an admin holding `catalog.write`, **When** they submit a valid product create payload, **Then** the system stores the product in draft state and returns its id and default DTO.
2. **Given** a draft product with AR + EN content and at least one primary image, **When** an admin holding `catalog.publish` publishes it, **Then** the product becomes visible on customer-facing endpoints and a `catalog.product.published` audit event is emitted.
3. **Given** an admin without `catalog.write` or `catalog.publish`, **When** they attempt the corresponding action, **Then** the system returns an authorization error and the attempt is recorded in the audit log.
4. **Given** an admin uploading an image or document, **When** they submit the file, **Then** the system routes the upload through the spec-003 storage abstraction, returns a signed URL, and links the asset to the product with alt text in AR + EN (where applicable).
5. **Given** an admin toggling `restricted_for_purchase = true`, **When** they save without providing a rationale, **Then** the system rejects the save and requires a rationale in AR + EN.

---

### User Story 3 — Admin manages the category tree (Priority: P1)

An admin creates a nested category tree with AR + EN names and slugs, reorders siblings, activates or deactivates nodes, and attaches products to one or more categories. Deactivating a non-leaf category does not orphan its subtree — the subtree is also hidden from customer responses but preserved in admin views.

**Why this priority**: The category tree is the primary navigation axis for customers and the primary filter for 006 search. Without it, Story 1 cannot render a usable storefront.

**Independent Test**: Seed an empty category set; through admin endpoints, create a 3-level tree, reorder two siblings, deactivate one subtree, then list customer-facing categories and confirm the deactivated subtree is absent. Admin endpoints still return the deactivated nodes with their `active=false` flag.

**Acceptance Scenarios**:

1. **Given** an admin with `catalog.write`, **When** they create a category under a parent, **Then** the system stores the node with the chosen parent, default position at the end of siblings, and active=true.
2. **Given** an admin dragging a category between siblings, **When** they commit the new order, **Then** the system updates positions atomically and the customer-facing tree reflects the new order.
3. **Given** an admin deactivating a node with children, **When** they commit, **Then** the node and all descendants are excluded from customer responses but remain in admin responses.
4. **Given** a product attached to multiple categories, **When** one of those categories is deactivated, **Then** the product remains visible through any still-active category it belongs to.

---

### User Story 4 — Admin authors brands and manufacturers (Priority: P2)

An admin creates a brand entity (name AR + EN, slug, logo, description, origin country) and optionally associates it with a manufacturer entity representing the regulatory/importer party. Brands are selectable when authoring products; manufacturers appear on tax-invoice footers (consumed by spec 012).

**Why this priority**: Required for complete product authoring but separable from the first round of admin acceptance tests; brands can be seeded for early MVP and fully authored later.

**Independent Test**: Create a brand with logo upload, create a manufacturer, attach them to a product, and confirm they appear in both admin DTO and customer-facing product detail DTO.

**Acceptance Scenarios**:

1. **Given** an admin with `catalog.write`, **When** they create a brand with AR + EN name and a logo upload, **Then** the brand is persisted, the logo is stored via the storage abstraction, and the brand is selectable when creating or editing products.
2. **Given** an admin editing a brand slug that is already in use, **When** they save, **Then** the system rejects the save and surfaces a localized conflict message.
3. **Given** a brand in use by ≥ 1 product, **When** an admin attempts to delete it, **Then** the system blocks the delete and suggests deactivation instead.

---

### User Story 5 — Admin authors rich product content (attributes, media, documents, spec sheets) (Priority: P2)

An admin authoring a product fills AR + EN name and description, adds structured attributes as name/value pairs (e.g., `pack_size: 100`, `material: stainless-steel`), uploads multiple images in order, uploads product documents (spec sheet PDF, instructions-for-use), and sets each image's alt text in AR + EN.

**Why this priority**: Needed for a credible launch, but the minimum-viable product-create flow can ship with a single image and no documents. Rich authoring is separable.

**Independent Test**: Create a product with three images (reordered), two documents (spec sheet + IFU), and five attribute pairs; retrieve via the admin DTO and customer-facing DTO; verify ordering, alt text localization, and document links are all present.

**Acceptance Scenarios**:

1. **Given** a product with three images in order A, B, C, **When** the admin swaps B and C, **Then** subsequent reads return A, C, B in that order and no extra images are lost.
2. **Given** an admin uploading a 30 MB PDF spec sheet, **When** the upload exceeds the 20 MB PDF maximum (FR-012), **Then** the system rejects the upload with a localized error naming the 20 MB limit.
3. **Given** structured attributes, **When** an admin saves the product, **Then** the attributes are stored as typed key/value pairs (string, number, boolean, enum) with localized display labels referenced by a `taxonomy_key` lookup table.

---

### User Story 6 — Restriction eligibility metadata drives purchase gating (Priority: P1)

An admin marks a product as `restricted_for_purchase = true` with a rationale (AR + EN) and a reason code (e.g., `dental-professional`). Spec 009 (cart) and spec 010 (checkout) consume a single eligibility check: given `customer_id` and `product_id`, "is this customer allowed to purchase this product?" answered by calling into the identity+verification hook established in spec 004 (and enriched in spec 020). The catalog ships the authoring of the metadata; 004 already exposes the `customer.verified-professional` policy.

**Why this priority**: Constitution Principle 8 is non-negotiable; restricted products MUST remain visible with prices and MUST block add-to-cart/checkout eligibility. Without catalog-owned metadata, downstream enforcement is impossible.

**Independent Test**: Seed one restricted and one non-restricted product. Call the catalog-owned eligibility endpoint with (a) a verified-professional customer id, (b) an unverified customer id, (c) no customer id; confirm the three outcomes are `allowed`, `blocked (requires-verification)`, and `blocked (requires-auth)` with localized reason copy.

**Acceptance Scenarios**:

1. **Given** a product with `restricted_for_purchase = true` and `restriction_reason_code = dental-professional`, **When** a verified-professional customer calls the eligibility endpoint, **Then** the response is `allowed = true`.
2. **Given** the same product, **When** an unverified authenticated customer calls the endpoint, **Then** the response is `allowed = false` with `reason_code = requires-verification` and localized reason copy.
3. **Given** the same product, **When** an unauthenticated visitor calls the endpoint, **Then** the response is `allowed = false` with `reason_code = requires-auth` and localized reason copy.
4. **Given** a non-restricted product, **When** any caller checks eligibility, **Then** the response is `allowed = true`.

---

### User Story 7 — Multi-vendor-ready ownership fields are populated but dormant at launch (Priority: P3)

Every catalog-owned row (products, brands, manufacturers, media, documents, categories) carries `owner_id` and an optional `vendor_id` column. At launch, `vendor_id` is NULL for every row — the platform is single-vendor operationally (Principle 6). Phase 2 marketplace expansion can populate `vendor_id` without schema changes or data rewrites.

**Why this priority**: Pure schema readiness. No launch UX consumes it. P3 because it must not be skipped (Principle 6 is mandatory) but it does not affect the launch user experience if nothing populates `vendor_id`.

**Independent Test**: Verify migration creates the columns with correct nullability and default; create a fixture row with a synthetic `vendor_id` and confirm it round-trips cleanly in admin DTOs.

**Acceptance Scenarios**:

1. **Given** any catalog-owned row created at launch, **When** inspected, **Then** `owner_id` is populated and `vendor_id` is NULL.
2. **Given** a future row populated with a `vendor_id`, **When** listed via admin endpoints, **Then** the value is preserved.

---

### Edge Cases

- A product is published with only an EN version (AR missing): the system rejects publish and requires AR copy before go-live (FR-028 editorial gate). Draft state may remain EN-only.
- A category is re-parented to one of its own descendants: the system rejects the move (cycle detection).
- A customer-facing listing paginates with page size > documented max: the system clamps to the max and returns a `clamped_page_size` flag.
- Two admins edit the same product concurrently: the system uses optimistic concurrency via a row version; the second write receives a conflict error with a prompt to reload.
- A product's SKU is changed after publish: the system blocks the change if the SKU is already referenced by inventory movements or orders (read-only after first referential use).
- A brand logo upload fails virus scan: the asset is rejected and never linked; the admin sees a localized error citing scan failure.
- An attribute key is used across products with mixed value types (e.g., `pack_size` as number on one, string on another): the taxonomy-key table enforces a declared type per key, and cross-type drift is rejected at validation.
- A restricted product's rationale is edited after publish: the edit is audited, the new rationale is shown on subsequent reads, and any active cart containing the product triggers a re-validation signal to spec 009 to re-check eligibility at next cart touch.
- Category tree reorder is partially applied (transaction fails mid-way): the system rolls back so no positions are corrupted.
- Media alt text is blank in one locale: save succeeds but customer DTOs fall back to the other locale's alt text with a `fallback_locale` flag.

---

## Requirements *(mandatory)*

### Functional Requirements

**Category tree**

- **FR-001**: System MUST model categories as a tree with a single root namespace (per tenant), supporting parent/child relationships, sibling ordering, and active/inactive flags. Maximum tree depth is 6 levels (root + 5 sub-levels); create and move operations that would exceed this depth MUST be rejected with a localized error.
- **FR-002**: System MUST enforce acyclic structure on every create or move operation.
- **FR-003**: System MUST exclude inactive categories and their descendants from customer-facing category and product listings while retaining them in admin views.
- **FR-004**: System MUST support reordering siblings within a parent; position changes MUST be atomic across the affected siblings.
- **FR-005**: System MUST allow a product to belong to one or more categories.

**Brands and manufacturers**

- **FR-006**: System MUST model brands with AR + EN name, a unique slug, a logo asset reference, a description in AR + EN, and an optional origin-country code.
- **FR-007**: System MUST model manufacturers with AR + EN name, a legal name field, and an optional regulatory-registration-number field; manufacturer data MAY appear on tax invoices (consumed by spec 012).
- **FR-008**: System MUST block deletion of a brand or manufacturer referenced by at least one product and MUST offer a deactivation path instead.

**Product model**

- **FR-009**: System MUST model products with brand reference, zero-or-more category references, optional manufacturer reference, AR + EN name, AR + EN marketing description, AR + EN short description, active flag, publish status (`draft`, `published`, `archived`), created-at, updated-at, row-version for optimistic concurrency, `owner_id`, and nullable `vendor_id`. A product owns 1..N variants; SKU and barcode belong to the variant, not the product.
- **FR-009a**: System MUST model product variants as child rows of a product. Each variant owns SKU (unique across non-archived variants platform-wide via a partial unique index; archiving a variant frees its SKU for reuse, and any reuse is captured in the audit log per FR-026), optional barcode, an ordered set of AR + EN variant-axis values (e.g., `pack_size: 100`, `shade: A2`), an optional media overlay (when a variant needs its own primary image), an optional attribute overlay (to add/override structured attributes for this variant only), an `active` flag, and a stable position for rendering in the variant picker. A product MUST have at least one active variant at publish time. Restriction metadata is declared at the product level (FR-016); variants inherit it and cannot override.
- **FR-010**: System MUST support structured attributes as typed key/value pairs (string, number, boolean, enum) backed by a taxonomy-key table that declares the type, display label (AR + EN), and unit (where applicable) for each key. Free-form attribute keys without a declared taxonomy row MUST be rejected. At launch, taxonomy-key rows are migration-seeded only; the admin UI exposes them read-only and there is no admin-facing create/edit/delete path for taxonomy keys in Phase 1B (self-service deferred to Phase 1.5).
- **FR-011**: System MUST support product media as an ordered collection of image assets, each with a storage reference, alt text in AR + EN, primary-image flag, and a stable position; media MUST pass virus scan before being linked. Accepted image types: JPEG, PNG, WebP, AVIF. Per-asset maximum: 8 MB. Uploads exceeding the limit or using other MIME types MUST be rejected at upload with a localized error naming the limit.
- **FR-012**: System MUST support product documents as a collection of file assets, each with a type tag (e.g., `spec-sheet`, `instructions-for-use`, `certification`), AR + EN title, and a storage reference. Accepted document types: PDF and PNG. Per-asset maximum: 20 MB. Video is link-only (an external URL reference stored on the product, no direct upload). Uploads exceeding the limit or using other MIME types MUST be rejected at upload.
- **FR-013**: System MUST block changes to a variant's SKU once it is referenced by any inventory movement or order record (referential freeze after first referential use). Deleting a referenced variant is also blocked; the variant MUST be deactivated instead.
- **FR-014**: System MUST model publish state transitions as `draft → published`, `published → archived`, `archived → draft`; every transition MUST carry actor, timestamp, and an audit event.
- **FR-015**: System MUST reject publish if AR name, EN name, AR description, EN description, or at least one primary image with AR and EN alt text is missing.

**Restriction metadata**

- **FR-016**: System MUST model per-product restriction metadata: `restricted_for_purchase` (boolean), `restriction_reason_code` (enum, seeded with at least `dental-professional`, `controlled-substance`, `institution-only`), `restriction_rationale_ar` (required when restricted), `restriction_rationale_en` (required when restricted).
- **FR-017**: System MUST keep restricted products visible on customer listing and detail endpoints with prices shown; the response MUST include the restriction flag and rationale in the caller's locale per Principle 8.
- **FR-018**: System MUST expose an eligibility endpoint — given a product id and an optional customer id — that returns one of `{ allowed }`, `{ blocked, reason-code, reason-copy }`. Eligibility MUST delegate to the identity/verification policy from spec 004 (`customer.verified-professional`) for `dental-professional` restrictions; other codes MAY require other policies authored in Phase 1D spec 020.

**Search-ready and inventory-ready shape**

- **FR-019**: System MUST emit a domain event on every product create, update, publish, archive, and price-range change so that spec 006 search can incrementally reindex without polling.
- **FR-020**: System MUST keep variant SKU the stable key for inventory (spec 008) and orders (spec 011) to reference a sellable unit; product id is the stable key for catalog navigation, reviews, and CMS references.

**Admin vs customer DTOs**

- **FR-021**: System MUST ship two distinct product DTO shapes — an admin DTO (all fields including draft/archived, restriction rationale in both locales, full audit metadata, row-version) and a customer DTO (published-and-active only, restriction flag + rationale in the caller's locale, no draft fields).
- **FR-022**: Customer-facing listing endpoints MUST support pagination (documented max page size), sort modes (relevance placeholder delegated to spec 006 search, newness, price ascending, price descending), and filters by category, brand, and availability. Price is delegated to spec 007-a; catalog returns a price token that spec 007-a resolves per caller context.

**Localization and editorial quality**

- **FR-023**: System MUST require AR + EN copy parity on all customer-visible strings (name, description, alt text for primary image, restriction rationale) before a product may be published.
- **FR-024**: System MUST surface localization fallback flags on customer DTOs where an optional field is missing in the requested locale and falls back to the other.

**Media pipeline and storage**

- **FR-025**: System MUST upload all media and documents via the spec-003 storage abstraction; every asset MUST receive a virus-scan verdict before it is linked, and rejected assets MUST not leak into product DTOs.

**Auditing and multi-vendor readiness**

- **FR-026**: System MUST emit auditable events for category CRUD, brand CRUD, manufacturer CRUD, product CRUD, product publish/archive transitions, restriction-metadata edits, media add/remove/reorder, and document add/remove, with actor, before, after, reason (where applicable), and correlation id per Principle 25.
- **FR-027**: System MUST carry `owner_id` and nullable `vendor_id` on every catalog-owned row at launch; `vendor_id` MUST be NULL for every row in Phase 1. The schema MUST NOT require migration to populate `vendor_id` in Phase 2.

**Concurrency and integrity**

- **FR-028**: System MUST enforce optimistic concurrency on product, category, brand, and manufacturer updates via a row-version; concurrent writes MUST resolve with a localized conflict error to the second writer.

### Key Entities

- **Category**: id, parent_id (nullable), position, active, AR + EN name, AR + EN slug, created/updated metadata, owner_id, nullable vendor_id.
- **Brand**: id, AR + EN name, unique slug, logo asset reference, AR + EN description, optional origin_country_code, active flag, owner_id, nullable vendor_id.
- **Manufacturer**: id, AR + EN name, legal_name, optional regulatory_registration_number, owner_id, nullable vendor_id.
- **Product**: id, brand_id, optional manufacturer_id, AR + EN name, AR + EN marketing description, AR + EN short description, publish_status, active flag, row_version, created/updated metadata, owner_id, nullable vendor_id, restricted_for_purchase flag, restriction_reason_code, AR + EN restriction_rationale.
- **ProductVariant**: id, product_id, SKU (unique across active variants), optional barcode, position, active flag, row_version, ordered AR + EN variant-axis values (typed via `TaxonomyKey`), optional media overlay, optional attribute overlay.
- **ProductCategory**: product_id + category_id junction (many-to-many).
- **ProductAttribute**: product_id, taxonomy_key, typed_value, locale-agnostic; display label resolved via `TaxonomyKey`.
- **TaxonomyKey**: key, value_type (`string` | `number` | `boolean` | `enum`), optional unit, AR + EN display label, optional enum value set.
- **ProductMedia**: id, product_id, position, is_primary, storage_ref, AR + EN alt_text, virus_scan_verdict.
- **ProductDocument**: id, product_id, type_tag, AR + EN title, storage_ref, virus_scan_verdict.
- **Restriction reason code**: enum seeded with at least `dental-professional`, `controlled-substance`, `institution-only`.
- **Audit Event** (emitted, not owned): consumed by spec 003 audit-log module.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A customer can load a category listing with 24 active products and see price, name, primary image, and availability indicator in the requested locale within 1.5 seconds at p95 under nominal load.
- **SC-002**: A catalog-editor admin can create a product with brand, one category, three images, one spec-sheet PDF, five structured attributes, AR + EN content, and restriction metadata in under 3 minutes in either locale.
- **SC-003**: 100% of published products have AR + EN name, AR + EN description, at least one primary image with AR + EN alt text, and (when restricted) AR + EN restriction rationale — verified by a release-candidate editorial report.
- **SC-004**: 100% of catalog mutations in the scope of Principle 25 produce an audit-log entry with actor, before, after, reason (where applicable), and correlation id on the release-candidate build.
- **SC-005**: 100% of restricted products remain visible on the customer surface with prices and the restriction badge + rationale in the caller's locale — verified by an automated snapshot test per seeded restriction reason code.
- **SC-006**: The product eligibility endpoint returns a correct decision in every combination of (restricted vs not) × (verified vs unverified vs unauthenticated) — verified by a complete truth-table test (4 × 3 = 12 cases plus the non-restricted short-circuit).
- **SC-007**: Every catalog-owned row created at launch has `owner_id` populated and `vendor_id` NULL — verified by a migration-time assertion and a post-seed data audit.
- **SC-008**: Spec 006 search receives a reindex-trigger event within 2 seconds of any product publish/update/archive — verified by an event-latency test.

---

## Assumptions

- Price is owned by spec 007-a (pricing-and-tax-engine). The catalog stores a product-level base price token and nothing else; effective price resolution is always delegated. Catalog listing responses include the price token; spec 007-a produces the customer-visible total.
- Inventory availability is owned by spec 008. The catalog surfaces a boolean `available` flag per product for customer listings; the underlying computation is delegated to spec 008 at read time.
- The restriction enforcement policy `customer.verified-professional` is owned by spec 004 (identity-and-access) and implemented on the `/internal/authorize` endpoint there. Catalog declares which reason codes map to which policy keys; catalog does not implement the policy.
- Verification state transitions (submitted → approved → expired) are owned by spec 020 (verification). Catalog consumes the current state through the identity policy, not directly.
- Media transformations (thumbnails, responsive variants) are produced by the storage abstraction pipeline in spec 003; catalog stores references, not binary data.
- Soft-delete is used for categories, brands, manufacturers, and products; hard-delete is never offered through the public admin surface.
- Taxonomy keys (attribute keys with declared types and display labels) are seeded by admin migration for the launch set; admin self-service management of taxonomy keys is deferred to Phase 1.5 unless a later clarification reopens it.
- PDF generation (tax invoice footer manufacturer block, spec-sheet regeneration) is handled by spec 012 and spec 003; catalog ships the data, not the rendering.
- Locale fallback rule: when an optional field is missing in the requested locale, fall back to the other locale and mark the field with a `fallback_locale` indicator. Mandatory fields (name, description, primary-image alt, restriction rationale when restricted) cannot fall back — publish is blocked until they are present.
- Virus-scan hook is provided by spec 003 storage abstraction; catalog does not ship a scanner.

---

## Dependencies

- **003 · shared-foundations** — storage abstraction (upload, signed URL, virus scan), PDF abstraction, localization scaffolding, audit-log module, error envelope, correlation-id middleware, `packages/shared_contracts` publishing.
- **004 · identity-and-access** — admin authentication + RBAC framework (consumed for `catalog.*` permissions); `customer.verified-professional` eligibility hook on `/internal/authorize` for restriction enforcement.

**Consumed by (forward-looking; informational only)**:

- **006 · search** — indexes products, brands, categories, attributes, and restriction flags; listens to FR-019 domain events for incremental reindex.
- **007-a · pricing-and-tax-engine** — consumes product price token; resolves effective price.
- **008 · inventory** — joins on SKU; supplies `available` flag for customer DTOs.
- **009 · cart / 010 · checkout** — read catalog product detail + call eligibility endpoint (FR-018) for restricted-product gating.
- **012 · tax-and-invoices** — renders manufacturer fields on invoice footers.
- **016 · admin-catalog** — Phase 1C UI consuming the admin DTO + endpoints defined by this spec.
- **022 · reviews-moderation** — links review entities to products; reads brand/category for aggregation.
- **024 · cms** — featured-section composer references product ids.
