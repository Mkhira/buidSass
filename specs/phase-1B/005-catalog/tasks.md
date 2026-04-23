---
description: "Dependency-ordered tasks for spec 005 — catalog"
---

# Tasks: Catalog v1

**Input**: spec.md (27 FRs, 10 SCs, 6 user stories), plan.md, research.md, data-model.md (12 tables, 2 state machines), contracts/catalog-contract.md.

## Phase 1: Setup

- [X] T001 Create module directory tree at `services/backend_api/Modules/Catalog/{Primitives,Primitives/Outbox,Customer,Admin,Entities,Persistence/{Configurations,Migrations},Workers,Seeding,AttributeSchemas,Messages}` and tests at `tests/Catalog.Tests/{Unit,Integration,Contract}`
- [X] T002 Register `AddCatalogModule` in `services/backend_api/Modules/Catalog/CatalogModule.cs` and wire into `Program.cs`
- [X] T003 [P] Add NuGet refs to `services/backend_api/backend_api.csproj`: `SixLabors.ImageSharp 3.*`, `NJsonSchema 11.*`, `YamlDotNet 16.*`

## Phase 2: Foundational

### Primitives
- [X] T004 [P] `CategoryTreeService` (closure-table insert/reparent/detach) in `Modules/Catalog/Primitives/CategoryTreeService.cs`
- [X] T005 [P] `ProductStateMachine` in `Modules/Catalog/Primitives/ProductStateMachine.cs`
- [X] T006 [P] `AttributeSchemaValidator` (NJsonSchema) in `Modules/Catalog/Primitives/AttributeSchemaValidator.cs`
- [X] T007 [P] `IImageVariantGenerator` + `ImageSharpVariantGenerator` in `Modules/Catalog/Primitives/ImageVariantGenerator.cs`
- [X] T008 [P] `RestrictionEvaluator` + `RestrictionCache` (5 s TTL, in-proc) in `Modules/Catalog/Primitives/Restriction/*.cs`
- [X] T009 [P] `ContentAddressedPaths` helper in `Modules/Catalog/Primitives/ContentAddressedPaths.cs`
- [X] T010 [P] `CatalogOutboxWriter` (transactional write alongside EF save) in `Modules/Catalog/Primitives/Outbox/CatalogOutboxWriter.cs`

### Persistence
- [X] T011 Create 12 entity classes in `Modules/Catalog/Entities/*.cs` per data-model.md
- [X] T012 Create 12 `IEntityTypeConfiguration<T>` in `Modules/Catalog/Persistence/Configurations/*.cs`
- [X] T013 `CatalogDbContext` with soft-delete filters + shared `SaveChangesInterceptor` in `Modules/Catalog/Persistence/CatalogDbContext.cs`
- [X] T014 EF migration `Catalog_Initial` at `Modules/Catalog/Persistence/Migrations/`; apply against A1 Postgres
- [X] T015 `CategoryAttributeSchemaSeeder` reading YAML from `Modules/Catalog/AttributeSchemas/*.yaml` in `Modules/Catalog/Seeding/CategoryAttributeSchemaSeeder.cs`
- [X] T016 `CatalogDevDataSeeder` (Dev-only brands + manufacturers + 20 sample products) in `Modules/Catalog/Seeding/CatalogDevDataSeeder.cs`
- [X] T017 State-machine unit tests at `tests/Catalog.Tests/Unit/StateMachines/ProductStateMachineTests.cs`
- [X] T018 Closure-table invariants property tests at `tests/Catalog.Tests/Unit/CategoryTreeInvariantsTests.cs`
- [X] T019 `CatalogTestFactory` + shared builders at `tests/Catalog.Tests/Infrastructure/*.cs`

## Phase 3: US1 — Browse catalog (P1) 🎯 MVP

- [X] T020 [P] [US1] Contract test `ListCategories_ReturnsActiveTreeForMarket` at `tests/Catalog.Tests/Contract/Customer/ListCategoriesContractTests.cs`
- [X] T021 [P] [US1] Contract test `GetCategoryProducts_ReturnsPublishedOnly` in `tests/Catalog.Tests/Contract/Customer/CategoryProductsContractTests.cs`
- [X] T022 [P] [US1] Contract test `GetProductBySlug_NonPublished_Returns404` in `tests/Catalog.Tests/Contract/Customer/ProductBySlugContractTests.cs`
- [X] T023 [P] [US1] Contract test `GetProductBySlug_RestrictedProduct_IncludesRestrictionBadge` in same file
- [X] T024 [P] [US1] Integration test `LocaleFallback_MissingEn_FallsBackToArWithHeader` at `tests/Catalog.Tests/Integration/LocaleFallbackTests.cs`
- [X] T025 [US1] Implement `Customer/ListCategories/{Request,Handler,Endpoint}.cs`
- [X] T026 [US1] Implement `Customer/GetCategoryProducts/*.cs` with facet counts (brand, price buckets, restriction)
- [X] T027 [US1] Implement `Customer/GetProductBySlug/*.cs` with localized DTO
- [X] T028 [US1] Populate `Messages/catalog.{ar,en}.icu` for US1 reason codes

## Phase 4: US2 — Catalog editor maintains catalog (P1)

