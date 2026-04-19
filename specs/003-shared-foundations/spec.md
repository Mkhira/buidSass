# Feature Specification: Shared Foundations

**Spec**: 003 | **Version**: 1.0.0 | **Date**: 2026-04-19
**Constitution version**: v1.0.0
**Depends on**: Spec 001 (governance-and-setup), Spec 002 (architecture-and-contracts)
**Phase**: 1A — Foundation

## Overview

Establish the shared infrastructure that every Phase-1 domain module will consume: a generated shared-contracts package, a design-system package with RTL-aware tokens, ICU localization scaffolding for Arabic and English, a central audit-log module, provider-agnostic storage and PDF abstractions, and baseline observability (health-check, structured logging, correlation IDs).

No module beyond spec 003 should roll its own audit logic, file-upload handling, PDF rendering, or localization key storage. This spec creates those shared surfaces and verifies each with a working stub or integration test before any downstream module spec begins.

## Clarifications

### Session 2026-04-19

- Q: When the audit store is unreachable, should the calling module's operation fail or proceed? → A: Fail the calling operation — no mutation proceeds without a confirmed audit write.
- Q: If the virus-scan service is unavailable, should the upload be rejected or allowed through? → A: Reject — scan-unavailable treated identically to threat detection.
- Q: Who is authorized to generate a signed URL — does the abstraction enforce access or does the caller? → A: Caller enforces access; the abstraction generates a signed URL for any caller that reaches it without its own policy check.
- Q: Which roles can read audit log entries? → A: Admin Read-only and above (AR, AW, AS); all other roles denied.
- Q: How should the contracts package be versioned? → A: Semver derived from the API version — breaking changes bump major, additive bump minor, fixes bump patch.

## User Stories

### US-1 (P1): Shared API contracts package

**As a** developer building the mobile app or admin web panel,
**I need** a contracts package that is automatically generated from the canonical API definition and published to an internal feed,
**so that** I always consume correct, up-to-date request and response types without manually copying or maintaining type definitions.

**Acceptance scenarios**:
- Given the backend API definition has changed, when the CI pipeline runs, then a new version of the contracts package is published within 5 minutes.
- Given a Flutter developer adds the contracts package, when they reference an API type, then the compiler confirms the type matches the current API without any manual sync step.
- Given the contracts package version is pinned in the admin web app, when the API definition changes, then the package version is bumped and the admin app's build warns of the mismatch.

### US-2 (P1): Design system with RTL-aware tokens

**As a** UI developer building screens for both Arabic (RTL) and English (LTR) layouts,
**I need** a design-system package that exposes named tokens for color, typography, and spacing, and that applies RTL mirroring automatically,
**so that** every screen in both markets renders consistently without per-screen directional overrides.

**Acceptance scenarios**:
- Given a developer uses a color token from the design system, when the palette changes in the package, then all screens that use the token update without any per-screen change.
- Given a screen is rendered in Arabic locale, when RTL mirroring rules apply, then directional layout elements (icons, padding, leading/trailing) are mirrored automatically.
- Given a designer adds a new spacing token following the Constitution's palette, when the design-system package is published, then the token is immediately available to all consuming apps.

### US-3 (P1): Localization scaffolding — AR + EN

**As a** developer adding any user-visible string,
**I need** an ICU-based localization scaffolding with Arabic and English resource files and a build-time check for missing keys,
**so that** no hardcoded string ever ships to a user and Arabic translations that need editorial review are explicitly flagged.

**Acceptance scenarios**:
- Given a developer writes a user-visible string without a localization key, when the build runs, then a warning (not a silent pass) is emitted.
- Given an AR string is marked as needing editorial review, when a reviewer runs the localization report, then all flagged strings appear in a single list.
- Given a key exists in EN but is missing from AR, when the build runs, then the missing key is reported; the app does not silently fall back to EN.

### US-4 (P1): Central audit-log module

**As a** module author (identity, catalog, orders, etc.),
**I need** a central audit-log service that accepts domain events — actor, action, entity type, entity ID, before-state, after-state, and correlation ID — and writes them to an append-only store,
**so that** every module can produce a consistent audit trail without implementing its own audit logic.

**Acceptance scenarios**:
- Given a module publishes an audit event, when the audit-log module receives it, then the event is persisted with all required fields (actor, action, entity, before, after, correlation ID, timestamp).
- Given an audit record has been written, when anyone attempts to update or delete it, then the operation is rejected with an error.
- Given a new module needs audit capability, when a developer integrates the audit-log module, then they can emit a complete audit event with five lines of code or fewer.

