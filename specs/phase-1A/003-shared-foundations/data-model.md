# Data Model: Shared Foundations

**Branch**: `003-shared-foundations` | **Date**: 2026-04-19

This spec produces shared infrastructure artifacts — packages, modules, and abstractions — rather than end-user domain data. The persistent entities below are owned by this spec and consumed by every downstream Phase-1 module.

---

## Entity: AuditLogEntry

Owned by the central audit-log module. Append-only — no `UPDATE` or `DELETE` permitted at application or database layer.

| Field | Type | Nullable | Constraint |
|-------|------|----------|------------|
| id | UUID | No | PK, generated |
| actor_id | UUID | No | FK → users(id) at write time; stored as snapshot |
| actor_role | VARCHAR(50) | No | Role name at time of action (e.g., `admin_write`, `customer`) |
| action | VARCHAR(100) | No | Named action (e.g., `catalog.product.created`) |
| entity_type | VARCHAR(100) | No | Entity class name (e.g., `Product`, `Order`) |
| entity_id | UUID | No | ID of the affected entity |
| before_state | JSONB | Yes | Serialized entity state before mutation; NULL for create events |
| after_state | JSONB | Yes | Serialized entity state after mutation; NULL for delete events |
| correlation_id | UUID | No | Propagated from inbound request |
| reason | TEXT | Yes | Human-readable reason supplied by actor (optional but encouraged) |
| occurred_at | TIMESTAMPTZ | No | Server-set at write time; cannot be supplied by caller |

**Indexes**: `(entity_type, entity_id)`, `(actor_id)`, `(correlation_id)`, `(occurred_at DESC)`

**Ownership**: Non-ownable entity (no `vendor_id` FK — consistent with Constitution Principle 25 and spec 002 ERD contract). Audit records belong to the platform, not a vendor.

**DB-level enforcement**: The PostgreSQL role used by the audit-log write path has only `INSERT` and `SELECT` grants on this table. `UPDATE` and `DELETE` grants are explicitly revoked.

---

## Value Object: AuditEvent

Passed by callers to `IAuditEventPublisher`. Not persisted directly — mapped to `AuditLogEntry` by the module.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| actor_id | UUID | Yes | |
| actor_role | string | Yes | |
| action | string | Yes | Dot-notation: `{domain}.{entity}.{verb}` |
| entity_type | string | Yes | |
| entity_id | UUID | Yes | |
| before_state | object? | No | Nullable for create events |
| after_state | object? | No | Nullable for delete events |
| reason | string? | No | |

Correlation ID is NOT a caller-supplied field — the module reads it from the ambient request scope (set by `CorrelationIdMiddleware`).

---

## Entity: StoredFile

Owned by the storage abstraction module. Records every successfully scanned and persisted file.

| Field | Type | Nullable | Constraint |
|-------|------|----------|------------|
| id | UUID | No | PK, generated |
| bucket_key | VARCHAR(500) | No | Provider-specific path/key |
| market | VARCHAR(10) | No | Enum: `KSA`, `EG` |
| original_filename | VARCHAR(255) | Yes | Caller-supplied display name |
| size_bytes | BIGINT | No | |
| mime_type | VARCHAR(100) | No | |
| virus_scan_status | VARCHAR(20) | No | Enum: `clean`, `infected`, `scan_unavailable` — only `clean` records are persisted |
| uploaded_by_actor_id | UUID | Yes | Actor who triggered the upload |
| uploaded_at | TIMESTAMPTZ | No | Server-set |
| expires_at | TIMESTAMPTZ | Yes | NULL = no expiry |

**Notes**:
- `virus_scan_status` on persisted records is always `clean` (infected and unavailable uploads are rejected before record creation per FR-015).
- The `expires_at` field supports temporary file scenarios (e.g., verification documents with retention periods).
- No `vendor_id` FK — storage is platform-level infrastructure, not a vendor-owned entity.

---

## Entity: PdfTemplate

Registry of available PDF templates. Populated at application startup from registered template classes; not user-editable at runtime.

| Field | Type | Nullable | Constraint |
|-------|------|----------|------------|
| name | VARCHAR(100) | No | PK; kebab-case identifier (e.g., `tax-invoice`) |
| supported_locales | VARCHAR[] | No | e.g., `{AR, EN}` |
| description | TEXT | Yes | |
| last_updated | TIMESTAMPTZ | No | Set on deploy |

**Note**: This is a read-only registry used for validation. The templates themselves are C# classes — not database rows.

---

## Value Objects and Enums

### MarketCode
```
KSA | EG
```
Required on file uploads to route to the correct storage bucket. Propagated from the inbound request's market context header/claim.

### ScanResult
```
Clean | Infected | ServiceUnavailable
```
Returned by `IVirusScanService`. Both `Infected` and `ServiceUnavailable` result in upload rejection.

### LocaleCode
```
AR | EN
```
Used by the PDF abstraction and localization module. Maps to ICU locale identifiers `ar-EG` / `ar-SA` and `en-US` / `en-GB` at rendering time.

---

## Package Artifact: LocalizationMessage (ARB schema)

ARB files are not database entities — they are source-controlled files in `packages/design_system/lib/l10n/`. Schema per key:

```json
{
  "key": "string (camelCase message key)",
  "value": "string (ICU message format)",
  "@key": {
    "description": "string",
    "x-editorial-review": "boolean (true = needs human AR review)"
  }
}
```

**Locales**: `app_ar.arb` and `app_en.arb` must always be in sync. A key present in `app_en.arb` and absent in `app_ar.arb` is a build error (`flutter gen-l10n --required-resource-attributes`).

---

## Package Artifact: DesignToken (conceptual schema)

Tokens are defined as Dart constants in `packages/design_system/lib/src/tokens/`. Each token has:

| Attribute | Value |
|-----------|-------|
| name | Dart constant name (e.g., `AppColors.primary`) |
| value | Hex color string / `double` / `TextStyle` |
| category | `color` / `typography` / `spacing` |
| rtl_mirror | `bool` — true if spacing/alignment token has a mirrored counterpart |

**Constitution Principle 7 palette** (authoritative, no deviation permitted):

| Token | Value |
|-------|-------|
| AppColors.primary | `#1F6F5F` |
| AppColors.secondary | `#2FA084` |
| AppColors.accent | `#6FCF97` |
| AppColors.neutral | `#EEEEEE` |

---

## Module Interfaces (service layer)

### IAuditEventPublisher

```
PublishAsync(AuditEvent event, CancellationToken ct) → Task
```
Throws on failure — callers must let the exception propagate (fail-fast per clarification Q1).

### IStorageService

```
UploadAsync(Stream content, string fileName, string mimeType, MarketCode market, CancellationToken ct) → Task<StoredFileResult>
GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct) → Task<Uri>
DeleteAsync(string fileId, CancellationToken ct) → Task
```

### IVirusScanService

```
ScanAsync(Stream content, CancellationToken ct) → Task<ScanResult>
```
Must not swallow exceptions — `ServiceUnavailable` is returned on any connectivity failure, not thrown (so `IStorageService` can handle it uniformly).

### IPdfService

```
RenderAsync(string templateName, LocaleCode locale, object data, CancellationToken ct) → Task<byte[]>
```
Throws `TemplateNotFoundException` if `templateName` is not registered.

### IHealthCheckService (built-in ASP.NET Core)

Registered checks: `db-connectivity`, `storage-reachability`. Exposed at `GET /health`.
