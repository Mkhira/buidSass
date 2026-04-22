# Implementation Plan: Catalog (v1)

**Branch**: `phase-1B-specs` | **Date**: 2026-04-22 | **Spec**: [spec.md](./spec.md)

## Summary

Catalog aggregate + publishing workflow + restriction seam + outbox to spec 006. Vertical-slice module at `services/backend_api/Modules/Catalog/`. Closure-table category tree, hybrid attribute storage (fixed columns + JSONB), draft/review/scheduled/published/archived state machine, async media-variant generation via worker queue, content-addressed storage via spec 003. No pricing, no inventory, no search engine calls — each is a separate spec.

## Technical Context

**Language/Version**: C# 12 / .NET 9, PostgreSQL 16.
**Primary Dependencies**:
- `MediatR` v12, `FluentValidation` v11, `EF Core` v9 (ADR-003, ADR-004)
- `SixLabors.ImageSharp` v3 for variant generation (license: SLPL — acceptable in this context per legal review; fallback option `Magick.NET-Q8-AnyCPU` if SLPL becomes an issue)
- `NJsonSchema` v11 for per-category attribute schema validation
- Spec 003 consumables: `IStorageProvider`, `IAuditEventPublisher`, `MessageFormat.NET`, `CorrelationIdMiddleware`, `SaveChangesInterceptor`
- Spec 004 consumables: JWT bearer with admin surface, `[RequirePermission]` filter, role `catalog.editor` / `catalog.publisher`

**Storage**: PostgreSQL — 11 tables (categories, category_closure, category_attribute_schemas, brands, manufacturers, products, product_categories, product_media, product_documents, product_state_transitions, scheduled_publishes) + 1 outbox table (`catalog_outbox`). Media originals + variants land in the spec 003 storage abstraction.

**Testing**: xUnit + FluentAssertions + Testcontainers Postgres; `WebApplicationFactory<Program>` for integration; golden-file tests for DTOs.

**Target Platform**: `services/backend_api/Modules/Catalog/` modular monolith slice.

**Performance Goals**:
- Category page p95 ≤ 250 ms (SC-002)
- Product detail p95 ≤ 200 ms (SC-003)
- Restriction check p95 ≤ 20 ms (SC-006)
- Scheduled publish worker latency ≤ 60 s (SC-004)

**Constraints**:
- Content-addressed media URLs (hash-suffix)
- Slugs immutable post first-publish (FR-008)
- Attributes JSONB validated against per-category JSON-Schema on write
- Outbox pattern for spec-006 events (no direct Meilisearch call)

**Scale/Scope**: 5k–200k products, 500 categories, 500 brands, ≤ 50k media rows at launch.

## Constitution Check

| Principle / ADR | Gate | Status |
|---|---|---|
| P5 Market Configuration | Products carry `market_codes[]` subset of configured markets; publish rejects unknown market | PASS |
| P6 Multi-vendor-ready | `owner_id` + `vendor_id` on product/brand/media; admin list defaults to `vendor_id IS NULL` | PASS |
| P8 Restricted Products | Restriction visible + price visible + add-to-cart disabled with reason; centralized check API | PASS |
| P10 Pricing | Catalog does NOT own prices; FR-006 omits price fields beyond a non-authoritative hint | PASS |
| P12/P26 Search | Outbox events only; no direct search engine calls | PASS |
| P15 Reviews | No review endpoints here; review entity seam owned by future spec | PASS |
| P20 Admin | Admin DTOs + permissions + audit reach surface parity with P20 module list | PASS |
| P22 Fixed Tech | .NET 9, PostgreSQL 16, EF Core 9 | PASS |
| P23 Architecture | Vertical slice under `Modules/Catalog/` | PASS |
| P25 Data & Audit | Every write emits audit with before/after | PASS |
| P27 UX Quality | Error payloads use structured reason codes consumed by UI specs | PASS |
| P29 Spec Output | Goal/roles/rules/flow/state/data/validation/API/edges/acceptance/phase/deps present | PASS |
| ADR-001 Monorepo | `services/backend_api/Modules/Catalog/` | PASS |
| ADR-003 Vertical slice | One folder per HTTP endpoint | PASS |
| ADR-004 EF Core | Code-first migrations | PASS |
| ADR-010 KSA residency | All tables in KSA-region Postgres | PASS |

## Project Structure

