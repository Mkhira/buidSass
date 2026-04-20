# Shared Foundations — Interface Contracts

**Version**: 1.0 | **Date**: 2026-04-19

This document defines the interface contracts that every downstream Phase-1 module MUST use to consume shared-foundations services. Any change to these interfaces is a breaking change requiring a major version bump and downstream impact review.

---

## Contract: IAuditEventPublisher

Every module that performs a write mutation MUST call this interface before returning a success response.

### Method

```
PublishAsync(AuditEvent event, CancellationToken ct) → Task
```

### AuditEvent fields (all required unless marked optional)

| Field | Type | Required | Constraint |
|-------|------|----------|------------|
| actor_id | UUID | Yes | ID of the authenticated actor |
| actor_role | string | Yes | Role name from the permissions matrix |
| action | string | Yes | Format: `{domain}.{entity}.{verb}` (e.g., `catalog.product.created`) |
| entity_type | string | Yes | PascalCase entity name (e.g., `Product`) |
| entity_id | UUID | Yes | ID of the affected entity |
| before_state | object? | No | Null for create events |
| after_state | object? | No | Null for delete events |
| reason | string? | No | Human-readable reason |

### Behavioral contract

| Rule | Verifiable by |
|------|--------------|
| `PublishAsync` throws on store failure — callers MUST NOT catch and suppress the exception | Integration test: bring DB down, assert caller returns 5xx |
| Correlation ID is NOT a caller field — the module reads it from ambient request scope | Code review: no `correlationId` param on `AuditEvent` |
| `before_state` MUST reflect the entity state immediately before the mutation | Integration test: assert before ≠ after on update |
| Calling `PublishAsync` with missing required fields throws `ArgumentException` before any DB write | Unit test |

---

## Contract: IStorageService

Modules handling file uploads, signed URL generation, or file deletion MUST use this interface.

### Methods

```
UploadAsync(Stream content, string fileName, string mimeType, MarketCode market, CancellationToken ct)
  → Task<StoredFileResult>

GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct)
  → Task<Uri>

DeleteAsync(string fileId, CancellationToken ct)
  → Task
```

### StoredFileResult fields

| Field | Type | Notes |
|-------|------|-------|
| file_id | UUID | Stable identifier for subsequent calls |
| signed_url | Uri | Direct-access URL valid for a default TTL |
| market | MarketCode | Echo of the upload market |

### Behavioral contract

| Rule | Verifiable by |
|------|--------------|
| A file upload where `IVirusScanService` returns `Infected` or `ServiceUnavailable` MUST throw `StorageUploadBlockedException`; no `StoredFile` record is created | Integration test |
| `GetSignedUrlAsync` does NOT check actor authorization — caller is responsible for prior authz | Code review: no role check inside implementation |
| `DeleteAsync` on a non-existent `fileId` MUST throw `FileNotFoundException` (idempotent delete not supported) | Unit test |
| Files uploaded with `market = KSA` MUST be stored in the KSA-designated bucket; same for EG | Integration test (dev stub: assert subdirectory) |
| Signed URLs expire after the requested `expiry` duration ± 5 seconds | Integration test |

---

## Contract: IVirusScanService

Called internally by `IStorageService` before persisting any upload. Downstream modules MUST NOT call this directly — they interact with `IStorageService` only.

### Method

```
ScanAsync(Stream content, CancellationToken ct) → Task<ScanResult>
```

### ScanResult enum

| Value | Meaning |
|-------|---------|
| `Clean` | File is safe to persist |
| `Infected` | Threat detected; upload MUST be blocked |
| `ServiceUnavailable` | Scanner unreachable; upload MUST be blocked |

### Behavioral contract

| Rule | Verifiable by |
|------|--------------|
| Any exception from the scanner service is caught internally and returned as `ServiceUnavailable` — never rethrown | Unit test: mock scanner throws, assert `ServiceUnavailable` returned |
| `ScanAsync` MUST NOT modify the stream position — callers may reuse the stream | Unit test: assert stream position unchanged |

---

## Contract: IPdfService

Modules generating PDFs (invoices, confirmations) MUST use this interface.

### Method

```
RenderAsync(string templateName, LocaleCode locale, object data, CancellationToken ct)
  → Task<byte[]>
```

### Behavioral contract

| Rule | Verifiable by |
|------|--------------|
| `templateName` MUST match a registered template; unrecognized names throw `TemplateNotFoundException` | Unit test |
| `locale = AR` produces a PDF with RTL text direction | Integration test: assert PDF metadata direction |
| `locale = EN` produces a PDF with LTR text direction | Integration test |
| Arabic-locale PDFs embed an Arabic-capable font | Integration test: assert embedded font names |
| `data` is validated against the template's expected schema; missing required fields throw `TemplateDataException` | Unit test |
| The `tax-invoice` template MUST be registered and renderable from a minimal data payload at spec 003 completion | Integration test (acceptance gate) |

---

## Contract: Localization scaffolding (build-time)

Not a runtime interface — a source-level convention enforced at build time.

### Rules for module authors adding user-visible strings

| Rule | Enforcement |
|------|------------|
| All user-visible strings MUST use a localization key from `AppLocalizations` — never a hardcoded literal | `flutter gen-l10n` lint + custom Dart analyzer rule |
| Every key added to `app_en.arb` MUST have a corresponding entry in `app_ar.arb` | `flutter gen-l10n --required-resource-attributes` build step |
| AR strings requiring human editorial review MUST set `"x-editorial-review": true` in ARB metadata | Convention; enforced by CI report step |
| The editorial review report MUST list all flagged keys grouped by module | CI step: `grep -r "x-editorial-review": true` + structured output |

---

## Contract: Packages version compatibility

| Package | Versioning scheme | Breaking change trigger |
|---------|------------------|------------------------|
| `shared_contracts` (.NET) | Semver from OpenAPI `info.version` | Removed field, renamed field, changed type |
| `shared_contracts` (Dart) | Semver from OpenAPI `info.version` | Same as above |
| `shared_contracts` (TypeScript) | Semver from OpenAPI `info.version` | Same as above |
| `design_system` (Flutter) | Semver manual | Removed token, renamed token, palette change |

A major version bump on `shared_contracts` MUST be accompanied by a migration note in the PR description listing all breaking fields and their replacements.
