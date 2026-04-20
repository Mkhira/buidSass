# Tasks: Catalog (005)

**Feature**: `specs/phase-1B/005-catalog/spec.md`
**Plan**: `./plan.md` · **Data model**: `./data-model.md` · **Contracts**: `./contracts/` · **Research**: `./research.md`

Tests are included because spec 005 has testable acceptance criteria per user story (Principle 28 + SC-006 truth-table + SC-008 latency test explicitly required).

All backend paths are under `services/backend_api/`. Shared contracts land in `packages/shared_contracts/catalog/`.

---

## Phase 1 — Setup

- [ ] T001 Create feature slice skeleton at `services/backend_api/Features/Catalog/` with subfolders `Categories/`, `Brands/`, `Manufacturers/`, `Products/`, `Variants/`, `Media/`, `Documents/`, `Attributes/`, `Eligibility/`, `Taxonomy/`, `CustomerListing/`, `Events/`, `Persistence/`, `Shared/`
- [ ] T002 Create test project skeletons `Tests/Catalog.Unit`, `Tests/Catalog.Integration`, `Tests/Catalog.Contract` and wire them into `buidsass.sln`
- [ ] T003 [P] Add NuGet references in `Features/Catalog/Catalog.csproj`: MediatR, FluentValidation, EFCore.NamingConventions, Npgsql.EntityFrameworkCore.PostgreSQL, SixLabors.ImageSharp, YamlDotNet, Serilog, OpenTelemetry
- [ ] T004 [P] Add Testcontainers.PostgreSql + Verify.Xunit + FsCheck to `Tests/Catalog.Integration/Catalog.Integration.csproj` and `Tests/Catalog.Unit/Catalog.Unit.csproj`
- [ ] T005 [P] Register `CatalogModule` DI in `Api/Program.cs` (AddCatalog() extension method in `Features/Catalog/ServiceCollectionExtensions.cs`)

## Phase 2 — Foundational (blocks all user stories)

- [ ] T006 Create `Features/Catalog/Persistence/CatalogDbContext.cs` with `DbSet<>` for every entity in `data-model.md`, snake_case convention, global soft-delete query filter, `xmin` row-version mapping via `IsRowVersion()`
- [ ] T007 Create `Features/Catalog/Persistence/Migrations/0001_InitialCatalogSchema.cs` — all 12 tables + partial unique indexes (SKU non-archived, slug per parent) + CHECKs (depth ≤ 6, restriction rationale parity, attribute typed-column xor, media MIME/size)
- [ ] T008 [P] Create `Features/Catalog/Persistence/Seeds/taxonomy_keys.yaml` with 12 launch keys per research.md §15 and `Features/Catalog/Persistence/Migrations/0002_SeedTaxonomyKeys.cs` that loads the YAML
- [ ] T009 [P] Create `Features/Catalog/Persistence/Migrations/0003_SeedRestrictionReasonCodes.cs` seeding `dental-professional`, `controlled-substance`, `institution-only` with policy key + AR + EN labels
- [ ] T010 [P] Create `Features/Catalog/Shared/Ports/IObjectStorage.cs`, `IVirusScanner.cs`, `IAuditEventPublisher.cs`, `IVerifiedProfessionalPolicy.cs`, `IVariantAvailabilityReader.cs` (consumed from spec 003 + spec 004; default impls register in `AddCatalog`)
- [ ] T011 [P] Create `Features/Catalog/Shared/Ports/StaticTrueAvailabilityReader.cs` default registration until spec 008 replaces it
- [ ] T012 [P] Create `Features/Catalog/Shared/Errors/CatalogErrorCodes.cs` (constants like `catalog.variant.sku_conflict`, `catalog.category.depth_exceeded`, `catalog.media.too_large`, `catalog.media.unsupported_mime`, `catalog.publish.parity_missing`, `catalog.concurrency.conflict`)
- [ ] T013 [P] Create `Features/Catalog/Shared/LocaleResolver.cs` that reads `Accept-Language`, chooses `ar|en`, and exposes `TryPickLocale<T>(arField, enField, out fallbackLocale)` for DTO projections
- [ ] T014 [P] Create `Features/Catalog/Events/CatalogDomainEvents.cs` with MediatR `INotification` records: `ProductCreated`, `ProductUpdated`, `ProductPublished`, `ProductArchived`, `ProductVariantChanged`, `ProductMediaChanged`, `CategoryTreeChanged`, `BrandChanged`
- [ ] T015 [P] Create `Features/Catalog/Events/AuditEventFactory.cs` — centralized `MakeAudit(actor, targetType, actionKey, before, after, correlationId)` producing envelopes consumed by spec 003 audit sink
- [ ] T016 Create `Features/Catalog/Persistence/CatalogAuditInterceptor.cs` — `SaveChangesInterceptor` that captures before/after snapshots for every tracked catalog entity and dispatches audit events on successful commit
- [ ] T017 [P] Contract publishing: add `contracts/catalog.openapi.yaml` and `contracts/events.md` to the `packages/shared_contracts/catalog/` pipeline via `scripts/shared-contracts/generate.sh catalog`; commit placeholder generated output

