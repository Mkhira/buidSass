# Tasks: Shared Foundations

**Feature**: Shared Foundations | **Spec**: 003 | **Date**: 2026-04-19
**Branch**: `003-shared-foundations`
**Total tasks**: 82 | **Phases**: 9

---

## Phase 1 — Setup (project initialization)

**Goal**: Create package directory structures and all manifest files. No logic yet.

- [X] T001 Create packages/shared_contracts/ with subdirectories dotnet/, dart/, typescript/ (remove any `.gitkeep` placeholder files created by spec 001 T002 before adding subdirectories)
- [X] T002 [P] Create packages/shared_contracts/dotnet/SharedContracts.csproj (NuGet package manifest, version placeholder)
- [X] T003 [P] Create packages/shared_contracts/dart/pubspec.yaml (Dart package manifest, version placeholder)
- [X] T004 [P] Create packages/shared_contracts/typescript/package.json (npm package manifest, version placeholder)
- [X] T005 Create packages/design_system/ Flutter package with pubspec.yaml, lib/src/tokens/, lib/src/theme/, lib/l10n/ directories
- [X] T006 Create services/backend_api/Modules/ subdirectories: AuditLog/, Storage/, Pdf/, Observability/HealthChecks/
- [X] T007 Create scripts/gen-contracts.sh shell script skeleton (empty, executable, with usage comment)
- [X] T008 Create packages/design_system/lib/l10n/app_en.arb and app_ar.arb as minimal skeleton ARB files

---

## Phase 2 — Foundational (blocking prerequisites for all user stories)

**Goal**: Shared infrastructure that all modules depend on — package deps, DB context registration.

- [X] T009 Add Serilog, QuestPDF, and Microsoft.Extensions.Diagnostics.HealthChecks NuGet packages to services/backend_api/services/backend_api.csproj
- [X] T009a Create the application PostgreSQL role `dental_api_app` with limited default privileges: CONNECT, SELECT on public schema; explicitly revoke INSERT/UPDATE/DELETE from default grants (individual tables grant INSERT as needed per module). Document the role name in `services/backend_api/README.md` so T042 (audit revoke script) and downstream module migrations reference the correct role name.
- [X] T009b Add `Noto_Naskh_Arabic-Regular.ttf` (OFL licensed, downloaded from Google Fonts) to `services/backend_api/Resources/Fonts/` and register it as an `EmbeddedResource` in `services/backend_api/services/backend_api.csproj`. This font is required by T061 (TaxInvoiceTemplate Arabic layout).
- [X] T010 Add flutter_localizations and intl dependencies to packages/design_system/pubspec.yaml and run flutter pub get
- [X] T011 Register AuditLog, Storage, Pdf, and Observability modules in services/backend_api/Program.cs as empty module registrations (stubs to be filled per user story — T043, T054, T064, T073 each write to this file sequentially; do not run those tasks in parallel)

---

## Phase 3 — User Story 1: Shared API contracts package

**Story goal**: Auto-generated contracts package published to GitHub Packages from OpenAPI spec, consumable by Flutter and Next.js with semver versioning.

**Independent test criteria**: Run `scripts/gen-contracts.sh` locally → all three output directories (`packages/shared_contracts/dotnet/`, `dart/`, `typescript/`) populated with typed code. Pin a new package version in a test consumer → new type is available.