- [X] T029 [P] [US2] Contract tests for category CRUD + reparent at `tests/Catalog.Tests/Contract/Admin/CategoriesContractTests.cs`
- [X] T030 [P] [US2] Contract tests for brand CRUD at `tests/Catalog.Tests/Contract/Admin/BrandsContractTests.cs`
- [X] T031 [P] [US2] Contract tests for product create/update/submit/publish at `tests/Catalog.Tests/Contract/Admin/ProductWorkflowContractTests.cs`
- [X] T032 [P] [US2] Contract test for media upload + variant status at `tests/Catalog.Tests/Contract/Admin/MediaContractTests.cs`
- [X] T033 [P] [US2] Integration test `Publish_EmitsOutboxRowForSearch` at `tests/Catalog.Tests/Integration/OutboxEmissionTests.cs`
- [X] T034 [US2] Implement `Admin/CreateCategory`, `UpdateCategory`, `ReparentCategory`, `DeleteCategory` slices
- [X] T035 [US2] Implement `Admin/CreateBrand`, `UpdateBrand` slices (+ manufacturers mirror)
- [X] T036 [US2] Implement `Admin/CreateProduct`, `UpdateProduct` slices (schema validation via AttributeSchemaValidator)
- [X] T037 [US2] Implement `Admin/SubmitProductForReview`, `PublishProduct`, `ArchiveProduct` slices (state machine)
- [X] T038 [US2] Implement `Admin/UploadMedia`, `UpdateMediaAlt`, `DeleteMedia` slices (queues variant job)
- [X] T039 [US2] Implement `Admin/UploadDocument` slice
- [X] T040 [US2] `MediaVariantWorker` background service in `Modules/Catalog/Workers/MediaVariantWorker.cs`
- [X] T041 [US2] `CatalogOutboxDispatcherWorker` polls every 2 s in `Modules/Catalog/Workers/CatalogOutboxDispatcherWorker.cs` (dispatches to `ICatalogEventSubscriber` — stub implementation until spec 006)

## Phase 5: US3 — Restriction gate (P1)

- [X] T042 [P] [US3] Contract test `CheckRestriction_RestrictedUnverified_ReturnsAllowedFalse` at `tests/Catalog.Tests/Contract/Restrictions/CheckRestrictionContractTests.cs`
- [X] T043 [P] [US3] Contract test `CheckRestriction_MarketScoped_UnrestrictedInOtherMarket` in same file
- [X] T044 [P] [US3] Integration test `RestrictionCache_HitRate_AtLeast95Percent` (SC-006) at `tests/Catalog.Tests/Integration/RestrictionCacheTests.cs`
- [X] T045 [US3] Implement `Customer/CheckRestriction/{Request,Handler,Endpoint}.cs`
- [X] T046 [US3] Wire cache invalidation on product publish/status-change events from `ProductStateMachine`

## Phase 6: US4 — Scheduled publish (P2)

- [X] T047 [P] [US4] Contract test `SchedulePublish_FutureTime_TransitionsToScheduled` at `tests/Catalog.Tests/Contract/Admin/ScheduleContractTests.cs`
- [X] T048 [P] [US4] Contract test `CancelSchedule_ReturnsToInReview` in same file
- [X] T049 [P] [US4] Integration test `ScheduledPublishWorker_PromotesWithin60s` (SC-004) at `tests/Catalog.Tests/Integration/ScheduledPublishTests.cs`
- [X] T050 [US4] Implement `Admin/SchedulePublish`, `Admin/CancelSchedule` slices
- [X] T051 [US4] `ScheduledPublishWorker` (tick every 30 s, claim due rows, advance state) in `Modules/Catalog/Workers/ScheduledPublishWorker.cs`

## Phase 7: US5 — Brand discipline (P2)

- [X] T052 [P] [US5] Contract test `CreateProduct_UnknownBrand_Returns400` at `tests/Catalog.Tests/Contract/Admin/BrandDisciplineContractTests.cs`
- [X] T053 [US5] Validator guard on `CreateProduct`/`UpdateProduct` handlers (already part of T036)

## Phase 8: US6 — Multi-vendor-ready (P2)

- [X] T054 [P] [US6] Integration test `AdminList_DefaultsToVendorNull` at `tests/Catalog.Tests/Integration/VendorScopingTests.cs`
- [X] T055 [US6] Verify all admin list handlers apply `vendor_id IS NULL` default filter and expose optional `?vendorId=` (reserved, no-op at launch)

## Phase 9: Bulk import + Polish

- [X] T056 [P] Bulk import contract test `BulkImport_MixedValidity_ReportsPerRow` at `tests/Catalog.Tests/Contract/Admin/BulkImportContractTests.cs`
- [X] T057 Implement `Admin/BulkImportProducts/*.cs` with streaming JSON-Lines request + response
- [X] T058 [P] OpenAPI regeneration + contract diff check green (Guardrail #2)
- [X] T059 [P] AR editorial pass on `catalog.ar.icu`; add `needs-ar-editorial-review` label on PR if any key lacks review sign-off
- [X] T060 [P] Golden-file DTO tests at `tests/Catalog.Tests/Contract/Golden/*.json`
- [X] T061 Fingerprint + DoD walk-through; attach audit-row spot check for publish flow

**Totals**: 61 tasks across 9 phases. MVP = Phases 1 + 2 + 3 + 4 + 5.