## Phase 3 — User Story 1 (P1): Customer browses catalog

**Story goal**: unauthenticated + authenticated customers can browse the tree, list products per category, and open product detail with price visible, restriction flag rendered, and availability computed.

**Independent test**: seed 1 brand + 1 active category + 1 active non-restricted product + 1 active restricted product (each with 1 variant, AR + EN copy). Listing + detail endpoints return expected payloads in both locales with restriction and price-token fields populated.

- [ ] T018 [US1] Write integration test `Tests/Catalog.Integration/CustomerListing_CategoryListingTests.cs` covering AS-1..AS-5 from spec §User Story 1 (listing includes price token + primary image + availability + restriction; inactive category hidden; AR request returns AR fields with fallback flag; empty category returns `total=0, items=[]`)
- [ ] T019 [US1] Write integration test `Tests/Catalog.Integration/CustomerListing_ProductDetailTests.cs` for restricted + non-restricted product detail in both locales, including `fallbackLocales` flag when an optional field is only present in one locale
- [ ] T020 [US1] Write contract snapshot test `Tests/Catalog.Contract/CustomerDtoSnapshotTests.cs` using Verify, locking the `CustomerProductDetail` and `CustomerProductCard` shapes
- [ ] T021 [P] [US1] Implement `Features/Catalog/CustomerListing/GetCustomerCategoryListingQuery.cs` + handler — reads active categories where `parent_id = @parentId` ordered by `position`, returns `CustomerCategoryListing`
- [ ] T022 [P] [US1] Implement `Features/Catalog/CustomerListing/GetCustomerProductListingQuery.cs` + handler — joins products → product_categories → brands, filters by active+published, applies sort + page + clamp at pageSize=48, returns `CustomerProductListing`
- [ ] T023 [US1] Implement `Features/Catalog/CustomerListing/GetCustomerProductDetailQuery.cs` + handler — returns `CustomerProductDetail`, pulls variants+axes+media+documents+attributes, uses `LocaleResolver` to pick AR or EN
- [ ] T024 [P] [US1] Implement `Features/Catalog/Shared/Projections/AvailabilityProjector.cs` — batches `IVariantAvailabilityReader.ReadAsync` for listing/detail responses
- [ ] T025 [P] [US1] Implement `Features/Catalog/Shared/Projections/PriceTokenFactory.cs` — emits `PriceToken { productId, variantId, marketCode }` (spec 007-a resolves)
- [ ] T026 [US1] Wire controllers `Api/Controllers/CustomerCatalogController.cs` with routes `GET /categories`, `GET /products`, `GET /products/{id}` per `contracts/catalog.openapi.yaml`
- [ ] T027 [P] [US1] Add k6 script `tests/perf/catalog/listing.k6.js` asserting p95 ≤ 1.5 s at 24 items per page (SC-001)

