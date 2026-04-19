# Research: Shared Foundations

**Branch**: `003-shared-foundations` | **Date**: 2026-04-19
**Depends on**: Spec 001 (CI pipeline), Spec 002 (ERD + testing strategy)

All decisions below are fully resolved. No NEEDS CLARIFICATION items remain.

---

## Decision 1 — Contracts package generation toolchain

**Decision**: Three generators, one per consumer platform:
- **.NET C# client**: Kiota v1.x (Microsoft's official OpenAPI client generator). Generates strongly-typed request builders and model classes from the OpenAPI spec artifact emitted by the CI pipeline (spec 001).
- **Dart / Flutter**: `openapi-generator` with the `dart-dio` template. Generates type-safe API classes and models consumable by the Flutter apps.
- **TypeScript / Next.js**: `openapi-typescript` (types-only, zero runtime dependency). Generates TypeScript interfaces from the same OpenAPI spec.

**Rationale**: Each platform has a native-quality generator. Using platform-native output avoids runtime adapter layers and keeps the generated code idiomatic. All three generators consume the same source artifact (the OpenAPI JSON/YAML emitted by CI), so they stay in sync automatically.

**Alternatives considered**:
- NSwag for .NET — viable but Kiota is Microsoft's strategic direction and supports more OpenAPI 3.1 features.
- A single polyglot generator — no mature tool covers .NET + Dart + TS idiomatically in one pass.

**Versioning**: CI script reads `info.version` from the OpenAPI spec and publishes packages at that version. Breaking changes (removed/renamed fields) → major bump; additive changes → minor bump; description/example changes → patch bump.

---

## Decision 2 — Design system package structure

**Decision**: Two independent artifacts:
- **Flutter package** (`packages/design_system`): Exports `AppColors`, `AppTypography`, `AppSpacing`, `AppTheme`. RTL is handled via Flutter's `TextDirection`, `Directionality` widget, and `EdgeInsetsDirectional` — no custom RTL engine needed. Color values map 1:1 to Constitution Principle 7 palette.
- **CSS custom properties file** (`packages/design_system/tokens.css`): Exports the same color and spacing tokens as CSS variables for consumption by the Next.js admin. Tailwind config can extend from this file.

**Rationale**: Flutter and Next.js use incompatible token systems. A single-source-of-truth is maintained in the Flutter package (authoritative values); the CSS file is generated from the same constants file so drift is impossible. No third-party design-token pipeline (Style Dictionary, Theo) is introduced — the monorepo layout makes a simple script sufficient.

**Alternatives considered**:
- Style Dictionary for cross-platform tokens — adds tooling complexity without proportional benefit at Phase 1A scale.
- Figma tokens — Figma is not a source of truth for the token values; the Constitution is.

---

## Decision 3 — ICU localization scaffolding per platform

**Decision**:
- **Flutter**: `flutter_localizations` + `intl` package with ARB (Application Resource Bundle) files. `flutter gen-l10n` generates strongly-typed message accessors (`AppLocalizations`). Editorial-review flag stored as ARB metadata: `"@key": { "x-editorial-review": true }`. A CI lint step greps for `x-editorial-review: true` and outputs a report.
- **.NET backend** (email bodies, admin UI strings, notification templates): `Microsoft.Extensions.Localization` with JSON resource files. ICU plural rules via the `MessageFormat.NET` library.

**Rationale**: ARB is the Flutter ecosystem standard; it has first-class tooling (`gen-l10n`, IDE plugins). The `x-editorial-review` metadata field is a non-standard extension allowed by the ARB spec. .NET's built-in localization is sufficient for server-rendered strings.

**Missing-key behavior**: `flutter gen-l10n` in strict mode (`--required-resource-attributes`) treats a missing key as a build error. This satisfies FR-008 (build warning/error, not silent fallback).

**Alternatives considered**:
- Phrase / Lokalise for translation management — deferred to Phase 1.5; not needed for scaffolding.
- A custom key-validation CI script — unnecessary given `--required-resource-attributes` flag.

---

## Decision 4 — Audit-log write pattern

**Decision**: **Synchronous in-process write** to PostgreSQL (not a message bus). The `IAuditEventPublisher` interface has a single `PublishAsync(AuditEvent event)` method that writes directly to `audit_log_entries` and returns only when the write is confirmed. If the write fails (DB unreachable, constraint violation), the exception propagates to the caller, causing the caller's operation to fail — satisfying clarification Q1 (fail-fast guarantee).

**Append-only enforcement** at two layers:
1. **Application layer**: No `Update` or `Delete` endpoints/methods on the audit module.
2. **Database layer**: The PostgreSQL role used by the audit write path has only `INSERT` and `SELECT` grants on `audit_log_entries`. `UPDATE` and `DELETE` are revoked.

**Rationale**: A message bus (RabbitMQ, Azure Service Bus) would decouple the write but would break the fail-fast requirement — the caller would get an ACK from the bus, not from the store. For Phase 1A infrastructure that serves compliance requirements, synchronous write with fail-fast is the correct posture.