### US-5 (P1): Storage and PDF abstractions with dev stubs

**As a** module author handling file uploads or document generation,
**I need** provider-agnostic storage and PDF abstractions with working dev stubs,
**so that** I can upload files, obtain signed URLs, and generate bilingual PDFs during development, with the production providers swapped in later without changing any calling code.

**Acceptance scenarios**:
- Given a module calls the storage abstraction to upload a file, when the operation succeeds, then a signed URL is returned that allows direct access to the file.
- Given a file upload is intercepted by the virus-scan hook, when the scan returns a threat, then the file is rejected and no record is persisted.
- Given a module calls the storage abstraction for a KSA tenant request, when the file is stored, then it is routed to the KSA-designated bucket (not the EG bucket).
- Given the PDF abstraction is called with a template name, locale, and data payload, then a bilingual document with correct RTL layout is returned for AR and correct LTR layout for EN.
- Given the tax-invoice stub template exists, when the PDF abstraction renders it, then a complete PDF is produced from a minimal data payload.

### US-6 (P2): Baseline observability

**As an** operator or on-call engineer,
**I need** a health-check endpoint, structured log lines carrying a correlation ID, and correlation-ID propagation from inbound request to all downstream calls within the same scope,
**so that** I can confirm the system is operational at a glance and trace any request end-to-end through logs.

**Acceptance scenarios**:
- Given the system is running normally, when the health-check endpoint is called, then a success response is returned within 500ms.
- Given any inbound request has a correlation-ID header, when the request is processed, then every log line emitted during that request carries the same correlation ID.
- Given a developer inspects the log output, when they search by correlation ID, then all log lines for that request (including downstream calls) are returned.

## Functional Requirements

### Contracts package
- **FR-001**: The shared-contracts package is auto-generated from the canonical API definition on every successful merge to main. No manual step is required to update it. The package uses semantic versioning derived from the API version: breaking changes bump the major version, additive changes bump minor, fixes bump patch.
- **FR-002**: The shared-contracts package is published to an internal package feed and is consumable by the Flutter mobile app and the Next.js admin panel without manual type copying.
- **FR-003**: A version mismatch between a consumer's pinned package version and the latest published version produces a build-time warning in the consuming project.

### Design system
- **FR-004**: The design-system package exposes named tokens for colors, typography, and spacing that map directly to the palette defined in Constitution Principle 7. No magic values are allowed in consuming code.
- **FR-005**: The design-system package includes RTL mirroring rules such that directional layout properties (start/end alignment, leading/trailing icons, bidirectional padding) are applied automatically when the active locale is RTL.
- **FR-006**: Adding a new token to the design-system package and publishing it makes the token immediately available to all consuming apps without any per-app configuration change.

### Localization
- **FR-007**: All user-visible strings are referenced by a message key using ICU message format. Hardcoded user-visible strings cause a build warning.
- **FR-008**: Both AR and EN resource files are present at all times. A key present in EN but absent in AR produces a build warning; the system does not silently fall back.
- **FR-009**: AR strings that require human editorial review before going to production are marked with an `editorial_review_needed` flag. A report command or CI step surfaces all flagged strings in one list.

### Audit-log module
- **FR-010**: The audit-log module accepts events containing: actor ID, actor role, action name, entity type, entity ID, before-state (serializable), after-state (serializable), and correlation ID. All fields are required.
- **FR-011**: Audit events are written to an append-only store. Update and delete operations on audit records are rejected at the service boundary. If the audit store is unreachable when a module attempts to emit an event, the calling operation MUST fail — no mutation proceeds without a confirmed audit write.
- **FR-012**: The audit-log module is callable with five lines of calling code or fewer for a standard single-entity mutation event.
- **FR-012a**: Audit log entries are readable by Admin Read-only (AR), Admin Write (AW), and Admin Super (AS) roles. All other roles — including customers, professionals, and B2B roles — cannot read audit records.

### Storage abstraction
- **FR-013**: The storage abstraction exposes three operations: upload file, get signed URL for an existing file, delete file. The underlying provider is swappable without changing any caller. The abstraction does not enforce its own access policy — each calling module is responsible for verifying the actor's authorization before invoking `getSignedUrl`.
- **FR-014**: The storage abstraction accepts a market context (KSA or EG) and routes files to the correct market-designated bucket automatically.
- **FR-015**: File uploads pass through a virus-scan hook before being persisted. A scan result indicating a threat causes the upload to be rejected and no file record to be created. If the virus-scan service is unreachable (timeout or error), the upload is also rejected — scan-unavailable is treated identically to a threat detection.