**Checkpoint**: T018–T020 go red → T021–T026 make them green → T027 proves the perf budget. After checkpoint, Story 1 is independently deliverable (MVP).

## Phase 4 — User Story 6 (P1): Restriction eligibility (advanced ahead of US2 because US2 depends on restriction metadata being authorable)

**Story goal**: catalog exposes the `/products/{id}/eligibility` endpoint; decision flow delegates to spec 004 policy for `dental-professional`; non-restricted short-circuits to `allowed=true`.

**Independent test**: seed 1 restricted + 1 non-restricted product; call eligibility with (verified, unverified, unauth) customers and with the non-restricted product → verify the 3 + short-circuit decisions and localized reason copy.

- [ ] T028 [US6] Write integration test `Tests/Catalog.Integration/EligibilityTruthTableTests.cs` — complete 4 × 3 matrix + non-restricted short-circuit (SC-006)
- [ ] T029 [P] [US6] Implement `Features/Catalog/Eligibility/GetProductEligibilityQuery.cs` + handler — reads product, maps `restriction_reason_code → policy_key` from `restriction_reason_codes`, calls `IVerifiedProfessionalPolicy.AuthorizeAsync`
- [ ] T030 [P] [US6] Implement `Features/Catalog/Eligibility/EligibilityCopyCatalog.cs` returning localized reason copy for `requires-auth`, `requires-verification`, `not-eligible-for-market`
- [ ] T031 [US6] Wire `Api/Controllers/EligibilityController.cs` route `GET /products/{productId}/eligibility`

**Checkpoint**: Eligibility endpoint ready; unblocks US2 publish flow which tests restricted-product authoring.

## Phase 5 — User Story 3 (P1): Admin manages category tree

**Story goal**: admin creates nested tree, reorders siblings, moves under different parents (cycle + depth-6 enforced), deactivates subtrees.

**Independent test**: seed empty tree → build 3 levels → reorder two siblings → deactivate a subtree → confirm customer listing hides deactivated subtree while admin listing preserves it.

- [ ] T032 [US3] Write integration test `Tests/Catalog.Integration/CategoryTreeTests.cs` covering AS-1..AS-4 from spec §User Story 3
- [ ] T033 [US3] Write property-based test `Tests/Catalog.Unit/CategoryMaterializedPathInvariantsTests.cs` via FsCheck — tree never cyclic, depth ≤ 6 after any sequence of create/move
- [ ] T034 [P] [US3] Implement `Features/Catalog/Categories/CreateCategoryCommand.cs` + handler — computes `path` + `depth`, enforces depth ≤ 6, emits `CategoryTreeChanged` and audit `catalog.category.created`
- [ ] T035 [P] [US3] Implement `Features/Catalog/Categories/UpdateCategoryCommand.cs` + handler — rename, slug change, active flag; enforces row-version
- [ ] T036 [US3] Implement `Features/Catalog/Categories/MoveCategoryCommand.cs` + handler — cycle check via `path LIKE 'self.%'`, depth recomputation, atomic sibling reorder in one transaction
- [ ] T037 [P] [US3] Implement `Features/Catalog/Categories/DeactivateCategoryCommand.cs` + handler — marks node + descendants inactive via path prefix update
- [ ] T038 [P] [US3] Implement `Features/Catalog/Categories/ReactivateCategoryCommand.cs` + handler
- [ ] T039 [US3] Wire admin controller routes in `Api/Controllers/AdminCategoriesController.cs`: `POST /admin/catalog/categories`, `PUT /admin/catalog/categories/{id}`, `POST /admin/catalog/categories/{id}/move`

**Checkpoint**: Category tree authoring complete; US1 listing can now render a real tree.

## Phase 6 — User Story 4 (P2): Admin authors brands and manufacturers

**Story goal**: brand + manufacturer CRUD with logo upload, slug uniqueness, delete blocked when referenced.

