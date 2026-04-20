# Quickstart: Shared Foundations

**Branch**: `003-shared-foundations` | **Date**: 2026-04-19
**Depends on**: Spec 001 at DoD (CI pipeline live, CODEOWNERS active), Spec 002 at DoD (ERD and testing strategy merged)

Implementation phases in order. Each section lists deliverables and the verification step.

---

## Phase A — Package scaffolding

**Deliverables**:
- `packages/shared_contracts/` directory with package manifests (NuGet `.csproj`, `pubspec.yaml`, `package.json`) — empty shells, no generated code yet.
- `packages/design_system/` Flutter package with `lib/src/tokens/` directory and empty `AppColors`, `AppTypography`, `AppSpacing` stubs.
- `packages/design_system/lib/l10n/` with empty `app_en.arb` and `app_ar.arb` skeleton files.

**Verify**: `ls packages/` shows `shared_contracts/` and `design_system/`. Each package manifest is valid (e.g., `dotnet restore` exits 0 for the NuGet package; `flutter pub get` exits 0 for the Dart package).

---

## Phase B — OpenAPI contracts generation pipeline

**Deliverables**:
- CI step in `.github/workflows/build-and-test.yml` that: (1) builds the backend and emits `openapi.json`, (2) runs Kiota to generate `packages/shared_contracts/dotnet/`, (3) runs `openapi-generator dart-dio` to generate `packages/shared_contracts/dart/`, (4) runs `openapi-typescript` to generate `packages/shared_contracts/typescript/types.ts`, (5) publishes all three to GitHub Packages at the semver derived from `openapi.json info.version`.
- Script `scripts/gen-contracts.sh` that can run the generation step locally.

**Verify**:
1. Run `scripts/gen-contracts.sh` locally — all three output directories are populated with typed code.
2. Add a dummy endpoint to the backend, run CI — generated packages are updated and published with a bumped version.
3. Pin the new package version in a consumer project — the new endpoint type is available.

---

## Phase C — Design system tokens

**Deliverables**:
- `packages/design_system/lib/src/tokens/app_colors.dart` — exports `AppColors` with all four Constitution Principle 7 tokens (`primary: #1F6F5F`, `secondary: #2FA084`, `accent: #6FCF97`, `neutral: #EEEEEE`) plus semantic variants.
- `packages/design_system/lib/src/tokens/app_typography.dart` — `AppTypography` with `TextStyle` definitions for headline, body, caption in LTR and RTL variants.
- `packages/design_system/lib/src/tokens/app_spacing.dart` — spacing scale constants.
- `packages/design_system/lib/src/theme/app_theme.dart` — `AppTheme.light()` and `AppTheme.dark()` returning `ThemeData` built from the tokens.
- `packages/design_system/tokens.css` — CSS custom properties file generated from the same Dart constants.
- RTL mirroring documented in `packages/design_system/README.md`: which layout properties use `EdgeInsetsDirectional` vs `EdgeInsets`.

**Verify**:
1. Create a single Flutter screen that uses `AppColors.primary` for the AppBar background and `EdgeInsetsDirectional.all(AppSpacing.md)` for padding. Render in LTR (EN) and RTL (AR) via `Directionality` widget — padding mirrors correctly in RTL.
2. Grep `packages/design_system/lib/src/tokens/app_colors.dart` for `#1F6F5F`, `#2FA084`, `#6FCF97`, `#EEEEEE` — all four present.
3. Open `tokens.css` — all four color variables present with matching hex values.

---

## Phase D — ICU localization scaffolding

**Deliverables**:
- `packages/design_system/lib/l10n/app_en.arb` — at least one real key (e.g., `appTitle`) with description.
- `packages/design_system/lib/l10n/app_ar.arb` — same key with AR translation and `"x-editorial-review": true` if not yet human-reviewed.
- `flutter gen-l10n` configured with `--required-resource-attributes` and `--output-class AppLocalizations`.
- CI step that runs `flutter gen-l10n` and fails if any key in `app_en.arb` is absent from `app_ar.arb`.
- CI step that outputs a report of all keys with `"x-editorial-review": true` — report is printed (does not block merge at this stage but is required output).

**Verify**:
1. Add a key to `app_en.arb` and omit it from `app_ar.arb` — CI `gen-l10n` step fails.
2. Add a key to `app_ar.arb` with `"x-editorial-review": true` — CI report lists it.
3. Use `AppLocalizations.of(context).appTitle` in the test screen — compiles without error.

---

## Phase E — Audit-log module

**Deliverables**:
- `services/backend_api/Modules/AuditLog/` directory containing:
  - `AuditLogEntry` EF Core entity mapped to `audit_log_entries` table.
  - `AuditEvent` value object (all required fields per data-model.md).
  - `IAuditEventPublisher` interface.
  - `AuditEventPublisher` implementation — synchronous `INSERT` to PostgreSQL, reads correlation ID from `IHttpContextAccessor`.
  - EF Core migration creating the `audit_log_entries` table with no `UPDATE`/`DELETE` grants.
  - Module registered in DI container.
- Integration test: publishes one event, reads it back, asserts all fields stored correctly.
- Integration test: brings DB connection down mid-publish, asserts caller operation fails (exception propagates).