```text
services/backend_api/Modules/Catalog/
├── Primitives/
│   ├── CategoryTree.cs                  # closure-table helper
│   ├── ProductStateMachine.cs
│   ├── AttributeSchemaValidator.cs      # NJsonSchema wrapper
│   ├── MediaVariantGenerator.cs         # ImageSharp
│   ├── RestrictionEvaluator.cs
│   ├── RestrictionCache.cs              # 5-second sliding cache
│   ├── ContentAddressedPaths.cs
│   └── Outbox/
│       ├── CatalogOutboxWriter.cs
│       └── CatalogOutboxDispatcherWorker.cs
├── Customer/                            # storefront-facing read slices
│   ├── ListCategories/
│   ├── GetCategoryProducts/
│   ├── GetProductBySlug/
│   └── CheckRestriction/                # serves spec 008/009/010
├── Admin/
│   ├── CreateCategory/
│   ├── UpdateCategory/
│   ├── ReparentCategory/
│   ├── CreateBrand/
│   ├── UpdateBrand/
│   ├── CreateProduct/
│   ├── UpdateProduct/
│   ├── SubmitProductForReview/
│   ├── PublishProduct/
│   ├── SchedulePublish/
│   ├── CancelSchedule/
│   ├── ArchiveProduct/
│   ├── UploadMedia/
│   ├── UpdateMediaAlt/
│   ├── UploadDocument/
│   └── BulkImportProducts/
├── Entities/                            # 11 EF entities
├── Persistence/
│   ├── CatalogDbContext.cs
│   ├── Configurations/
│   └── Migrations/
├── Workers/
│   ├── MediaVariantWorker.cs
│   ├── ScheduledPublishWorker.cs
│   └── CatalogOutboxDispatcherWorker.cs
├── Seeding/
│   ├── CategoryAttributeSchemaSeeder.cs # YAML-backed reference data
│   └── CatalogDevDataSeeder.cs
└── Messages/
    ├── catalog.ar.icu
    └── catalog.en.icu

services/backend_api/tests/Catalog.Tests/
├── Unit/
├── Integration/
└── Contract/
```

**Structure Decision**: Vertical slice per ADR-003. Customer read slices separated from Admin write slices so CORS/auth/rate-limit policies stay distinct. Workers isolated in `Workers/` so scheduling is explicit. Outbox pattern keeps spec 006 coupling async.

## Implementation Phases

| Phase | Scope | Blockers cleared |
|---|---|---|
| A. Primitives | CategoryTree helpers, ProductStateMachine, AttributeSchemaValidator, MediaVariantGenerator, RestrictionEvaluator, Outbox primitives | Foundation |
| B. Persistence | 11 entities + outbox table, EF configs, migration, closure-table trigger or app-level maintenance | All slices |
| C. Reference data seeder | Category attribute schemas from YAML; seed brands/manufacturers for Dev | Editor slices |
| D. Admin write slices | Categories (CRUD + reparent), brands, manufacturers, products (create/update), media upload, documents | US2, US5 |
| E. Workflow slices | Submit/Publish/Schedule/CancelSchedule/Archive + ProductStateMachine enforcement | US2, US4 |
| F. Customer read slices | ListCategories, CategoryProducts, ProductBySlug with DTOs + facet counts | US1 |
| G. Restriction API | CheckRestriction endpoint + 5-second cache | US3 |
| H. Workers | ScheduledPublishWorker (60 s tick), MediaVariantWorker (dequeue + ImageSharp), OutboxDispatcher | SC-004, SC-010 |
| I. Bulk import | JSON-Lines streaming endpoint, per-row idempotency, per-row errors | SC-009 |
| J. AR/EN editorial | catalog.{ar,en}.icu bundles complete; reason-code parity test | P4 |
| K. Integration + DoD | Full Testcontainers run, fingerprint, DoD walkthrough | PR gate |

## Complexity Tracking

| Design choice | Why Needed | Rejected alternative |
|---|---|---|
| Closure table for categories | Arbitrary depth, stable re-parent, fast ancestor queries for breadcrumbs + facet rollup | Nested-set — awful under frequent re-parenting; adjacency-list — O(depth) ancestor queries |
| Hybrid columns + JSONB attributes | Fixed columns stay indexable; JSONB absorbs long-tail without schema churn | Pure EAV — query complexity; pure JSONB — no typed indexes on SKU/barcode/brand |
| Outbox table → async Meilisearch | Principle 12 decoupling + failure isolation | Direct call — Meilisearch outage blocks publish |
| 4 fixed variant sizes, async | Predictable CDN costs + predictable UI | On-demand per request — CDN thrashing |
| Immutable slug post first-publish | SEO + permalink stability | Mutable slug — URL rot + search re-indexing storms |
| Content-addressed media paths | Safe CDN caching + dedup | Versioned paths + cache-bust headers — operationally fragile |
| Per-category attribute JSON-Schema | Enforces attribute hygiene without dynamic migration | Rigid per-category columns — migration storm at launch |
