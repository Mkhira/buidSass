# Implementation Plan: Catalog

**Branch**: `phase_1B_creating_specs` (spec-creation branch; implementation branch will be spun off per PR) | **Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1B/005-catalog/spec.md`

## Summary

Deliver the catalog module for the dental commerce platform in Phase 1B: category tree, brands, manufacturers, products with 1..N variants, structured typed attributes, ordered media and documents, restriction metadata, and the eligibility endpoint that downstream cart/checkout consume. Authoring lives on a tightly-scoped admin surface gated by the RBAC framework from spec 004 (`catalog.read`, `catalog.write`, `catalog.publish`); the customer surface ships a read-only listing + detail + eligibility endpoint. Price is delegated to spec 007-a (a `price_token` token is returned by catalog, resolved downstream). Inventory is delegated to spec 008 (the `available` boolean is computed at read time by joining spec 008). Domain events fire on every product create/update/publish/archive so spec 006 search reindexes incrementally.

**Technical approach**: .NET 9 vertical-slice + MediatR (ADR-003) living at `services/backend_api/Features/Catalog/`, EF Core 9 code-first migrations (ADR-004) for all tables including the materialized-path column on categories, soft-delete query filters, `SaveChangesInterceptor` audit hooks reusing the spec-003 audit-log module, and `xmin` row-version for optimistic concurrency. Variants are modeled as child rows of `products` with their own SKU and `xmin`. Partial unique index enforces SKU uniqueness across non-archived variants only. Taxonomy keys ship as migration-seeded reference data; admin UI exposes them read-only. Media and document uploads route through the spec-003 storage abstraction (`IObjectStorage`, `IVirusScanner`); catalog stores only storage refs + verdicts. Contracts published via `packages/shared_contracts` pipeline. Eligibility endpoint delegates to the spec-004 `/internal/authorize` call with policy key `customer.verified-professional` for the `dental-professional` reason code.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend); downstream consumers are TypeScript 5.x (Next.js admin, spec 016) and Dart 3.x (Flutter storefront, spec 014) — those are out of scope for this spec
**Primary Dependencies**: ASP.NET Core 9, MediatR, FluentValidation, EF Core 9 (Npgsql), `EFCore.NamingConventions` (snake_case), `HybridModelBinding` not used (explicit DTOs only), Serilog + OpenTelemetry for structured logs and correlation id, ImageSharp for image MIME/size introspection pre-upload validation, `System.Text.Json` for payload, YamlDotNet for taxonomy-key migration seeds
**Storage**: PostgreSQL 16 single instance in Azure Saudi Arabia Central (ADR-010), schema `catalog` inside the shared database; audit events appended to `audit_events` (owned by spec 003); object storage for media/documents via spec 003's `IObjectStorage` (Azure Blob Storage container per environment); `market_code` column stamped on every tenant-owned table per ADR-010
**Testing**: xUnit + FluentAssertions; WebApplicationFactory-based integration tests; Testcontainers for throwaway Postgres + in-memory object-storage fake; property-based testing via FsCheck for the category tree invariants (acyclic, depth ≤ 6, reorder atomicity) and the eligibility truth table (FR-018); snapshot tests (Verify) for the customer vs admin DTOs
**Target Platform**: Linux container (Azure App Service / AKS) in Azure Saudi Arabia Central; .NET 9 runtime; HTTPS terminated at ingress
**Project Type**: web-service (backend API); Phase 1C consumers will be the Flutter customer app (spec 014) and the Next.js admin app (spec 016)
**Performance Goals**: Customer category listing p95 ≤ 1.5 s at 24 items per page under nominal load (SC-001); product detail p95 ≤ 700 ms; admin product create round-trip p95 ≤ 2 s excluding upload time; eligibility endpoint p95 ≤ 150 ms; reindex event dispatched to spec 006 within 2 s of any product mutation (SC-008)
**Constraints**: Single-region per ADR-010 — no cross-region replication; category max depth 6 (FR-001); image per-asset ≤ 8 MB (JPEG/PNG/WebP/AVIF); document per-asset ≤ 20 MB (PDF/PNG) per FR-011/FR-012; variant SKU unique only among non-archived variants; taxonomy-key authoring is migration-only in Phase 1B; every catalog-owned row carries `owner_id` (always populated) and `vendor_id` (NULL at launch) per FR-027
**Scale/Scope**: Launch target 10 k products, ~25 k variants, 5 brands × 50 categories across EG + KSA, ~50 k total media assets, ~10 k documents; admin catalog-editor audience ≤ 20 users; customer read QPS peak ~80 req/s across listing + detail; domain-event volume ≤ 200 k/month in Phase 1B

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Status |
|---|---|---|
| 2 — Real operational depth | Variants, typed attributes, media + documents, restriction rationale, publish lifecycle, audit trail all first-class — not a minimal demo. | PASS |
| 4 — AR + EN editorial | FR-015, FR-023, FR-024: publish blocked without AR + EN parity; locale-fallback flagging on optional fields; editorial sign-off in DoD. | PASS |
| 6 — Multi-vendor-ready | FR-027: `owner_id` populated, `vendor_id` nullable on every catalog-owned row; no Phase-2 migration needed. | PASS |
| 7 — Branding | No UI surface in this spec; media pipeline preserves originals for downstream brand-palette-aware admin UI (spec 016). | PASS |
| 8 — Restricted products visible with prices | FR-017 + FR-018: restricted products remain visible with prices; eligibility gate delegates to spec-004 policy. | PASS |
| 11 — Inventory-ready data shape | FR-020: variant SKU is the stable key for spec 008 joins; `available` delegated at read time. | PASS |
| 12 — Search-ready | FR-019: domain events on every product mutation for incremental reindex; attributes + brand + category all structured. | PASS |
| 15 — Reviews linkage | Product id is the stable identifier spec 022 will link reviews against. | PASS |
| 25 — Audit on critical actions | FR-026 enumerates every auditable catalog event with actor + before + after + correlation-id. | PASS |
| 27 — UX states | FR-021 customer DTO + FR-022 pagination + Edge Cases cover empty/loading/restricted/error/conflict. | PASS |
| 28 — AI-build standard | Vertical-slice per MediatR handler (ADR-003) maps 1:1 to FRs. | PASS |
| 29 — Required spec output | Spec covers goal, roles, business rules, user flow, UI states, data model, validation, API/service requirements, edge cases, acceptance criteria, phase assignment, dependencies. | PASS |
| ADR-010 — Data residency | All catalog storage + object storage in Azure Saudi Arabia Central per ADR-010; no cross-region fan-out. | PASS |

**No violations. No entries in Complexity Tracking.**

## Project Structure

### Documentation (this feature)

```text
specs/phase-1B/005-catalog/
├── plan.md                 # This file
├── spec.md                 # /speckit-specify output
├── research.md             # /speckit-plan Phase 0 output
├── data-model.md           # /speckit-plan Phase 1 output
├── quickstart.md           # /speckit-plan Phase 1 output
├── contracts/              # /speckit-plan Phase 1 output
│   ├── catalog.openapi.yaml
│   └── events.md
├── checklists/
│   └── requirements.md     # /speckit-specify checklist
└── tasks.md                # /speckit-tasks output (NOT created by /speckit-plan)
```

### Source code (under `services/backend_api/`)

```text
services/backend_api/
├── Features/
│   └── Catalog/
│       ├── Categories/          # list, create, update, move, deactivate, reorder
│       ├── Brands/              # CRUD + deactivate
│       ├── Manufacturers/       # CRUD + deactivate
│       ├── Products/            # create, update, publish, archive, draft-to-active
│       ├── Variants/            # create, update, activate, archive (child of product)
│       ├── Media/               # upload, reorder, set-primary, delete
│       ├── Documents/           # upload, delete
│       ├── Attributes/          # per-product attribute upsert
│       ├── Eligibility/         # restricted-product gating endpoint
│       ├── Taxonomy/            # read-only view of migration-seeded keys
│       ├── CustomerListing/     # category listing + product detail for customer surface
│       ├── Events/              # CatalogDomainEvents (MediatR INotification)
│       ├── Persistence/
│       │   ├── CatalogDbContext.cs
│       │   └── Migrations/
│       └── Shared/              # DTOs, policies, extensions
└── Tests/
    ├── Catalog.Unit/
    ├── Catalog.Integration/     # Testcontainers Postgres + fake storage
    └── Catalog.Contract/        # OpenAPI-diff + DTO snapshot
```

## Phase 0 & 1 summary

See `research.md`, `data-model.md`, `contracts/catalog.openapi.yaml`, `contracts/events.md`, and `quickstart.md` in this directory.
