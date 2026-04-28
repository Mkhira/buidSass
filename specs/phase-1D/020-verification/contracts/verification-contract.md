# HTTP & Cross-Module Contract: Professional Verification (Spec 020)

**Date**: 2026-04-28
**Spec**: [../spec.md](../spec.md) · **Plan**: [../plan.md](../plan.md) · **Data model**: [../data-model.md](../data-model.md)

This file is the source of truth for every endpoint, request body, response body, error reason code, and cross-module interface that spec 020 publishes. The OpenAPI artifact `services/backend_api/openapi.verification.json` is regenerated from the handler signatures and asserted in CI to match this document (Guardrail #2).

---

## 1. Conventions

- **Base paths**: customer endpoints under `/api/customer/verifications`; admin endpoints under `/api/admin/verifications`.
- **Authentication**: customer endpoints require a customer JWT (per spec 004); admin endpoints require an admin JWT + the named permission.
- **Idempotency**: every state-transitioning POST requires `Idempotency-Key: <uuid>` (per spec 003 platform middleware).
- **Locale**: every response that includes user-facing strings is rendered in the locale resolved from `Accept-Language` (Arabic mirrored to RTL by client).
- **Error envelope** (platform-wide):
  ```json
  {
    "type": "https://errors.dental/verification.{reason_code}",
    "title": "<localized title>",
    "status": <http_status>,
    "detail": "<localized detail>",
    "reason_code": "<stable enum value>",
    "trace_id": "<correlation id>"
  }
  ```
- **Time**: every timestamp is ISO-8601 UTC.
- **Money / numbers**: not applicable in this spec.

---

## 2. Customer endpoints

### 2.1 `POST /api/customer/verifications` — submit verification

**Permission**: any authenticated customer.

**Request body** (multipart/form-data; documents uploaded out-of-band first via 2.5, then referenced here):
```json
{
  "profession": "dentist",
  "regulator_identifier": "1234567",
  "document_ids": ["3f6f...", "9a2c..."]
}
```

**Validation**:
- `profession` must be in the active `verification_market_schemas.required_fields[profession].values` for the customer's market.
- `regulator_identifier` must satisfy the active schema's regex.
- `document_ids` count between 1 and 5; all referenced documents must belong to this customer (spec 020-scoped uploads), have `scan_status='clean'`, and have aggregate size ≤ 25 MB.
- Customer must not already have a non-terminal verification (unless this is a renewal — see 2.7).
- If the customer is in `rejected` state cool-down, the cool-down clock must have elapsed (FR-008, FR-037).

**Success — 201 Created**:
```json
{
  "id": "01HX...",
  "state": "submitted",
  "submitted_at": "2026-04-28T13:45:00Z",
  "expires_at": null,
  "schema_version": 3,
  "market_code": "ksa",
  "profession": "dentist"
}
```

**Errors**:
| HTTP | `reason_code` | When |
|---|---|---|
| 400 | `verification.required_field_missing` | A required field for the customer's market is missing. |
| 400 | `verification.regulator_identifier_invalid` | Pattern violation. |
| 400 | `verification.documents_invalid` | Count, size, type, or scan_status violation. |
| 409 | `verification.already_pending` | Non-terminal verification exists; renewal endpoint required. |
| 409 | `verification.cooldown_active` | Cool-down clock has not elapsed. Body includes `unblocks_at`. |
| 422 | `verification.account_inactive` | Customer account is locked or deleted. |

### 2.2 `GET /api/customer/verifications/active` — get active verification

**Permission**: any authenticated customer.

**Returns the most recent non-terminal verification, OR the most recent `approved` row if no non-terminal exists.**

**Success — 200**:
```json
{
  "id": "01HX...",
  "state": "approved",
  "submitted_at": "2026-04-15T10:00:00Z",
  "decided_at": "2026-04-16T14:23:00Z",
  "expires_at": "2027-04-16T14:23:00Z",
  "schema_version": 3,
  "market_code": "ksa",
  "profession": "dentist",
  "renewal_open": false,
  "next_action": null
}
```

**Empty case — 200 with `null` body**:
```json
null
```

`renewal_open` is `true` when `now >= expires_at - max(reminder_windows_days)`. `next_action` is one of:
- `null` — nothing required from customer.
- `"upload_documents"` — when in `info-requested` and reviewer's reason references document upload.
- `"resubmit"` — when in `info-requested`.
- `"renew_now"` — when active and `renewal_open=true`.

### 2.3 `GET /api/customer/verifications` — list my verifications

**Permission**: any authenticated customer.
**Query**: `?state=<csv>&page=<n>&page_size=<n≤50>`.
**Returns**: paginated list of the customer's own verifications, newest first. Documents are not embedded; use 2.4 to fetch.

### 2.4 `GET /api/customer/verifications/{id}` — get my verification detail

**Permission**: any authenticated customer; resource owner check.

**Success — 200**:
```json
{
  "id": "01HX...",
  "state": "info-requested",
  "submitted_at": "...",
  "schema_version": 3,
  "market_code": "ksa",
  "profession": "dentist",
  "regulator_identifier": "1234567",
  "documents": [
    { "id": "3f6f...", "content_type": "application/pdf", "uploaded_at": "...", "purged": false }
  ],
  "transitions": [
    { "prior_state": "__none__", "new_state": "submitted", "actor_kind": "customer", "occurred_at": "...", "reason": "Initial submission." },
    { "prior_state": "submitted", "new_state": "in-review", "actor_kind": "reviewer", "occurred_at": "...", "reason": "Begin review." },
    { "prior_state": "in-review", "new_state": "info-requested", "actor_kind": "reviewer", "occurred_at": "...", "reason": "License image is unreadable." }
  ],
  "next_action": "upload_documents",
  "cooldown_until": null
}
```

**Errors**:
| HTTP | `reason_code` | When |
|---|---|---|
| 404 | `verification.not_found` | Wrong id or not owned by caller. |

### 2.5 `POST /api/customer/verifications/{id}/documents` — attach document

**Permission**: customer; resource owner; verification state IN (`submitted`, `info-requested`).

**Request**: multipart/form-data, single file part `file`. Server enforces content-type allowlist and 10 MB max; AV scan runs synchronously (max ~10 s) before returning.

**Success — 201**:
```json
{ "id": "9a2c...", "content_type": "image/png", "size_bytes": 2451200, "scan_status": "clean", "uploaded_at": "..." }
```

**Errors**:
| HTTP | `reason_code` | When |
|---|---|---|
| 400 | `verification.document_too_large` | > 10 MB. |
| 400 | `verification.document_type_not_allowed` | Content-type not in allowlist. |
| 422 | `verification.document_aggregate_exceeded` | Adding this would exceed 25 MB or 5 documents. |
| 422 | `verification.document_scan_failed` | AV scan returned `infected` or `error`. |
| 409 | `verification.invalid_state_for_action` | Verification is not in `submitted` or `info-requested`. |

### 2.6 `POST /api/customer/verifications/{id}/resubmit` — resubmit after info request

**Permission**: customer; resource owner; verification in `info-requested`.

**Request body**:
```json
{ "regulator_identifier": "1234567" }   // optional updated value
```

**Behavior**: state → `in-review` (FR's "back to in-review, not submitted"); preserves original `submitted_at`. `decided_at` and `decided_by` reset to null until the next decision.

**Errors**: `409 verification.invalid_state_for_action`, `400 verification.no_changes_provided` (no field updates and no documents added since the info-request).

### 2.7 `POST /api/customer/verifications/renew` — request renewal

**Permission**: customer; must hold an active `approved` verification with `renewal_open=true`.

**Request body**: same as 2.1.

**Behavior**: creates a new `verifications` row with `supersedes_id` set to the prior approval's id. The prior approval **stays** `approved`; eligibility query continues to return `Eligible` until the renewal commits.

**Errors**:
| HTTP | `reason_code` | When |
|---|---|---|
| 409 | `verification.renewal_window_not_open` | Earliest reminder window has not yet been entered. |
| 409 | `verification.no_active_approval` | No approved verification to renew. |
| 409 | `verification.renewal_already_pending` | A non-terminal renewal already exists for this approval. |

---

## 3. Admin endpoints

### 3.1 `GET /api/admin/verifications` — queue

**Permission**: `verification.review`.
**Query**: `?market=<code>&state=<csv>&profession=<csv>&age_min_business_days=<n>&search=<regulator_identifier>&sort=oldest|newest&page=<n>&page_size=<n≤100>`.

Default filter: `state IN (submitted, in-review, info-requested)` AND market in reviewer's assigned markets. Default sort: oldest-first.

**Success — 200**: paginated list with per-row summary `{ id, state, market_code, profession, submitted_at, sla_signal: ('ok'|'warning'|'breach'), age_business_days }`.

### 3.2 `GET /api/admin/verifications/{id}` — submission detail

**Permission**: `verification.review`.

**Returns** full `Verification` + every document's metadata + every transition + the `restriction_policy_snapshot` + the `schema_version` definition that was applied at submission. Document bodies are NOT embedded; reviewer fetches via 3.7.

**Customer locale block** (FR-033): the response includes `customer_locale: 'en' | 'ar'`, resolved from spec 004 identity, so the reviewer UI can render which locale the customer will read.

**Regulator-assist block** (FR-016b): if `IRegulatorAssistLookup.LookupAsync` returns non-null, the response includes a `regulator_assist` object (`{ register_found, status, issued_date, expiry_date, full_name_in_register }`). V1's default `NullRegulatorAssistLookup` always returns null → the field is absent. A future Phase 1.5 swap-in becomes a UI-only consumer change with no contract bump.

### 3.3 `POST /api/admin/verifications/{id}/approve`

**Permission**: `verification.review`.
**Idempotency**: required.
**Concurrency**: optimistic (xmin); `409 verification.already_decided` if lost.

**Request body**:
```json
{
  "reason": { "en": "License verified against SCFHS register.", "ar": "تم التحقق من الترخيص مقابل سجل الهيئة." }
}
```

`reason` is an object `{ en?: string, ar?: string }` (FR-033). At least one locale MUST be present. Empty object → `400 verification.reason_required`. Both locales preserved in the audit log; customer-facing rendering uses the customer's preferred locale (with a one-line "(reviewer left this in {OtherLocale})" notice if the other is missing).

**Behavior**: state → `approved`. `expires_at` set from `market.expiry_days`. If this verification has `supersedes_id`, the prior approval transitions to `superseded` in the same Tx. Eligibility cache rebuilt in the same Tx. `VerificationApproved` event published.

**Errors**:
| HTTP | `reason_code` |
|---|---|
| 400 | `verification.reason_required` |
| 409 | `verification.invalid_state_for_action` |
| 409 | `verification.already_decided` (optimistic concurrency) |

### 3.4 `POST /api/admin/verifications/{id}/reject`

Same shape as 3.3. State → `rejected`. Cool-down begins. No eligibility change for any other prior verification. `VerificationRejected` event published.

### 3.5 `POST /api/admin/verifications/{id}/request-info`

Same shape as 3.3. State → `info-requested`. SLA timer pauses. `VerificationInfoRequested` event published.

### 3.6 `POST /api/admin/verifications/{id}/revoke`

**Permission**: `verification.revoke` (distinct from 3.3–3.5).
**Idempotency**: required.

**Request body**: same `{ reason: { en?, ar? } }` shape as 3.3 (FR-033).
```json
{
  "reason": { "en": "License revoked by SCFHS notice 2026-04-25.", "ar": "تم إلغاء الترخيص بإشعار الهيئة 2026-04-25." }
}
```

**Behavior**: state → `revoked`. Eligibility cache rewritten. No cool-down for next submission (FR-009). `VerificationRevoked` event published.

**Errors**:
| HTTP | `reason_code` |
|---|---|
| 403 | `verification.revoke_permission_required` |
| 409 | `verification.invalid_state_for_action` (only `approved` is revocable) |

### 3.7 `GET /api/admin/verifications/{id}/documents/{documentId}/open`

**Permission**: `verification.review`.

**Behavior**:
- If parent verification is in any non-terminal state OR is `approved`: returns a short-lived signed URL to fetch the document body. Audit event `verification.pii_access` written with `kind=DocumentBodyRead`.
- If parent verification is in any terminal state (`rejected`, `expired`, `revoked`, `superseded`, `void`): returns the same signed URL. Audit event written with `kind=DocumentBodyRead` AND a separate `verification.pii_access` event for the "open historical document" action with `surface=admin_review`.
- If `purged_at IS NOT NULL`: returns `410 verification.document_purged` with body `{ purged_at, retention_policy_version }`.

**Errors**:
| HTTP | `reason_code` |
|---|---|
| 404 | `verification.document_not_found` |
| 410 | `verification.document_purged` |

---

## 4. Cross-module interfaces (`Modules/Shared/`)

### 4.1 `ICustomerVerificationEligibilityQuery`

```csharp
public interface ICustomerVerificationEligibilityQuery
{
    /// <summary>
    /// Single authoritative answer to "may this customer purchase this restricted SKU right now?".
    /// Consumed by Catalog (005), Cart (009), Checkout (010). Catalog/Cart/Checkout MUST NOT reimplement this policy (FR-024).
    /// </summary>
    /// <returns>
    /// EligibilityResult { Class: 'Eligible'|'Ineligible'|'Unrestricted', ReasonCode: EligibilityReasonCode, MessageKey: string, ExpiresAt: DateTimeOffset? }
    /// </returns>
    /// <remarks>
    /// Latency budget: p95 ≤ 5 ms, p99 ≤ 15 ms (locks SC-004). Deterministic for (customer, sku, point-in-time).
    /// </remarks>
    ValueTask<EligibilityResult> EvaluateAsync(
        Guid customerId,
        string sku,
        CancellationToken cancellationToken);

    /// <summary>
    /// Bulk variant for catalog list pages. Same semantics, batched.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, EligibilityResult>> EvaluateManyAsync(
        Guid customerId,
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken);
}

public sealed record EligibilityResult(
    EligibilityClass Class,
    EligibilityReasonCode ReasonCode,
    string MessageKey,
    DateTimeOffset? ExpiresAt);

public enum EligibilityClass { Eligible, Ineligible, Unrestricted }
```

`EligibilityReasonCode` enum is documented in [data-model.md §4](../data-model.md).

### 4.2 `ICustomerAccountLifecycleSubscriber`

```csharp
public interface ICustomerAccountLifecycleSubscriber
{
    Task OnAccountLockedAsync(CustomerAccountLocked evt, CancellationToken ct);
    Task OnAccountDeletedAsync(CustomerAccountDeleted evt, CancellationToken ct);
    Task OnMarketChangedAsync(CustomerMarketChanged evt, CancellationToken ct);
}

public sealed record CustomerAccountLocked(Guid CustomerId, string Reason, DateTimeOffset OccurredAt);
public sealed record CustomerAccountDeleted(Guid CustomerId, DateTimeOffset OccurredAt);
public sealed record CustomerMarketChanged(Guid CustomerId, string FromMarket, string ToMarket, Guid ChangedBy, DateTimeOffset OccurredAt);
```

Spec 004 publishes; spec 020's `AccountLifecycleHandler` subscribes.

### 4.3 `IProductRestrictionPolicy` (declared here; implemented by spec 005)

```csharp
public interface IProductRestrictionPolicy
{
    /// <summary>
    /// Returns the restriction policy for a SKU. May return Unrestricted for products not subject to professional verification.
    /// V1: VendorId always null; reserved for Phase 2 multi-vendor.
    /// </summary>
    ValueTask<ProductRestrictionPolicy> GetForSkuAsync(string sku, CancellationToken ct);
}

public sealed record ProductRestrictionPolicy(
    string Sku,
    IReadOnlySet<string> RestrictedInMarkets,    // empty set = unrestricted
    string? RequiredProfession,
    Guid? VendorId);                              // V1: null
```

### 4.4 `IRegulatorAssistLookup` (V1: null implementation)

```csharp
public interface IRegulatorAssistLookup
{
    /// <summary>
    /// V1 default impl returns null. Phase 1.5+ may swap a real adapter.
    /// FR-016a: NEVER called as part of a state transition.
    /// </summary>
    Task<RegulatorAssistResult?> LookupAsync(string marketCode, string regulatorIdentifier, CancellationToken ct);
}

public sealed record RegulatorAssistResult(
    bool RegisterFound,
    string? Status,                       // 'active' | 'suspended' | 'expired' | etc — provider-dependent
    DateOnly? IssuedDate,
    DateOnly? ExpiryDate,
    string? FullNameInRegister);
```

### 4.5 `IPiiAccessRecorder` (internal; not in `Shared/`)

```csharp
internal interface IPiiAccessRecorder
{
    Task RecordAsync(PiiAccessKind kind, Guid verificationId, Guid? documentId, CancellationToken ct);
}

internal enum PiiAccessKind { LicenseNumberRead, DocumentBodyRead, DocumentMetadataRead }
```

Centralizes FR-015a-e compliance at one chokepoint (R13).

---

## 5. Domain events (in-process bus)

Defined in `Modules/Shared/VerificationDomainEvents.cs`; consumed by spec 025 once it lands.

See [data-model.md §6](../data-model.md) for record shapes.

---

## 6. OpenAPI

`services/backend_api/openapi.verification.json` is generated from handler signatures + this contract. CI fails the PR if the artifact diff is unexpected (Guardrail #2). Reviewers check the diff to ratify intentional contract changes.

---

## 7. Reason-code enum (full set, V1)

| Code | Used by |
|---|---|
| `verification.required_field_missing` | 2.1 |
| `verification.regulator_identifier_invalid` | 2.1 |
| `verification.documents_invalid` | 2.1 |
| `verification.document_too_large` | 2.5 |
| `verification.document_type_not_allowed` | 2.5 |
| `verification.document_aggregate_exceeded` | 2.5 |
| `verification.document_scan_failed` | 2.5 |
| `verification.document_not_found` | 3.7 |
| `verification.document_purged` | 3.7 |
| `verification.already_pending` | 2.1 |
| `verification.cooldown_active` | 2.1 |
| `verification.invalid_state_for_action` | 2.5 / 2.6 / 3.3 / 3.4 / 3.5 / 3.6 |
| `verification.no_changes_provided` | 2.6 |
| `verification.renewal_window_not_open` | 2.7 |
| `verification.no_active_approval` | 2.7 |
| `verification.renewal_already_pending` | 2.7 |
| `verification.reason_required` | 3.3 / 3.4 / 3.5 / 3.6 |
| `verification.already_decided` | 3.3–3.6 (optimistic concurrency) |
| `verification.revoke_permission_required` | 3.6 |
| `verification.account_inactive` | 2.1 |
| `verification.not_found` | 2.4 / 3.x |
