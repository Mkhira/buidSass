# Implementation Plan: Search

**Branch**: `phase_1B_creating_specs` (spec-creation branch; implementation branch will be spun off per PR) | **Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1B/006-search/spec.md`

## Summary

Deliver the search module for the dental commerce platform in Phase 1B: keyword + facet + autocomplete product search with Arabic normalization, typo tolerance, exact SKU/barcode short-circuit, empty-state guidance, and event-driven incremental reindex from spec 005. The module sits behind a clean provider port (Meilisearch at launch per ADR-005; swappable per Principle 26). Launch scope includes per-market scoping, restricted-product visibility with `priceToken` delegation to spec 007-a, a single multilingual index with dual language-analyzer fields, an admin-only full-reindex safety valve, observability logs for queries (no click tracking), and seeded synonym + stopword YAML files (admin console deferred to spec 1.5-d).

**Technical approach**: .NET 9 vertical-slice + MediatR (ADR-003) living at `services/backend_api/Features/Search/`. Provider port `ISearchProvider` with `MeilisearchProvider` adapter and `StaticSearchProvider` stub for tests. Incremental reindex is a MediatR `INotificationHandler` subscribing to catalog domain events from spec 005's `CatalogDomainEvents`. Full reindex is a Hangfire-style background job (built on `IHostedService` + in-process queue for Phase 1B; a distributed queue is out of scope). All queries logged via Serilog + OpenTelemetry. Admin endpoints gated by the spec-004 RBAC policy `search.reindex`. Contracts published via `packages/shared_contracts/search/`. Arabic normalization implemented as a module-owned `ArabicAnalyzerSettings` resource applied both at indexing (via Meilisearch configured stopwords + custom tokenizer rules) and in query pre-normalization wrappers for parity under provider swap.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (backend)
**Primary Dependencies**: ASP.NET Core 9, MediatR, FluentValidation, `Meilisearch` (official .NET client), `YamlDotNet` for synonym/stopword seeds, Polly for bounded-backoff retry, Serilog + OpenTelemetry for observability, HashiCorp-style murmur hash or `System.Security.Cryptography.HMACSHA256` for caller-id hashing (FR-021)
**Storage**: Meilisearch instance in Azure Saudi Arabia Central (ADR-010, ADR-005) with one index per market (`products_eg`, `products_ksa`) — one logical index per market keeps residency and per-market boundaries clean; physical backing is a single Meilisearch deployment. Audit events appended to `audit_events` (spec 003).
**Testing**: xUnit + FluentAssertions; WebApplicationFactory-based integration tests; Testcontainers for a throwaway Meilisearch instance; `StaticSearchProvider` stub for provider-swap tests; curated AR corpus (100 query/expected-hit pairs) for Arabic normalization correctness (SC-005); k6 for latency (SC-002); FsCheck for property-based tests on facet count intersection semantics (FR-009)
**Target Platform**: Linux container (Azure App Service / AKS) in Azure Saudi Arabia Central; .NET 9 runtime
**Project Type**: web-service (backend search API)
**Performance Goals**: Search p95 ≤ 500 ms at 24 hits (SC-002); autocomplete p95 ≤ 150 ms; reindex event propagation p95 ≤ 2 s (SC-001); full reindex of 10 k products ≤ 5 min (SC-007)
**Constraints**: Single-region per ADR-010; per-market index; query clamp 200 chars; pagination depth clamp pageSize × page ≤ 20 000; autocomplete payload ≤ 8 products + 3 brands + 3 categories (FR-011); no click tracking in Phase 1B (deferred to spec 028); Arabic normalization ON by default for `ar`-tagged fields; retry with bounded exponential backoff on provider errors (FR-016)
**Scale/Scope**: Launch target 10 k products × 2 markets = ~20 k index documents; expected query QPS peak ~100 across search+autocomplete; expected reindex-event rate peak ~5/s during admin bulk authoring; incident-only full-reindex cadence (not scheduled)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Status |
|---|---|---|
| 2 — Real operational depth | Keyword + facets + autocomplete + Arabic normalization + SKU short-circuit + empty-state UX + admin reindex + provider swap all first-class. | PASS |
| 4 — AR + EN editorial parity | FR-004, FR-005 mandate Arabic-normalized indexing/querying; SC-005 enforces 95% Arabic corpus accuracy. | PASS |
| 8 — Restricted products visible | FR-003 keeps restricted products in results with `priceToken`; matches catalog spec 005. | PASS |
| 12 — Search is a core capability | FR-009 facets + FR-011 autocomplete + FR-004 Arabic normalization + FR-008 SKU/barcode short-circuit = full Principle 12 coverage. | PASS |
| 25 — Audit on critical actions | FR-024 audits admin reindex lifecycle. Customer queries go to observability logs per FR-021, not the audit log (correct separation). | PASS |
| 26 — Search architecture decoupled | FR-020 mandates the `ISearchProvider` port; SC-008 verifies swappability via stub adapter. | PASS |
| 27 — UX states | FR-017 empty state; FR-018 clamp flags; FR-019 error envelope — all states captured. | PASS |
| 28 — AI-build standard | Vertical-slice per MediatR handler (ADR-003) maps 1:1 to FRs. | PASS |
| 29 — Required spec output | Spec covers goal, roles, rules, flow, UI states, data model, validation, APIs, edge cases, acceptance criteria, phase, dependencies. | PASS |
| ADR-005 — Meilisearch | Locked as the launch provider; port keeps it swappable. | PASS |
| ADR-010 — Data residency | Meilisearch hosted in Azure Saudi Arabia Central; per-market logical indexes. | PASS |

**No violations. No entries in Complexity Tracking.**

## Project Structure

### Documentation (this feature)

```text
specs/phase-1B/006-search/
├── plan.md                 # This file
├── spec.md                 # /speckit-specify output
├── research.md             # /speckit-plan Phase 0 output
├── data-model.md           # /speckit-plan Phase 1 output
├── quickstart.md           # /speckit-plan Phase 1 output
├── contracts/              # /speckit-plan Phase 1 output
│   ├── search.openapi.yaml
│   └── events.md
├── checklists/
│   └── requirements.md     # /speckit-specify checklist
└── tasks.md                # /speckit-tasks output
```

### Source code (under `services/backend_api/`)

```text
services/backend_api/
├── Features/
│   └── Search/
│       ├── Query/                 # customer search + autocomplete query handlers
│       ├── Indexing/              # catalog-event subscribers + index writers
│       ├── Reindex/               # admin full-reindex command + job orchestration
│       ├── Provider/              # ISearchProvider port + Meilisearch adapter + Static stub
│       ├── Normalization/         # Arabic analyzer settings, stopwords, synonyms
│       ├── Seeds/                 # stopwords.ar.yaml, stopwords.en.yaml, synonyms.yaml
│       ├── Observability/         # query log writer, health endpoint
│       ├── Persistence/            # reindex_jobs table (audit source of truth for jobs)
│       └── Shared/                # DTOs, error codes, facet-bucket projections
└── Tests/
    ├── Search.Unit/
    ├── Search.Integration/        # Testcontainers Meilisearch + catalog event fixtures
    └── Search.Contract/           # OpenAPI-diff + DTO snapshot
```
