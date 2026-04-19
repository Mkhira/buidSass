# Implementation Plan: Shared Foundations

**Branch**: `003-shared-foundations` | **Date**: 2026-04-19 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/003-shared-foundations/spec.md`
**Depends on**: Spec 001 at DoD, Spec 002 at DoD

## Summary

Build the shared infrastructure consumed by every Phase-1 domain module: a generated contracts package (three platform targets), a design-system package with RTL-aware tokens, ICU localization scaffolding with an editorial-review flag, a central audit-log module (synchronous, fail-fast, append-only), provider-agnostic storage and PDF abstractions with working dev stubs, and baseline observability (health-check, Serilog structured logging, correlation-ID middleware). No domain module beyond spec 003 may roll its own audit, file-upload, PDF, or localization logic.

## Technical Context

**Language/Version**: C# / .NET 9 (backend modules); Dart 3 / Flutter 3.x (design system + localization package); TypeScript 5 (Next.js contracts)
**Primary Dependencies**:
- `Kiota` v1.x — .NET OpenAPI client generator
- `openapi-generator` (dart-dio template) — Dart client generation
- `openapi-typescript` — TypeScript type generation
- `flutter_localizations` + `intl` — ICU localization (Flutter)
- `Microsoft.Extensions.Localization` + `MessageFormat.NET` — .NET localization
- `QuestPDF` v2024.x (MIT) — PDF generation with RTL support
- Noto Naskh Arabic font (OFL) — Arabic font embedding in PDFs
- `Serilog` + `Serilog.Sinks.Console` + `Serilog.Enrichers.CorrelationId` — structured logging
- `Microsoft.Extensions.Diagnostics.HealthChecks` — health-check endpoint (built-in)
- `Testcontainers` + `xUnit` — integration test infrastructure
**Storage**: PostgreSQL — `audit_log_entries` table (append-only), `stored_files` table; local disk (dev stub for storage abstraction)
**Testing**: Unit (xUnit + Moq), Integration (Testcontainers + Postgres), Contract (oasdiff, already in CI from spec 001)
**Target Platform**: Monorepo — `packages/shared_contracts/`, `packages/design_system/`, `services/backend_api/Modules/{AuditLog,Storage,Pdf,Observability}/`
**Performance Goals**: Health-check endpoint responds within 500ms; contracts package published within 5 minutes of merge
**Constraints**: Dev stubs only for storage (local disk) and PDF (stub renderer) in this spec; production providers wired in Stage 7. Audit writes are synchronous and fail-fast — no message bus.
**Scale/Scope**: Shared by all 26 downstream domain specs; interfaces must remain stable across the Phase-1 build

## Constitution Check

| Principle | Check | Status |
|---|---|---|
| P1 (multi-vendor ready) | `StoredFile` has market routing (KSA/EG); `AuditLogEntry` is platform-owned (no `vendor_id` — non-ownable per P25) | ✅ Pass |
| P4 (localization) | ICU ARB scaffolding for AR + EN; editorial-review flag; build error on missing AR key | ✅ Pass |
| P5 (market config) | Storage abstraction routes by `MarketCode` parameter — no hardcoded market logic | ✅ Pass |
| P6 (multi-vendor ready DB) | `StoredFile` non-ownable; `AuditLogEntry` non-ownable — consistent with ERD contract from spec 002 | ✅ Pass |
| P7 (branding palette) | Design token constants map 1:1 to `#1F6F5F`, `#2FA084`, `#6FCF97`, `#EEEEEE` — no deviation | ✅ Pass |
| P22 (fixed tech) | .NET 9, Flutter, PostgreSQL, Next.js — no new locked technology introduced | ✅ Pass |
| P23 (modular monolith) | All backend modules under `services/backend_api/Modules/`; no microservice extraction | ✅ Pass |
| P24 (state machines) | No Principle-24 domains implemented here — no state machine required | ✅ Pass |
| P25 (audit) | Append-only store, actor + before/after + correlation ID, central module, no per-module copies | ✅ Pass |
| P28 (AI-build standard) | All interfaces defined with explicit contracts; acceptance tests specified per phase | ✅ Pass |
| P29 (spec output standard) | All 12 sections present in spec.md | ✅ Pass |
| P30 (phasing) | Phase 1A — Foundation | ✅ Pass |
| P31 (constitution supremacy) | No conflicts detected | ✅ Pass |

**Post-Phase-1 re-check**: Audit write is synchronous fail-fast — consistent with P25 (no silent audit gaps). QuestPDF is a documentation/rendering library, not a locked product technology — consistent with P22 (locked tech refers to product platform choices).

## Project Structure

### Documentation (this feature)

```text
specs/003-shared-foundations/
├── plan.md                                    # This file
├── research.md                                # Phase 0 — all decisions resolved
├── data-model.md                              # Phase 1 — entity schemas + interface signatures
├── quickstart.md                              # Phase 1 — implementation guide
├── contracts/
│   └── shared-foundations-contract.md        # Phase 1 — service interface contracts
└── tasks.md                                   # Phase 2 — created by /speckit-tasks
```

