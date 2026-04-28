# HTTP & Cross-Module Contract: Quotes and B2B (Spec 021)

**Date**: 2026-04-28
**Spec**: [../spec.md](../spec.md) · **Plan**: [../plan.md](../plan.md) · **Data model**: [../data-model.md](../data-model.md)

This file is the source of truth for every endpoint, request body, response body, error reason code, and cross-module interface that spec 021 publishes. The OpenAPI artifact `services/backend_api/openapi.b2b.json` is regenerated from handler signatures and asserted in CI to match this document (Guardrail #2).

---

## 1. Conventions

- **Base paths**: `/api/customer/quotes`, `/api/customer/companies`, `/api/admin/quotes`, `/api/admin/companies` (the admin-side company surface lands as part of spec 019; declared here for the SuspendCompany action).
- **Authentication**: customer endpoints require a customer JWT; admin endpoints require an admin JWT + the named permission.
- **Idempotency**: every state-transitioning POST requires `Idempotency-Key: <uuid>`.
- **Locale**: every response with user-facing strings is rendered in the locale resolved from `Accept-Language`.
- **Error envelope**: same shape as spec 020 (problem-details with `reason_code` + `trace_id`).

---

## 2. Customer quote endpoints

### 2.1 `POST /api/customer/quotes/from-cart` — request quote from cart

**Permission**: any authenticated customer (US1 implies a buyer-role customer; US2 individual customer also valid if they have a non-empty cart).

**Request body**:
```json
{
  "company_id": "01HX...",        // optional — if omitted, individual quote
  "branch_id": "01HX...",         // optional — only meaningful with company_id
  "po_number": "PO-2026-0042",    // optional — required only when company.po_required=true
  "message": { "en": "Need bulk pricing.", "ar": "نحتاج تسعير جملة." }   // optional, at least one locale if provided
}
```

**Behavior**: invokes `ICartSnapshotProvider.SnapshotAndClearAsync(customerId)` (R4), persists the snapshot into `quotes.originating_cart_snapshot`, captures `IProductRestrictionPolicy` for each line into `quotes.restriction_policy_snapshot`, transitions to `requested`, publishes `QuoteRequested`, audits.

**Success — 201**: full Quote summary (id, state, requested_at, market_code, company_id?, branch_id?).

**Errors**:
| HTTP | `reason_code` | When |
|---|---|---|
| 400 | `quote.cart_empty` | The customer's cart is empty. |
| 400 | `quote.po_required` | `company.po_required=true` and no `po_number`. |
| 400 | `quote.required_field_missing` | Other required fields missing (e.g. `message` provided as object with no locale). |
| 400 | `quote.po_already_used` | `company.unique_po_required=true` and PO collides. |
| 409 | `quote.no_active_company_membership` | `company_id` provided but caller has no active membership / role doesn't permit quote requests. |
| 422 | `quote.market_mismatch` | Company market differs from caller's market-of-record. |
| 422 | `quote.account_inactive` | Caller's account is locked or deleted. |
| 422 | `quote.company_suspended` | Company is in `suspended` state. |
| 429 | `quote.rate_limit_exceeded` | Per-customer or per-company hourly cap exceeded; body includes `retry_after_seconds`. |

### 2.2 `POST /api/customer/quotes/from-product` — request quote from a single product

**Permission**: any authenticated customer.

**Request body**:
```json
{
  "product_id": "01HX...",
  "quantity": 5,
  "company_id": "01HX...",        // optional — for company-buyer flow
  "branch_id": "01HX...",         // optional
  "po_number": "PO-2026-0042",    // optional
  "message": { "en": "...", "ar": "..." }   // optional
}
```

**Behavior**: same as 2.1 except `originating_product_id` populated and `originating_cart_snapshot=NULL`. Cart is NOT cleared (the customer may legitimately have other items in their cart).

**Errors**: same set as 2.1 plus `quote.product_not_quotable` (the product's market or restriction policy disallows quoting).

### 2.3 `GET /api/customer/quotes` — list my quotes

**Permission**: any authenticated customer.
**Query**: `?state=<csv>&company_id=<id>&page=<n>&page_size=<n≤50>&sort=newest|oldest`.
**Returns**: paginated list scoped to (a) the caller's individual quotes and (b) quotes for any company where the caller holds a `buyer` or `approver` membership; `companies.admin`-membership confers visibility of every quote of that company.

### 2.4 `GET /api/customer/quotes/{id}` — get quote detail

**Permission**: caller must be the customer-owner OR a member of the quote's company with role visible-to-the-quote (per 2.3 rules).

**Returns**: full quote, including the latest `QuoteVersion` line items + totals, every prior version's metadata (without re-rendering the PDFs), `next_action` derived field (`null | request_revision | submit_acceptance | renew_now`).

### 2.5 `POST /api/customer/quotes/{id}/withdraw` — withdraw

**Permission**: caller is the customer-owner OR holds `companies.admin` membership for the quote's company.
**Behavior**: state → `withdrawn`. Allowed in any non-terminal state.

### 2.6 `POST /api/customer/quotes/{id}/request-revision` — request a revision

**Permission**: caller is the customer-owner OR holds `buyer` membership.
**Request body**: `{ "comment": { "en": "...", "ar": "..." } }` — at least one locale.
**Behavior**: state from `revised` → `drafted` (operator-only-visible); `customer_revision_comment` is preserved on the next `QuoteVersion`.

**Errors**: `409 quote.invalid_state_for_action`, `400 quote.required_field_missing`.

### 2.7 `POST /api/customer/quotes/{id}/submit-acceptance` — submit acceptance

**Permission**: caller is the customer-owner OR holds `buyer` (or `companies.admin`) membership for the quote's company.

**Request body**:
```json
{
  "po_number": "PO-2026-0042",          // optional override; required when company.po_required=true
  "po_warning_acknowledged": false,     // set true when caller is confirming a soft warning re: reused PO
  "tax_preview_drift_acknowledged": false  // set true when caller is confirming a tax-drift conversion (R11)
}
```

**Behavior**: per Clarifications Q1 — if company has `approver_required=true` and ≥ 1 approver → state to `pending-approver`; else direct to `accepted` and conversion runs (per FR-032). Both paths carry `Idempotency-Key`.

**Soft-warning response (PO reuse with `unique_po_required=false`)**: when the PO collides with one or more prior quotes for this company AND the request body's `po_warning_acknowledged != true`, the handler MUST return `200 OK` with the body shape below and DO NOT transition state. The caller re-submits with `po_warning_acknowledged=true` to commit; the second call's audit metadata records the acknowledgement (`quote.po_warning_acknowledged` event with `prior_quote_ids`).

```json
{
  "po_warning": {
    "prior_quote_ids": ["01HX...", "01HY..."],
    "message_key": "quote.po_warning"
  }
}
```

**Tax-preview-drift response**: when the conversion path detects drift > `quote_market_schemas.tax_preview_drift_threshold_pct` AND `tax_preview_drift_acknowledged != true`, the handler returns `409 quote.tax_preview_drift_threshold_exceeded` with the new tax + drift % in the body so the caller can render a confirm prompt and re-submit with `tax_preview_drift_acknowledged=true`.

**Errors**:
| HTTP | `reason_code` |
|---|---|
| 409 | `quote.invalid_state_for_action` (must be `revised`) |
| 409 | `quote.expired` (race with expiry worker) |
| 409 | `quote.no_approver_available` (`approver_required=true` and 0 approvers — Clarifications Q1) |
| 409 | `quote.po_already_used` (hard reject when `unique_po_required=true`) |
| 409 | `quote.tax_preview_drift_threshold_exceeded` (soft block until acknowledged — body includes the new tax + drift %) |
| 422 | `quote.eligibility_required` (FR-036 — restricted SKU + buyer not eligible per spec 020) |
| 422 | `quote.market_mismatch` (caller's market changed mid-flight per FR-046) |

### 2.8 `GET /api/customer/quotes/{id}/versions/{versionId}/documents/{locale}` — download PDF

**Permission**: same visibility rules as 2.4.
**Behavior**: returns a short-lived signed URL to the locale-specific PDF blob.
**Errors**: `404 quote.not_found`, `404 quote.document_not_found`.

### 2.9 `POST /api/customer/quotes/{id}/save-as-template` — repeat-order template

**Permission**: caller is the customer-owner OR holds `buyer` / `companies.admin` membership.
**Request body**: `{ "name": { "en": "...", "ar": "..." } }` — at least one locale.
**Behavior**: state of the quote must be `accepted`; INSERT `repeat_order_templates`; uniqueness scoped per [research.md §R12](../research.md).
**Errors**: `409 template.name_already_exists`, `409 quote.invalid_state_for_action`.

---

## 3. Approver endpoints (customer surface)

### 3.1 `GET /api/customer/quotes/awaiting-my-approval` — list

**Permission**: caller must hold `approver` membership for at least one company.
**Query**: `?company_id=<id>&page=<n>&page_size=<n≤50>`.
**Returns**: paginated list of quotes currently in `pending-approver` for any of the caller's `approver`-companies, with buyer + branch + total + validity-remaining + buyer's acceptance note.

### 3.2 `POST /api/customer/quotes/{id}/finalize-acceptance` — finalize

**Permission**: caller must hold `approver` membership for the quote's company. State must be `pending-approver`.
**Idempotency**: required.
**Concurrency**: optimistic xmin guard; loser sees `quote.already_decided`.
**Behavior**: per Clarifications Q1 — first-action-wins. Triggers conversion (FR-032).

**Errors**: `409 quote.already_decided`, `409 quote.invalid_state_for_action`, `409 quote.expired`, `422 quote.eligibility_required`, `409 quote.tax_preview_drift_threshold_exceeded` (caller must re-call with `tax_preview_drift_acknowledged=true` in body).

### 3.3 `POST /api/customer/quotes/{id}/reject-acceptance` — approver rejects

**Permission**: caller must hold `approver` membership.
**Request body**: `{ "comment": { "en": "...", "ar": "..." } }` — at least one locale.
**Behavior**: state `pending-approver → revised`; `approver_rejection_note` set; buyer + the rejecting approver's identity recorded; the buyer is notified via `QuoteApproverRejected`.

---

## 4. Admin quote endpoints

### 4.1 `GET /api/admin/quotes` — queue

**Permission**: `quotes.author` or `quotes.review`.
**Query**: `?market=<code>&state=<csv>&company_id=<id>&customer_id=<id>&age_min_business_days=<n>&search=<po_or_company>&sort=oldest|newest&page=<n>&page_size=<n≤100>`.
**Default**: oldest-first; non-terminal states; reviewer's market scope.

**Per-row response shape** (FR-014):
```json
{
  "id": "01HX...",
  "state": "requested",
  "market_code": "ksa",
  "company_id": "01HX...",
  "customer_id": "01HX...",
  "po_number": "PO-2026-0042",
  "requested_at": "2026-04-26T10:00:00Z",
  "expires_at": null,
  "age_business_days": 2,
  "sla_signal": "warning",
  "totals_summary": null
}
```

`sla_signal ∈ ('ok' | 'warning' | 'breach')` — `ok` when `age_business_days < sla_warning_business_days`; `warning` when `sla_warning_business_days ≤ age_business_days < sla_decision_business_days`; `breach` when `age_business_days ≥ sla_decision_business_days`. No pause states (spec 021 has no `info-requested` analogue of spec 020). Both thresholds resolved from the active `quote_market_schemas` row.

### 4.2 `GET /api/admin/quotes/{id}` — detail

**Permission**: `quotes.review` (read) / `quotes.author` (read for editing).
**Returns**: full Quote + every QuoteVersion (line items + totals) + every transition + the customer / company context + the `restriction_policy_snapshot` + the `customer_locale` field (FR-033 mirror) + the active `quote_market_schema` row that applied at request time.

**Real-time advisory blocks** (re-evaluated each call, not snapshotted; intended for the admin authoring UI to render warnings before publish):

- `verification_warnings: [{ sku, reason_code, message_key }]` — populated when any restricted-SKU line on the latest QuoteVersion (or, for unpublished quotes, the originating cart snapshot or product) currently fails spec 020's `ICustomerVerificationEligibilityQuery`. Empty array when all lines pass. Surfaces the spec.md §Edge Cases "verification status flips to expired before authoring" condition.

- `archived_sku_lines: [string]` — array of SKUs on the latest QuoteVersion (or originating cart / product) that are no longer present-and-active in spec 005's catalog. Empty when none. Surfaces the spec.md §Edge Cases "operator authors a quote whose lines no longer all exist" condition.

### 4.3 `POST /api/admin/quotes/{id}/draft` — author / revise a draft version

**Permission**: `quotes.author`.
**Idempotency**: required.

**Request body**:
```json
{
  "lines": [
    { "sku": "ABC-123", "quantity": 5, "override_unit_price": 119.00, "override_reason": { "en": "Bulk discount.", "ar": "خصم الكمية." }, "line_discount_amount": 0 }
  ],
  "terms_text": { "en": "Net 30", "ar": "شبكة 30" },
  "terms_days": 30,
  "validity_extends": false,
  "internal_note": "Customer requested reduced terms."
}
```

**Behavior**: state from `requested` or `revised` → `drafted`. Below-baseline overrides require non-empty `override_reason` (`{en?, ar?}` with at least one locale); audited per FR-040.

**Errors**: `400 quote.below_baseline_reason_required`, `400 quote.required_field_missing`, `409 quote.invalid_state_for_action`.

### 4.4 `POST /api/admin/quotes/{id}/publish` — publish current draft

**Permission**: `quotes.author`.
**Idempotency**: required.
**Behavior**: state `drafted → revised`. Generates EN + AR PDFs synchronously (R3); persists `QuoteVersion` + `QuoteVersionDocument` rows; recomputes `expires_at` if `validity_extends=true` (Clarifications Q5); publishes `QuotePublished` domain event.

**Errors**: `400 quote.required_field_missing` (missing terms or empty lines), `409 quote.invalid_state_for_action`, `500` if PDF generation fails (the entire publish is rolled back; the slice retries are safe via Idempotency-Key replay returning the failure).

---

## 5. Company-account endpoints (customer surface)

### 5.1 `POST /api/customer/companies` — register

**Permission**: any authenticated customer (with no existing `companies.admin` membership for this company's tax_id).
**Request body**: `{ name: {en, ar}, tax_id, market_code, primary_address, billing_address?, approver_required?, po_required?, unique_po_required? }`.
**Behavior**: per Clarifications Q2 — state `active` immediately when the per-market `company_verification_required` is OFF (default for both markets at V1 launch); else `pending-verification`. Caller is bound as `companies.admin` + `buyer` membership rows.
**Errors**: `409 company.duplicate_tax_id`, `400 company.tax_id_invalid`, `422 quote.market_mismatch`.

### 5.2 `GET /api/customer/companies/{id}` — read

**Permission**: caller must hold any active membership for this company.
**Returns**: full company config + branches list + memberships list (with user names; PII filtered for non-`companies.admin` callers).

### 5.3 `PATCH /api/customer/companies/{id}` — update config

**Permission**: caller must hold `companies.admin` membership.
**Request body**: any subset of `{ name, primary_address, billing_address, approver_required, po_required, unique_po_required, invoice_billing_eligible }`.
**Behavior**: writes the audit `company.config_changed` event with the changed fields. Toggling `approver_required=false` does NOT auto-finalize `pending-approver` quotes (FR-031); they revert to `revised`.

### 5.4 `POST /api/customer/companies/{id}/branches` — add branch

**Permission**: `companies.admin`.

### 5.5 `DELETE /api/customer/companies/{id}/branches/{branchId}` — remove branch

**Permission**: `companies.admin`.
**Errors**: `409` if any non-terminal quote references the branch (caller must reassign or wait for terminal state).

### 5.6 `POST /api/customer/companies/{id}/invitations` — invite user

**Permission**: `companies.admin`.
**Request body**: `{ invited_email, target_role }`.
**Behavior**: creates `company_invitations` row; sends email via spec 025 in invitee's preferred locale (if known) or company's default; expires in 14 days (per `quote_market_schemas.invitation_ttl_days`).
**Errors**: `400 company.invitation_email_invalid`, `409 company.invitation_already_pending` (a pending invite already exists for `(company, email, role)`), `409 company.member_already_exists`.

### 5.7 `POST /api/customer/companies/invitations/{token}/accept` — invitee accepts

**Permission**: any authenticated customer whose verified email matches the invite (or the platform allows accepting via opaque token).
**Behavior**: invitation `pending → accepted`; INSERT `company_memberships` for the invitee with the target role.
**Errors**: `404` (token not found), `409 company.invitation_expired`, `409 company.invitation_already_pending` (token already terminal).

### 5.8 `POST /api/customer/companies/invitations/{token}/decline` — invitee declines

### 5.9 `DELETE /api/customer/companies/{id}/memberships/{membershipId}` — remove member

**Permission**: `companies.admin`.
**Errors**: `409 company.last_admin_cannot_be_removed`, `409 company.last_approver_cannot_be_removed_with_required` (when `approver_required=true` and this is the only approver).

### 5.10 `PATCH /api/customer/companies/{id}/memberships/{membershipId}` — change role

**Permission**: `companies.admin`. Same FR-024 / FR-025 invariants apply.

---

## 6. Admin company-suspend (declared here, used by spec 019)

### 6.1 `POST /api/admin/companies/{id}/suspend`

**Permission**: `companies.suspend` (granted by spec 019's role model).
**Behavior**: company state → `suspended`; FR-026 applies (no new quote requests, no acceptance of non-terminal quotes); all members notified; audit `company.suspended` written.

---

## 7. Cross-module interfaces (`Modules/Shared/`)

### 7.1 `IOrderFromQuoteHandler` (declared here; implemented by spec 011)

Full signature in [research.md §R6](../research.md).

### 7.2 `IPricingBaselineProvider` (declared here; implemented by spec 007-a)

Full signature in [research.md §R5](../research.md).

### 7.3 `ICartSnapshotProvider` (declared here; implemented by spec 009)

Full signature in [research.md §R4](../research.md).

### 7.4 `ICustomerVerificationEligibilityQuery` (declared by spec 020; consumed here)

See spec 020's `contracts/verification-contract.md §4.1`. Consumed at acceptance time per FR-036.

### 7.5 `ICustomerAccountLifecycleSubscriber` (declared by spec 020; subscribed here)

See spec 020's `contracts/verification-contract.md §4.2`. Subscribed by `AccountLifecycleHandler` (R13).

---

## 8. Domain events

`Modules/Shared/QuoteDomainEvents.cs` and `Modules/Shared/CompanyInvitationDomainEvents.cs` — see [data-model.md §6](../data-model.md) for record shapes.

---

## 9. Reason-code enum (full set, V1)

| Code | Used by |
|---|---|
| `quote.required_field_missing` | 2.1 / 2.2 / 4.3 |
| `quote.cart_empty` | 2.1 |
| `quote.product_not_quotable` | 2.2 |
| `quote.no_active_company_membership` | 2.1 / 2.2 |
| `quote.po_required` | 2.1 / 2.2 / 2.7 |
| `quote.po_already_used` | 2.1 / 2.2 / 2.7 (hard reject when `unique_po_required=true`) |
| `quote.po_warning_acknowledged` | 2.7 metadata only |
| `quote.rate_limit_exceeded` | 2.1 / 2.2 |
| `quote.market_mismatch` | every endpoint touching market boundary |
| `quote.eligibility_required` | 2.7 / 3.2 (FR-036) |
| `quote.invalid_state_for_action` | almost every state-transition endpoint |
| `quote.no_changes_provided` | 2.6 |
| `quote.no_approver_available` | 2.7 (Clarifications Q1) |
| `quote.cooldown_active` | reserved (R10), unused in V1 |
| `quote.already_decided` | 3.2 (optimistic concurrency) |
| `quote.reason_required` | 2.6 / 3.3 / 4.3 |
| `quote.below_baseline_reason_required` | 4.3 (FR-040) |
| `quote.expired` | 2.7 / 3.2 |
| `quote.tax_preview_drift_threshold_exceeded` | 2.7 / 3.2 (R11) |
| `quote.idempotency_replay` | meta — returned on Idempotency-Key replays |
| `quote.account_inactive` | 2.1 / 2.2 / 2.7 |
| `quote.company_suspended` | 2.1 / 2.2 / 2.7 |
| `quote.product_archived` | edge case — admin authoring detects archived SKU |
| `quote.not_found` | 2.4 / 2.5 / 2.6 / 2.7 / 4.x |
| `quote.document_not_found` | 2.8 |
| `company.tax_id_invalid` | 5.1 |
| `company.duplicate_tax_id` | 5.1 |
| `company.last_admin_cannot_be_removed` | 5.9 / 5.10 |
| `company.last_approver_cannot_be_removed_with_required` | 5.9 / 5.10 |
| `company.member_already_exists` | 5.6 |
| `company.invitation_email_invalid` | 5.6 |
| `company.invitation_already_pending` | 5.6 / 5.7 |
| `company.invitation_expired` | 5.7 / 5.8 |
| `template.name_already_exists` | 2.9 |