- [X] T012 [US1] Add backend OpenAPI emit step to .github/workflows/build-and-test.yml (build backend, emit openapi.json as a named CI artifact). Note: spec 001 T014 creates a separate `contract-diff.yml` that also emits openapi.json — this task adds the emit to `build-and-test.yml` for the contracts generation pipeline specifically. Coordinate with spec 001 to confirm which artifact is canonical; if spec 001's artifact is already accessible in the same workflow run, consume it instead of re-emitting.
- [X] T013 [US1] Implement scripts/gen-contracts.sh: add Kiota invocation to generate .NET client into packages/shared_contracts/dotnet/
- [X] T014 [P] [US1] Implement scripts/gen-contracts.sh: add openapi-generator dart-dio invocation into packages/shared_contracts/dart/
- [X] T015 [P] [US1] Implement scripts/gen-contracts.sh: add openapi-typescript invocation into packages/shared_contracts/typescript/types.ts
- [X] T016 [US1] Implement semver extraction in scripts/gen-contracts.sh: read info.version from openapi.json and write to package manifest files
- [X] T017 [US1] Add GitHub Packages publish step for .NET NuGet to .github/workflows/build-and-test.yml (triggers on main merge, uses GITHUB_TOKEN)
- [X] T018 [P] [US1] Add GitHub Packages publish step for Dart package to .github/workflows/build-and-test.yml
- [X] T019 [P] [US1] Add GitHub Packages publish step for npm TypeScript package to .github/workflows/build-and-test.yml
- [X] T020 [US1] Add version-mismatch build warning: CI step that diffs pinned consumer version against latest published version and prints warning if behind in .github/workflows/build-and-test.yml
- [X] T021 [US1] Verify end-to-end locally: run scripts/gen-contracts.sh, assert all three output directories populated and typed code present

---

## Phase 4 — User Story 2: Design system with RTL-aware tokens

**Story goal**: Flutter design-system package with Constitution P7 palette tokens, RTL mirroring rules, and matching CSS custom properties for Next.js admin.

**Independent test criteria**: Test Flutter screen using AppColors.primary + EdgeInsetsDirectional renders mirrored padding in RTL mode. All four P7 hex values (`#1F6F5F`, `#2FA084`, `#6FCF97`, `#EEEEEE`) present in app_colors.dart and tokens.css.

- [X] T022 [US2] Implement AppColors with all four Constitution P7 palette constants in packages/design_system/lib/src/tokens/app_colors.dart
- [X] T023 [P] [US2] Implement AppTypography with headline, body, and caption TextStyles in packages/design_system/lib/src/tokens/app_typography.dart
- [X] T024 [P] [US2] Implement AppSpacing scale constants in packages/design_system/lib/src/tokens/app_spacing.dart
- [X] T025 [US2] Implement AppTheme.light() and AppTheme.dark() ThemeData builders using AppColors, AppTypography, AppSpacing in packages/design_system/lib/src/theme/app_theme.dart
- [X] T026 [US2] Generate packages/design_system/tokens.css with CSS custom properties matching the Dart token values (add generation to scripts or as a build step)
- [X] T027 [US2] Document RTL mirroring rules in packages/design_system/README.md: which tokens use EdgeInsetsDirectional vs EdgeInsets and which icons require RTL mirroring
- [X] T028 [US2] Widget test: render screen with AppColors.primary AppBar + EdgeInsetsDirectional.all(AppSpacing.md) padding in LTR and RTL Directionality widgets, assert padding direction mirrors in packages/design_system/test/rtl_theme_test.dart
- [X] T029 [US2] Add CI grep step to .github/workflows/build-and-test.yml: assert all four P7 hex values present in app_colors.dart and tokens.css (blocks merge if a token is removed or changed)

---

## Phase 5 — User Story 3: Localization scaffolding — AR + EN

**Story goal**: ICU ARB localization with EN and AR resource files, build-time missing-key detection, and editorial-review flag report.

**Independent test criteria**: Adding an EN key without a matching AR key fails the CI gen-l10n step. Keys with `x-editorial-review: true` appear in the CI report. `AppLocalizations.of(context).appTitle` compiles.