### Source Code (repository root)

```text
packages/
├── shared_contracts/
│   ├── dotnet/                                # Kiota-generated .NET client
│   ├── dart/                                  # openapi-generator dart-dio output
│   └── typescript/
│       └── types.ts                           # openapi-typescript output
└── design_system/
    ├── lib/
    │   ├── src/
    │   │   ├── tokens/
    │   │   │   ├── app_colors.dart            # P7 palette constants
    │   │   │   ├── app_typography.dart
    │   │   │   └── app_spacing.dart
    │   │   └── theme/
    │   │       └── app_theme.dart
    │   └── l10n/
    │       ├── app_en.arb                     # EN messages (authoritative)
    │       └── app_ar.arb                     # AR messages (editorial-review flagged)
    └── tokens.css                             # CSS custom properties for Next.js

services/
└── backend_api/
    └── Modules/
        ├── AuditLog/
        │   ├── AuditLogEntry.cs               # EF Core entity
        │   ├── AuditEvent.cs                  # Value object
        │   ├── IAuditEventPublisher.cs        # Interface
        │   └── AuditEventPublisher.cs         # Implementation
        ├── Storage/
        │   ├── IStorageService.cs
        │   ├── IVirusScanService.cs
        │   ├── LocalDiskStorageService.cs     # Dev stub
        │   ├── LocalVirusScanService.cs       # Dev stub (always Clean)
        │   └── StoredFile.cs                  # EF Core entity
        ├── Pdf/
        │   ├── IPdfService.cs
        │   ├── PdfTemplateRegistry.cs
        │   ├── Templates/
        │   │   └── TaxInvoiceTemplate.cs      # QuestPDF IDocument
        │   ├── QuestPdfService.cs             # Production implementation
        │   └── StubPdfService.cs              # Dev/test stub
        └── Observability/
            ├── CorrelationIdMiddleware.cs
            └── HealthChecks/
                ├── DbConnectivityCheck.cs
                └── StorageReachabilityCheck.cs

scripts/
└── gen-contracts.sh                           # Local contracts generation

.github/workflows/
└── build-and-test.yml                         # + gen-contracts job (Phase B)
```

## Implementation Phases

### Phase A — Package scaffolding
Create `packages/shared_contracts/` and `packages/design_system/` directory structures with manifest files (`.csproj`, `pubspec.yaml`, `package.json`) and empty stubs.

### Phase B — OpenAPI contracts generation pipeline
Wire CI to emit `openapi.json`, run all three generators (Kiota, openapi-generator dart-dio, openapi-typescript), and publish to GitHub Packages with semver from `info.version`. Add `scripts/gen-contracts.sh` for local runs.

### Phase C — Design system tokens
Implement `AppColors`, `AppTypography`, `AppSpacing`, `AppTheme` with Constitution Principle 7 palette. Generate `tokens.css` from the same constants. Document RTL mirroring rules.

### Phase D — ICU localization scaffolding
Populate `app_en.arb` and `app_ar.arb` with at least one real key. Configure `flutter gen-l10n` with `--required-resource-attributes`. Add CI step for missing-key detection and editorial-review report.

### Phase E — Audit-log module
Implement `IAuditEventPublisher`, `AuditEventPublisher`, `audit_log_entries` migration with INSERT-only DB grants. Integration test: publish event + assert stored; DB-down test + assert caller fails.

### Phase F — Storage abstraction + dev stub
Implement `IStorageService`, `IVirusScanService`, `LocalDiskStorageService`, `LocalVirusScanService`. Integration tests for upload, virus-blocked upload, and market routing.

### Phase G — PDF abstraction + dev stub + tax-invoice template
Implement `IPdfService`, `PdfTemplateRegistry`, `TaxInvoiceTemplate` (QuestPDF, AR + EN), `QuestPdfService`, `StubPdfService`. Integration tests for AR and EN render. Unit test for unknown template.

### Phase H — Observability baseline
Configure Serilog with JSON sink and CorrelationId enricher. Implement `CorrelationIdMiddleware`. Register `/health` endpoint with `db-connectivity` and `storage-reachability` checks.

### Phase I — Integration, CI wiring, DoD sign-off
All tests green in CI. Contracts pipeline verified end-to-end. Context fingerprint in PR description. DoD checklist (UC-1 through UC-8) all pass.

## Complexity Tracking

| Item | Complexity note |
|---|---|
| Audit fail-fast posture | Synchronous write means any DB hiccup during audit fails the entire request. Mitigation: DB health check at startup + circuit breaker pattern deferred to Phase 1.5. |
| QuestPDF RTL | Arabic text requires explicit `TextDirection.RightToLeft` per text element in QuestPDF v2024. Test with a real Arabic string, not placeholder Latin text. |
| three-platform contracts generation | Three generators must stay in sync. If the Dart generator lags on a feature (e.g., `oneOf` discriminators), document the gap and add a workaround script. |
| AR editorial-review reporting | The CI report must be human-readable and actionable. Format: `module | key | EN value | AR value (NEEDS REVIEW)`. |