**Alternatives considered**:
- Outbox pattern (write to local DB table + background publisher) — adds complexity and doesn't satisfy the "fail if audit unavailable" requirement without additional transaction coupling.
- Domain event bus with guaranteed delivery — deferred to Phase 2 if audit volume demands it.

---

## Decision 5 — Storage abstraction design

**Decision**: `IStorageService` interface with three operations:
- `UploadAsync(Stream content, string fileName, string mimeType, MarketCode market, CancellationToken ct) → StoredFileResult`
- `GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct) → Uri`
- `DeleteAsync(string fileId, CancellationToken ct) → void`

**Virus scan**: Separate `IVirusScanService` interface called by `IStorageService` before persisting. Returns `ScanResult` enum: `Clean | Infected | ServiceUnavailable`. Both `Infected` and `ServiceUnavailable` cause `StorageUploadBlockedException` — satisfying clarification Q2.

**Access control**: The abstraction does not enforce access policy — it generates signed URLs for any caller that reaches it. Calling modules apply authorization before invoking `GetSignedUrlAsync` — satisfying clarification Q3.

**Dev stub**: `LocalDiskStorageService` — writes to `tmp/storage/{market}/` directory. Signed URL returned as `http://localhost:5000/dev-storage/{fileId}`. `LocalVirusScanService` always returns `Clean`.

**Market routing**: `MarketCode` enum (`KSA`, `EG`) is a required upload parameter. The production provider maps it to the correct bucket/container prefix. The dev stub uses it as a subdirectory.

---

## Decision 6 — PDF generation library

**Decision**: **QuestPDF** (v2024.x, MIT license) for .NET.

**Rationale**: QuestPDF supports RTL text direction natively via `TextDirection.RightToLeft`. It embeds custom fonts (embedding Noto Naskh Arabic, OFL license). It is MIT licensed (no royalty, no LGPL linking constraints). It is actively maintained with a .NET 9 compatible NuGet package.

**Arabic font**: Noto Naskh Arabic (Google Fonts, OFL license) — embeddable in commercial PDFs, good Arabic coverage, renders well at invoice body sizes.

**`IPdfService` interface**:
- `RenderAsync(string templateName, string locale, object data, CancellationToken ct) → byte[]`

Templates registered by name as C# classes implementing QuestPDF's `IDocument`. The tax-invoice stub template is the only template bundled at spec 003 completion.

**Dev stub**: `StubPdfService` returns a minimal single-page PDF with the `data` payload serialized as JSON text — sufficient for integration tests to assert that the PDF path is exercised.

**Alternatives considered**:
- iTextSharp — AGPL license; commercial license costly. Rejected.
- PdfSharpCore — no native RTL support. Rejected.
- Puppeteer / headless Chrome — heavy process spawn; poor for server-side embedded rendering. Rejected.

---

## Decision 7 — Observability stack

**Decision**:
- **Structured logging**: Serilog with `Serilog.Sinks.Console` (JSON output format). Enriched with `Serilog.Enrichers.CorrelationId` so the correlation ID appears on every log line automatically.
- **Correlation ID middleware**: `CorrelationIdMiddleware` reads `X-Correlation-Id` request header. If absent, generates `Guid.NewGuid().ToString("D")`. Sets value on the `ILogger` scope and on the `X-Correlation-Id` response header. All downstream `HttpClient` calls within the same scope inject the header via `DelegatingHandler`.
- **Health checks**: ASP.NET Core `IHealthCheck` + `/health` endpoint. Registered checks: DB connectivity (EF Core ping), storage reachability (head request to bucket). Returns `200 OK` (healthy) or `503 Service Unavailable` (degraded/unhealthy).

**Rationale**: Serilog is the de-facto standard for structured .NET logging. The `X-Correlation-Id` convention is widely adopted and compatible with Azure Application Insights, Datadog, and other APM tools. ASP.NET Core health checks are built-in with no additional packages required beyond the `Microsoft.Extensions.Diagnostics.HealthChecks` namespace.

**Alternatives considered**:
- OpenTelemetry for tracing — deferred to Phase 1.5; correlation IDs via Serilog are sufficient for Phase 1A.
- NLog — no advantage over Serilog for this stack.

---

## Decision 8 — Internal package feed

**Decision**: **GitHub Packages** for all three package types (NuGet, pub.dev-compatible, npm). Publishing is automated by the CI pipeline on every merge to main that changes the contracts or design-system packages.

**Rationale**: GitHub Packages is co-located with the monorepo, requires no external service, and supports NuGet, npm, and custom registry protocols. Authentication uses the existing `GITHUB_TOKEN` in CI — no additional secrets needed.

**Alternatives considered**:
- Azure Artifacts — viable but requires an additional Azure DevOps organization setup not yet established.
- Verdaccio (self-hosted npm proxy) — unnecessary given GitHub Packages supports npm.