- [X] T030 [US3] Populate packages/design_system/lib/l10n/app_en.arb with appTitle key, ICU value, and required ARB metadata (@appTitle description field)
- [X] T031 [US3] Populate packages/design_system/lib/l10n/app_ar.arb with appTitle AR translation and `"x-editorial-review": true` in @appTitle metadata
- [X] T032 [US3] Create packages/design_system/l10n.yaml configuring flutter gen-l10n with --required-resource-attributes, --output-class AppLocalizations, arb-dir: lib/l10n
- [X] T033 [US3] Add flutter gen-l10n CI step to .github/workflows/build-and-test.yml (runs on any PR touching packages/design_system/lib/l10n/; fails if AR key missing)
- [X] T034 [US3] Add editorial-review report CI step to .github/workflows/build-and-test.yml: grep x-editorial-review: true across all ARB files, output grouped report (module | key | EN value | AR status)
- [X] T035 [US3] Widget test: add a test EN key to app_en.arb and omit from app_ar.arb, run flutter gen-l10n and assert it exits non-zero in packages/design_system/test/l10n_missing_key_test.dart
- [X] T036 [US3] Widget test: use AppLocalizations.of(context).appTitle in a minimal test widget and assert it compiles and returns non-empty string in packages/design_system/test/l10n_test.dart

---

## Phase 6 — User Story 4: Central audit-log module

**Story goal**: Synchronous fail-fast IAuditEventPublisher with append-only PostgreSQL store, INSERT-only DB grants, and 5-line calling code criterion verified.

**Independent test criteria**: Integration test publishes one AuditEvent and asserts all fields stored. Integration test with DB down asserts caller operation fails. 5-line calling code criterion demonstrated in test.

- [X] T037 [US4] Implement AuditLogEntry EF Core entity with all fields from data-model.md in services/backend_api/Modules/AuditLog/AuditLogEntry.cs
- [X] T038 [P] [US4] Implement AuditEvent value object with all required fields (actor_id, actor_role, action, entity_type, entity_id, before_state, after_state, reason) in services/backend_api/Modules/AuditLog/AuditEvent.cs
- [X] T039 [US4] Implement IAuditEventPublisher interface with single PublishAsync(AuditEvent, CancellationToken) method in services/backend_api/Modules/AuditLog/IAuditEventPublisher.cs
- [X] T040 [US4] Implement AuditEventPublisher: synchronous INSERT to audit_log_entries, reads correlation_id from IHttpContextAccessor scope, throws on DB failure in services/backend_api/Modules/AuditLog/AuditEventPublisher.cs
- [X] T041 [US4] Create EF Core migration for audit_log_entries table with all fields and indexes from data-model.md in services/backend_api/Migrations/
- [X] T042 [US4] Add SQL post-migration script to revoke UPDATE and DELETE grants on audit_log_entries for the application DB role in services/backend_api/Modules/AuditLog/Migrations/RevokeAuditWriteGrants.sql
- [X] T043 [US4] Register IAuditEventPublisher → AuditEventPublisher in DI in services/backend_api/Program.cs — depends on T011 complete; write sequentially (do not run concurrently with T054, T064, or T073)
- [X] T044 [US4] Add AuditLogReadPolicy: restrict GET /audit-log endpoint to AR, AW, AS roles using ASP.NET Core authorization policy in services/backend_api/Modules/AuditLog/AuditLogReadPolicy.cs
- [X] T045 [US4] Integration test: call PublishAsync with valid AuditEvent, query audit_log_entries, assert all 10 fields stored correctly in services/backend_api/Tests/AuditLog/AuditEventPublisherTests.cs
- [X] T046 [US4] Integration test: take DB offline via Testcontainers pause, call PublishAsync, assert exception propagates (caller receives 5xx, not a silent pass) in services/backend_api/Tests/AuditLog/AuditEventPublisherTests.cs
- [X] T047 [US4] Integration test: demonstrate 5-line calling code — create minimal MediatR handler that calls PublishAsync in ≤5 lines, assert it compiles and passes in services/backend_api/Tests/AuditLog/FiveLineCallingCodeTest.cs

---

## Phase 7 — User Story 5: Storage and PDF abstractions with dev stubs