**Independent test**: create brand w/ logo → create manufacturer → attach to a product → confirm both appear in admin and customer DTOs.

- [ ] T040 [US4] Write integration test `Tests/Catalog.Integration/BrandLifecycleTests.cs` covering create, slug conflict, delete-blocked-when-referenced fallback to deactivate
- [ ] T041 [P] [US4] Implement `Features/Catalog/Brands/CreateBrandCommand.cs` + handler
- [ ] T042 [P] [US4] Implement `Features/Catalog/Brands/UpdateBrandCommand.cs` + handler
- [ ] T043 [P] [US4] Implement `Features/Catalog/Brands/DeactivateBrandCommand.cs` + handler (delete → soft deactivate when any product references it)
- [ ] T044 [P] [US4] Implement `Features/Catalog/Brands/UploadBrandLogoCommand.cs` + handler — routes through `IObjectStorage` + `IVirusScanner`, writes a `product_media` row with `brand_id` set
- [ ] T045 [P] [US4] Implement `Features/Catalog/Manufacturers/{Create,Update,Deactivate}ManufacturerCommand.cs` + handlers (3 files)
- [ ] T046 [US4] Wire `Api/Controllers/AdminBrandsController.cs` and `Api/Controllers/AdminManufacturersController.cs`

## Phase 7 — User Story 2 (P1): Admin creates product with variants

**Story goal**: admin creates draft product → adds ≥1 variant → uploads primary media → publishes. Parity gate enforced at publish.

**Independent test**: as a `catalog-editor` admin, create a product + variant + primary image + attribute, publish, then fetch customer detail and see the published product.

- [ ] T047 [US2] Write integration test `Tests/Catalog.Integration/ProductAuthoringLifecycleTests.cs` covering AS-1..AS-5 from spec §User Story 2 plus FR-015 parity rejection
- [ ] T048 [US2] Write integration test `Tests/Catalog.Integration/VariantSkuReuseTests.cs` covering Clarification Q3 — archive variant, create a new variant with same SKU → succeeds and emits `catalog.variant.sku.reused`
- [ ] T049 [P] [US2] Implement `Features/Catalog/Products/CreateProductCommand.cs` + handler + FluentValidation — rejects free-form attribute keys (FR-010), enforces restriction CHECK
- [ ] T050 [P] [US2] Implement `Features/Catalog/Products/UpdateProductCommand.cs` + handler — row-version check, emits `ProductUpdated` + `catalog.product.restriction.changed` when restriction flips
- [ ] T051 [P] [US2] Implement `Features/Catalog/Products/PublishProductCommand.cs` + handler — FR-015 parity gate (AR + EN name + description + ≥1 primary image with AR + EN alt text + ≥1 active variant) → 422 with `catalog.publish.parity_missing` listing missing fields
- [ ] T052 [P] [US2] Implement `Features/Catalog/Products/ArchiveProductCommand.cs` + handler
- [ ] T053 [P] [US2] Implement `Features/Catalog/Variants/CreateVariantCommand.cs` + handler — validates SKU regex, inserts variant + axes, detects SKU reuse against archived rows and emits `catalog.variant.sku.reused`
- [ ] T054 [P] [US2] Implement `Features/Catalog/Variants/UpdateVariantCommand.cs` + handler — status transitions (`active ↔ inactive`, `* → archived`), FR-013 SKU freeze when referenced by inventory/orders (check via `IVariantAvailabilityReader` sentinel + future spec 011 reference probe)
- [ ] T055 [P] [US2] Implement `Features/Catalog/Attributes/UpsertProductAttributesCommand.cs` + handler — validates each key against `taxonomy_keys`
- [ ] T056 [US2] Wire `Api/Controllers/AdminProductsController.cs` with `POST/GET/PUT /admin/catalog/products[/{id}]`, `POST .../publish`, `POST .../archive`, `POST .../variants`, `PUT .../variants/{id}`
- [ ] T057 [P] [US2] Implement `Features/Catalog/Products/AdminProductDetailProjection.cs` — single EF projection populating `AdminProductDetail` including variants + media + documents + attributes + row_version
- [ ] T058 [P] [US2] Add contract snapshot test `Tests/Catalog.Contract/AdminDtoSnapshotTests.cs` locking `AdminProductDetail`, `AdminVariant`