### PDF abstraction
- **FR-016**: The PDF abstraction accepts a template name, a locale (AR or EN), and a structured data payload. It returns a rendered document. Callers do not specify layout instructions.
- **FR-017**: AR-locale documents use RTL text direction and embed Arabic-capable fonts. EN-locale documents use LTR.
- **FR-018**: A tax-invoice stub template is bundled with the abstraction so the PDF rendering path is exercised by an integration test at spec 003 completion.

### Observability
- **FR-019**: A health-check endpoint is present. It returns a success status when the system is operating normally and a non-success status when a critical dependency is unavailable.
- **FR-020**: All log output is structured and machine-parseable. Every log line carries the correlation ID of the active request.
- **FR-021**: The correlation ID is extracted from the inbound request header (or generated if absent) and propagated through all downstream calls within the same request scope without manual threading by the caller.

## Success Criteria

### Measurable outcomes
- **SC-001**: Any new module can publish a complete audit event (actor, action, entity, before, after, correlation ID) with five lines of calling code or fewer — verified by the spec 003 integration test.
- **SC-002**: A new version of the shared-contracts package is available to consumers within 5 minutes of an API definition change merging to main.
- **SC-003**: Switching the storage provider (e.g., from dev stub to cloud storage) requires changes only inside the abstraction layer; zero changes in any calling module.
- **SC-004**: A developer who writes a hardcoded user-visible string receives a build warning before any code merges to main.
- **SC-005**: All AR strings marked `editorial_review_needed` are surfaced in a single, actionable report without manual file scanning.
- **SC-006**: The PDF abstraction produces a complete, bilingual AR + EN document with correct RTL layout given only a template name and data payload.
- **SC-007**: The health-check endpoint responds within 500ms under normal load.
- **SC-008**: Every inbound request is traceable end-to-end using its correlation ID in structured log output.

## Key Entities

| Entity | Core attributes |
|--------|----------------|
| AuditLogEntry | id, actor_id, actor_role, action, entity_type, entity_id, before_state (JSON), after_state (JSON), correlation_id, occurred_at |
| LocalizationMessage | key, locale (AR/EN), value, editorial_review_needed (bool), last_updated |
| DesignToken | name, value, category (color/typography/spacing), rtl_mirror (bool) |
| StoredFile | id, bucket_key, market (KSA/EG), size_bytes, mime_type, virus_scan_status, signed_url_expiry, uploaded_at |
| PdfTemplate | name, supported_locales, layout_direction, last_updated |

## Assumptions

1. Spec 001 CI pipeline emits the canonical OpenAPI definition on every merge — the contracts-package generation step consumes that artifact.
2. Spec 002 architecture artifacts (ERD, permissions matrix, testing strategy) are merged before any coding on spec 003 begins.
3. Constitution Principle 7 defines the exact color palette; design tokens must map 1:1 and cannot add off-palette colors.
4. Dev stubs for storage (local disk) and PDF (simple renderer) are sufficient to pass spec 003 integration tests; production providers are wired in Stage 7.
5. The virus-scan hook in the storage abstraction calls an external scanner interface; the dev stub always returns clean.
6. Correlation IDs are UUIDs; if absent on an inbound request, the system generates one and returns it in the response header.
7. The internal package feed is a repository-local mechanism (e.g., GitHub Packages or equivalent); external publication is out of scope.

## Out of Scope

- Production storage provider configuration — deferred to Stage 7 (specs 025–026).
- Production PDF renderer — deferred to Stage 7.
- Notification delivery (SMS, email, push, WhatsApp) — spec 027 / Phase 1.5.
- Full ZATCA / ETA tax-invoice compliance fields — spec 011 (tax/invoices).
- User-facing localization UI (language switcher, user preference persistence) — spec 014 (customer-app-shell).
- Admin localization management UI — spec 019 (admin-panel-shell).

## Dependencies

| Dependency | Type | Notes |
|-----------|------|-------|
| Spec 001 — governance-and-setup | Hard | CI pipeline and CODEOWNERS must be live before spec 003 merges |
| Spec 002 — architecture-and-contracts | Hard | ERD and testing strategy must be merged; audit-log entity from ERD is authoritative |
| Constitution Principle 7 | Hard | Color palette is fixed; design tokens are derived, not invented |
| Principle 25 (audit) | Hard | Audit-log schema and non-deletability rules are constitutionally mandated |