**Story goal**: IStorageService with LocalDiskStorageService dev stub (market routing, virus-scan hook, signed URLs) + IPdfService with TaxInvoiceTemplate (AR RTL + EN LTR, QuestPDF).

**Independent test criteria**: Upload → file on disk + DB record + signed URL. Upload with scanner returning ServiceUnavailable → StorageUploadBlockedException, no file, no record. Market routing to KSA and EG subdirectories verified. PDF render in AR and EN produces non-empty valid PDF bytes.

### Storage abstraction (T048–T058)

- [X] T048 [US5] Implement IStorageService interface (UploadAsync, GetSignedUrlAsync, DeleteAsync) in services/backend_api/Modules/Storage/IStorageService.cs
- [X] T049 [P] [US5] Implement IVirusScanService interface (ScanAsync → Task<ScanResult>) in services/backend_api/Modules/Storage/IVirusScanService.cs
- [X] T050 [US5] Implement LocalVirusScanService stub that always returns ScanResult.Clean in services/backend_api/Modules/Storage/LocalVirusScanService.cs
- [X] T051 [US5] Implement LocalDiskStorageService: UploadAsync calls IVirusScanService, writes to tmp/storage/{market}/, creates StoredFile record, returns signed URL as http://localhost:5000/dev-storage/{fileId} in services/backend_api/Modules/Storage/LocalDiskStorageService.cs
- [X] T052 [US5] Implement StoredFile EF Core entity with all fields from data-model.md in services/backend_api/Modules/Storage/StoredFile.cs
- [X] T053 [US5] Create EF Core migration for stored_files table in services/backend_api/Migrations/
- [X] T054 [US5] Register IStorageService → LocalDiskStorageService and IVirusScanService → LocalVirusScanService in DI for development environment in services/backend_api/Program.cs — depends on T011 complete; write sequentially (do not run concurrently with T043, T064, or T073)
- [X] T055 [US5] Integration test: upload file via LocalDiskStorageService → assert file exists in tmp/storage/{market}/, StoredFile DB record created, signed URL returned in services/backend_api/Tests/Storage/StorageServiceTests.cs
- [X] T056 [US5] Integration test: inject mock IVirusScanService returning ServiceUnavailable → assert StorageUploadBlockedException thrown, no file on disk, no DB record in services/backend_api/Tests/Storage/StorageServiceTests.cs
- [X] T057 [P] [US5] Integration test: upload with market=KSA → file written to tmp/storage/KSA/ (not EG/) in services/backend_api/Tests/Storage/StorageServiceTests.cs
- [X] T058 [P] [US5] Integration test: upload with market=EG → file written to tmp/storage/EG/ (not KSA/) in services/backend_api/Tests/Storage/StorageServiceTests.cs

### PDF abstraction (T059–T067)

- [X] T059 [US5] Implement IPdfService interface (RenderAsync(templateName, locale, data, ct) → Task<byte[]>) in services/backend_api/Modules/Pdf/IPdfService.cs
- [X] T060 [US5] Implement PdfTemplateRegistry: register templates by name string, throw TemplateNotFoundException for unrecognized names in services/backend_api/Modules/Pdf/PdfTemplateRegistry.cs
- [X] T061 [US5] Implement TaxInvoiceTemplate as QuestPDF IDocument with AR locale (TextDirection.RightToLeft + Noto Naskh Arabic embedded font) and EN locale (LTR) layout variants in services/backend_api/Modules/Pdf/Templates/TaxInvoiceTemplate.cs
- [X] T062 [US5] Implement QuestPdfService: resolve template from PdfTemplateRegistry, call QuestPDF Document.GeneratePdf(), return byte[] in services/backend_api/Modules/Pdf/QuestPdfService.cs
- [X] T063 [US5] Implement StubPdfService: return a minimal single-page PDF with data payload serialized as JSON text (for test assertion) in services/backend_api/Modules/Pdf/StubPdfService.cs
- [X] T064 [US5] Register IPdfService → StubPdfService in development/test and QuestPdfService in production in services/backend_api/Program.cs — depends on T011 complete; write sequentially (do not run concurrently with T043, T054, or T073)
- [X] T065 [US5] Integration test: RenderAsync("tax-invoice", AR, minimalData) → returned byte[] is non-empty and valid PDF in services/backend_api/Tests/Pdf/PdfServiceTests.cs
- [X] T066 [P] [US5] Integration test: RenderAsync("tax-invoice", EN, minimalData) → returned byte[] is non-empty and valid PDF in services/backend_api/Tests/Pdf/PdfServiceTests.cs
- [X] T067 [US5] Unit test: RenderAsync("nonexistent-template", EN, data) → throws TemplateNotFoundException in services/backend_api/Tests/Pdf/PdfServiceTests.cs