**Checkpoint**: Admin can author a restricted product end-to-end; combined with US6 the eligibility flow is fully verified.

## Phase 8 — User Story 5 (P2): Rich product content (media, documents, attributes)

**Story goal**: admin uploads multiple images in order, documents (PDF/PNG spec sheets, IFU, certifications), external video links, and typed attributes.

**Independent test**: create a product with 3 reordered images + 2 documents + 5 attributes → retrieve via admin DTO → confirm order, alt text, document links.

- [ ] T059 [US5] Write integration test `Tests/Catalog.Integration/MediaLifecycleTests.cs` — upload, reorder, set-primary, delete, reject > 8 MB, reject unsupported MIME (PR per FR-011)
- [ ] T060 [US5] Write integration test `Tests/Catalog.Integration/DocumentLifecycleTests.cs` — upload PDF (≤ 20 MB), upload PNG document, reject 30 MB (spec §5 AS-2 variant), add external video link
- [ ] T061 [P] [US5] Implement `Features/Catalog/Media/UploadProductMediaCommand.cs` + handler — pre-parse via ImageSharp, reject > 8 MB or unsupported MIME with `catalog.media.too_large` / `catalog.media.unsupported_mime`, upload → scan → insert only on clean verdict
- [ ] T062 [P] [US5] Implement `Features/Catalog/Media/ReorderProductMediaCommand.cs` + handler — atomic position rewrite
- [ ] T063 [P] [US5] Implement `Features/Catalog/Media/SetPrimaryMediaCommand.cs` + handler — partial unique index enforces at-most-one primary
- [ ] T064 [P] [US5] Implement `Features/Catalog/Media/DeleteProductMediaCommand.cs` + handler — soft delete
- [ ] T065 [P] [US5] Implement `Features/Catalog/Documents/UploadProductDocumentCommand.cs` + handler — PDF + PNG accepted, ≤ 20 MB, or external URL for `external-video-link`
- [ ] T066 [P] [US5] Implement `Features/Catalog/Documents/DeleteProductDocumentCommand.cs` + handler
- [ ] T067 [P] [US5] Implement `Features/Catalog/Taxonomy/GetTaxonomyKeysQuery.cs` + handler — read-only list per Clarification Q5
- [ ] T068 [US5] Wire `Api/Controllers/AdminMediaController.cs`, `AdminDocumentsController.cs`, `AdminTaxonomyController.cs` with routes per OpenAPI

## Phase 9 — User Story 7 (P3): Multi-vendor-ready ownership

**Story goal**: every catalog-owned row carries `owner_id` populated and `vendor_id` NULL at launch; schema supports Phase 2 population without migration.

**Independent test**: migration-time assertion + post-seed data audit confirms `vendor_id IS NULL` everywhere (SC-007).

- [ ] T069 [US7] Write migration-time assertion in `Features/Catalog/Persistence/Migrations/0004_AssertVendorIdNullAtLaunch.cs` — throws if any catalog-owned row has non-null `vendor_id` at launch
- [ ] T070 [P] [US7] Write integration test `Tests/Catalog.Integration/MultiVendorReadinessTests.cs` — synthetic row with `vendor_id` round-trips cleanly via admin DTO; default creates leave it NULL
- [ ] T071 [P] [US7] Add post-seed data-audit script `scripts/catalog/assert-vendor-id-null.sh` invoked in CI (SC-007)

## Phase 10 — Polish & cross-cutting