**Verify**:
1. Run integration tests — both pass.
2. Attempt `context.AuditLogEntries.Remove(entry); context.SaveChanges()` — throws (DB-level revoke in place).
3. Call `PublishAsync` from a controller action with five lines of calling code — confirm it fits within the SC-001 acceptance criterion.

---

## Phase F — Storage abstraction + dev stub

**Deliverables**:
- `services/backend_api/Modules/Storage/` containing:
  - `IStorageService` interface.
  - `IVirusScanService` interface + `LocalVirusScanService` stub (always returns `Clean`).
  - `LocalDiskStorageService` dev stub — writes to `tmp/storage/{market}/`, returns `http://localhost:5000/dev-storage/{fileId}` as signed URL.
  - `StoredFile` EF Core entity + migration.
  - Module registered in DI with `LocalDiskStorageService` as default in development.
- Integration tests:
  - Upload a file via `LocalDiskStorageService` — file appears on disk, `StoredFile` record created, signed URL returned.
  - Upload with `LocalVirusScanService` returning `ServiceUnavailable` (mock override) — `StorageUploadBlockedException` thrown, no file on disk, no DB record.
  - Upload with `market = KSA` — file written to `tmp/storage/KSA/` subdirectory.
  - Upload with `market = EG` — file written to `tmp/storage/EG/` subdirectory.

**Verify**: All four integration tests pass. `IStorageService` is resolvable from DI container in development mode.

---

## Phase G — PDF abstraction + stub + tax-invoice template

**Deliverables**:
- `services/backend_api/Modules/Pdf/` containing:
  - `IPdfService` interface.
  - `PdfTemplateRegistry` — registers templates by name; throws `TemplateNotFoundException` for unrecognized names.
  - `TaxInvoiceTemplate` — QuestPDF `IDocument` implementation with AR (RTL + Noto Naskh Arabic font) and EN (LTR) layout variants.
  - `QuestPdfService` production implementation.
  - `StubPdfService` dev stub — returns a minimal single-page PDF with data serialized as JSON text.
  - Module registered in DI with `StubPdfService` as default in development and test; `QuestPdfService` in production.
- Integration test: render `tax-invoice` template in AR locale — assert returned `byte[]` is a valid PDF and is non-empty.
- Integration test: render `tax-invoice` template in EN locale — assert returned `byte[]` is a valid PDF.
- Unit test: call `RenderAsync("nonexistent-template", ...)` — `TemplateNotFoundException` thrown.

**Verify**: All three tests pass. `IPdfService` is resolvable from DI. The rendered AR PDF opens in a PDF viewer with RTL text direction visible.

---

## Phase H — Observability baseline

**Deliverables**:
- Serilog configured in `services/backend_api/Program.cs` with JSON console sink and `CorrelationId` enricher.
- `CorrelationIdMiddleware` registered — reads `X-Correlation-Id` header or generates UUID; sets on Serilog scope and response header.
- `DelegatingHandler` on all outbound `HttpClient` instances that injects the correlation ID header.
- `GET /health` endpoint registered with two checks: `db-connectivity` and `storage-reachability`. Returns `200 OK` or `503`.

**Verify**:
1. Send a request with `X-Correlation-Id: test-123` — every log line for that request contains `"CorrelationId": "test-123"`.
2. Send a request without the header — a generated UUID appears on all log lines; the same UUID is in the response `X-Correlation-Id` header.
3. `GET /health` with DB available — returns `200 OK` within 500ms.
4. `GET /health` with DB connection string set to an invalid host — returns `503 Service Unavailable`.

---

## Phase I — Integration tests, CI wiring, and DoD sign-off

**Deliverables**:
- All integration tests from Phases E, F, G, H passing in CI (Testcontainers + Postgres).
- Confirm `validate-diagrams` CI job (spec 002) still passes after spec 003 files are added — no Mermaid regressions.
- Contracts package CI step verified end-to-end (Phase B) with a real backend build.
- PR description includes context fingerprint (`scripts/compute-fingerprint.sh` output).

**Verify** (DoD acceptance gate):

| UC item | Verification |
|---|---|
| UC-1: acceptance scenarios pass | Phases A–H verification steps above all green |
| UC-2: lint-format green | `dotnet format`, `dart format`, `eslint`, `prettier` all exit 0 |
| UC-3: contract-diff green | `oasdiff` finds no breaking changes vs previous OpenAPI version |
| UC-4: context fingerprint matches | Embedded in PR description |
| UC-5: no constitution edits outside amendment | This spec does not amend the constitution |
| UC-6: required approvals | 1 code-owner (standard PR) |
| UC-7: commits signed | Enforced by branch protection |
| UC-8: constitution version recorded in spec | `v1.0.0` in spec.md header |

**Active applicability tags**:
- `[audit-event]` — audit-log module is the implementation of Principle 25; audit actions for the module's own write path are self-referential (omitted to avoid infinite recursion; documented as exception).
- `[state-machine]` — not applicable; no stateful domain entities in this spec.
- `[pdf]` — PDF abstraction is established here; tax-invoice stub template exercised.
- `[storage]` — storage abstraction is established here; dev stub exercised.
- `[user-facing-strings]` — localization scaffolding introduces AR strings (ARB files); editorial review by a native Arabic speaker required before launch-readiness.