---

## Phase 8 — User Story 6: Baseline observability

**Story goal**: Serilog JSON structured logging with correlation-ID enrichment, CorrelationIdMiddleware, and /health endpoint with DB + storage checks.

**Independent test criteria**: Request with X-Correlation-Id header → all log lines carry same ID. Request without header → generated UUID on all log lines + response header. GET /health with DB up → 200 OK within 500ms. GET /health with DB down → 503.

- [X] T068 [US6] Configure Serilog in services/backend_api/Program.cs with JSON console sink and Serilog.Enrichers.CorrelationId enricher (all log lines carry CorrelationId field)
- [X] T069 [US6] Implement CorrelationIdMiddleware: read X-Correlation-Id header or generate UUID, set on Serilog LogContext scope and response header in services/backend_api/Modules/Observability/CorrelationIdMiddleware.cs
- [X] T070 [US6] Implement CorrelationId DelegatingHandler: inject X-Correlation-Id header on all outbound HttpClient calls within request scope in services/backend_api/Modules/Observability/CorrelationIdDelegatingHandler.cs
- [X] T071 [P] [US6] Implement DbConnectivityCheck IHealthCheck: execute EF Core DB ping, return Healthy or Unhealthy in services/backend_api/Modules/Observability/HealthChecks/DbConnectivityCheck.cs
- [X] T072 [P] [US6] Implement StorageReachabilityCheck IHealthCheck: perform head/ping request to storage service, return Healthy or Unhealthy in services/backend_api/Modules/Observability/HealthChecks/StorageReachabilityCheck.cs
- [X] T073 [US6] Register CorrelationIdMiddleware and GET /health endpoint with DbConnectivityCheck and StorageReachabilityCheck in services/backend_api/Program.cs — depends on T011 complete; write sequentially (do not run concurrently with T043, T054, or T064)
- [X] T074 [US6] Integration test: send request with X-Correlation-Id: test-abc-123, capture log output, assert every log line contains CorrelationId: "test-abc-123" in services/backend_api/Tests/Observability/CorrelationIdTests.cs
- [X] T075 [US6] Integration test: GET /health with DB container running → 200 OK; pause DB container → GET /health → 503 Service Unavailable, both within 500ms in services/backend_api/Tests/Observability/HealthCheckTests.cs

---

## Phase 9 — Polish & cross-cutting concerns

**Goal**: All tests green in CI, contracts pipeline verified end-to-end, formatting clean, DoD gate passed.