- [ ] T072 [P] Write audit coverage test `Tests/Catalog.Integration/CatalogAuditCoverageTests.cs` — enumerates every action key in `contracts/events.md` and asserts each produces an audit event (FR-026 + DoD item)
- [ ] T073 [P] Write reindex-latency test `Tests/Catalog.Integration/ReindexLatencyTests.cs` — asserts every reindex event fires within 2 seconds of the corresponding catalog mutation (SC-008)
- [ ] T074 [P] Editorial sign-off checklist `specs/phase-1B/005-catalog/checklists/editorial-signoff.md` — AR + EN copy review template for seeded sample products
- [ ] T075 [P] Add OpenAPI lint + diff CI step invoking `scripts/shared-contracts/verify.sh catalog` on every PR touching `contracts/`
- [ ] T076 Add PR-body fingerprint append step to `docs/dod.md` catalog section (mirrors spec 004 DoD)
- [ ] T077 [P] Update `docs/audit/phase-1B.md` (or create if absent) with spec 005 closeout row
- [ ] T078 [P] Verify `scripts/compute-fingerprint.sh` includes `specs/phase-1B/005-catalog/**` in the manifest glob

---

## Dependencies & execution order

- Phase 1 → Phase 2 → (Phase 3 ∥ Phase 4 ∥ Phase 5) → Phase 6 → Phase 7 → Phase 8 → Phase 9 → Phase 10
- Within each phase, tasks marked `[P]` may run in parallel (different files, no data dependency on sibling tasks).
- Phase 7 (US2) depends on Phase 5 (US3) for categories + Phase 6 (US4) for brands to exist; and on Phase 4 (US6) to wire eligibility for restricted products it authors.
- Phase 8 (US5) depends on Phase 7 for products to exist.

## Parallel execution examples

```
# After Phase 2 complete, Phase 3 author can run T021 ∥ T022 ∥ T024 ∥ T025 ∥ T027 in parallel.
# After Phase 2 complete, Phase 4 author can start independently — only needs products table seed (Phase 2).
# Phase 7 T049..T055 and T057..T058 are all in different files — fully parallel.
```

## Implementation strategy

- **MVP = Phase 1 + Phase 2 + Phase 3 (US1) + Phase 4 (US6) + Phase 5 (US3) + minimum slice of Phase 6 (seed one brand) + minimum slice of Phase 7 (create/publish one variant-less product) + Phase 9 (US7 schema).**
- Phase 8 (rich content) and the rest of Phase 6/7 roll in incrementally; each story is releasable because its acceptance test is self-contained.
- Phase 10 polish closes DoD (audit coverage, reindex latency, fingerprint, editorial sign-off).

---

## Amendment A1 — Environments, Docker, Seeding

**Source**: [`docs/missing-env-docker-plan.md`](../../../docs/missing-env-docker-plan.md)

**Hard dependency**: PR A1 (scaffolding) must merge before this spec's implementation PR opens. PR 004 must merge before this PR (seed dependency: `DependsOn=["identity-v1"]`).

### New tasks

- [ ] T101 [US1] Implement `services/backend_api/Features/Seeding/Seeders/_005_CatalogSeeder.cs` (`ISeeder`, `Name="catalog-v1"`, `Version=1`, `DependsOn=["identity-v1"]`). Uses `DatasetSize` option (small=60 products/120 variants, medium=200/400, large=800/1600). Seeds 8 bilingual categories (depth ≤ 3), 5 brands, products + variants with 15% restricted, 5% soft-deleted, AR+EN via curated phrase bank at `Features/Seeding/Datasets/PhraseBank.catalog.{ar,en}.json`. Publishes `CatalogVariantCreated` MediatR notifications after each variant insert so `SearchBridge` reindexes.
- [ ] T102 [US1] Integration test `Tests/Catalog.Integration/Seeding/CatalogSeederTests.cs`: all three dataset sizes populate expected row counts; AR + EN fields non-null on every record; restricted / soft-delete ratios within ±2%.
- [ ] T103 [P] Add bilingual phrase bank JSON files with ≥ 100 curated AR/EN product name fragments + 20 category names. Editorial-grade Arabic (Principle 4).