- [X] T076 Run full integration test suite in CI with Testcontainers + Postgres and confirm all Phase 5–8 tests pass (update .github/workflows/build-and-test.yml if any Testcontainers config is missing)
- [X] T077 Verify contracts pipeline end-to-end: dotnet + npm publish verified on GitHub Actions run `24636917073` (main @ fcf0eb1 → post-PR-#10) — NuGet "Your package was pushed" for `BuidSass.SharedContracts 0.2604.23`, npm `+ @mkhira/shared-contracts@0.2604.23`. Dart publish deferred to Phase 1.5 per ADR note.
- [X] T078 Confirm validate-diagrams CI job from spec 002 still passes after spec 003 source files are added (no Mermaid regressions)
- [X] T079 Run dotnet format, dart format, eslint, and prettier across all spec 003 files and fix any formatting violations
- [X] T080 Embed context fingerprint (scripts/compute-fingerprint.sh output) in PR description; confirm DoD UC-1 through UC-8 checklist passes

---

## Dependencies

```
T001 → T002, T003, T004, T005, T006, T007, T008
T008 → T009, T010, T011

Phase 3 (US-1 contracts):
T011 → T012 → T013 → T014, T015 → T016 → T017, T018, T019 → T020 → T021

Phase 4 (US-2 design system) — parallel with US-1 after T005:
T005 → T022 → T023, T024 → T025 → T026 → T027 → T028 → T029

Phase 5 (US-3 localization) — parallel with US-1 and US-2 after T010:
T010 → T030, T031 → T032 → T033 → T034 → T035, T036

Phase 6 (US-4 audit-log) — parallel with US-1/2/3 after T009a, T011:
T009a, T009b, T011 → T037, T038 → T039 → T040 → T041 → T042 → T043 → T044 → T045, T046 → T047

Phase 7 (US-5 storage + PDF) — parallel with US-4 after T009a, T009b, T011:
T009a, T009b, T011 → T048, T049 → T050, T051 → T052 → T053 → T054 → T055, T056, T057, T058
T009b → T059 → T060 → T061 → T062, T063 → T064 → T065, T066 → T067
(Storage and PDF tracks within US-5 are parallel with each other from T048/T059 onwards)
⚠️ Program.cs constraint: T043, T054, T064, T073 all write to the same file — run these sequentially even when their phases run in parallel. Recommended order: T043 → T054 → T064 → T073.

Phase 8 (US-6 observability) — depends on T068 (Serilog):
T011 → T068 → T069, T070 → T071, T072 → T073 → T074, T075

Phase 9 (Polish):
All phases complete → T076 → T077 → T078 → T079 → T080
```

---

## Parallel execution opportunities

| Parallelizable group | Tasks | Shared dependency |
|---|---|---|
| Package manifests | T002, T003, T004 | T001 |
| Script generators (per platform) | T014, T015 | T013 |
| CI publish steps | T018, T019 | T017 |
| Token definitions | T023, T024 | T022 |
| Market routing tests | T057, T058 | T055 |
| PDF AR + EN tests | T065, T066 | T064 |
| Health check implementations | T071, T072 | T068 |
| US-1 vs US-2 vs US-3 | Full phases 3, 4, 5 | T008–T011 complete |
| US-4 (audit) vs US-5 storage track | T037–T058 | T009a, T009b, T011 |
| US-5 storage track vs PDF track | T048–T058 vs T059–T067 | T009b, T011 |
| **⚠️ NOT parallelizable** | T043, T054, T064, T073 | All write to Program.cs — sequential only |

---

## Implementation strategy

**MVP scope (Phases 1–6, T001–T047)**: All four P1 stories (contracts, design system, localization, audit-log) are independently testable after Phase 6. This is the minimum required for downstream backend domain specs (004+) to begin — they all need `IAuditEventPublisher` and `IStorageService` at minimum.

**Full scope (all phases, T001–T080)**: Required for Phase 1A DoD. PDF abstraction (US-5) and observability (US-6) are needed before any domain spec with PDF output (spec 011, tax/invoices) or before staging deployment.

**Recommended order for a single AI agent session**:
1. Phases 1–2 (setup) in sequence
2. Phases 3 + 4 + 5 in parallel (three independent package tracks)
3. Phase 6 (audit-log) — most critical shared dependency
4. Phase 7 storage track, then PDF track
5. Phase 8 (observability)
6. Phase 9 (polish + DoD)
