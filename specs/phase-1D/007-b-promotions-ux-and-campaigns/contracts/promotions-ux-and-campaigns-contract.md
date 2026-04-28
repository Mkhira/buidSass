# API Contract: Promotions UX & Campaigns (Spec 007-b · Phase 1D)

**Date**: 2026-04-28
**Inputs**: spec.md, plan.md, research.md, data-model.md (this directory).
**Generated artifact**: `services/backend_api/openapi.pricing.commercial.json` (regenerated each PR).

This contract enumerates every HTTP endpoint, every request body, every response body, every reason code, every domain event, and every cross-module interface introduced or extended by spec 007-b. It is the single source of truth consumed by the contract-test suite (`tests/Pricing.Tests/Contract/`).

**Conventions across all sections**:
- Base path: `/v1/admin/commercial/`. RBAC enforced via `[RequirePermission("commercial.*")]` attribute (see §1).
- All POST / PUT / PATCH require `Idempotency-Key: <uuid-v4>` header. Replays within 24 h return the original response (per spec 003 platform middleware).
- All mutations require `If-Match: <row_version>` header on existing rows. Mismatch → `409 commercial.row.version_conflict`.
- All responses use `application/json; charset=utf-8`.
- Bilingual fields use `{ "ar": "...", "en": "..." }` shape; both required for customer-visible labels.
- Money is integer minor units (`*_minor`). Example: `cap_minor: 5000000` is SAR 50 000 (5 000 000 fils).
- Per-rule rate-limits per FR-035: 30 writes / min / actor and 600 / hour / actor; over-limit → `429 commercial.rate_limit_exceeded`.

---

## 1. RBAC

| Permission | Granted by default to | Endpoints |
|---|---|---|
| `commercial.operator` | new role | All coupon, promotion, campaign, preview-profile authoring; preview tool. Cannot edit business pricing or thresholds. |
| `commercial.b2b_authoring` | new role | All `commercial.operator` endpoints + business-pricing endpoints (`§4`). |
| `commercial.approver` | new role | Approval-queue endpoints (`§7`); promote PreviewProfile to `shared` (`§6.2`). |
| `commercial.threshold_admin` | `super_admin` only | Threshold mutation (`§8.2`). |
| `viewer.finance` (existing spec 015) | finance team | Read-only on every endpoint. |
| `support` (existing spec 023) | support agents | Read-only on coupon code + state only (no preview, no audit-diff). |

`super_admin` is implicit superset of all of the above.

---

## 2. Coupons

### 2.1 `POST /v1/admin/commercial/coupons` — Create coupon

**Auth**: `commercial.operator`.

**Request**:
```json
{
  "code": "RAMADAN10",
  "markets": ["SA","EG"],
  "type": "percent_off",
  "value": 10,
  "amount_off_minor_per_market": null,
  "cap_minor": 5000,
  "per_customer_limit": 1,
  "overall_limit": 10000,
  "excludes_restricted": true,
  "eligibility_segment_id": null,
  "valid_from": "2026-04-30T00:00:00Z",
  "valid_to": "2026-05-30T23:59:59Z",
  "stacks_with_promotions": true,
  "display_in_banners": false,
  "label": { "ar": "خصم رمضان", "en": "Ramadan discount" },
  "description": { "ar": "...", "en": "..." }
}
```

`amount_off_minor_per_market` is required when `type=amount_off`; `value` is required when `type=percent_off`.

**Response 201**:
```json
{
  "id": "0192c8d0-...",
  "code": "RAMADAN10",
  "state": "draft",
  "row_version": "...",
  "created_at_utc": "2026-04-28T15:00:00Z"
}
```

**Errors**:
- `400 coupon.code.duplicate` — uppercase code already exists (R6).
- `400 coupon.label.required_bilingual` — missing `ar` or `en`.
- `400 coupon.schedule.invalid_window` — `valid_to <= valid_from`.
- `400 coupon.value.invalid` — `value <= 0` or out of range.
- `400 commercial.markets.empty_or_invalid` — empty or unknown market code.
- `400 commercial.usage_limit.zero_forbidden` — `per_customer_limit=0` or `overall_limit=0`.
- `429 commercial.rate_limit_exceeded`.

**Audit**: `coupon.created` with `after_jsonb` snapshot.

---

### 2.2 `PATCH /v1/admin/commercial/coupons/{id}` — Update coupon (non-state-transition)

**Auth**: `commercial.operator`. **Idempotent**.

**Request body**: any subset of the create fields except `code` (codes are immutable post-create).

**Errors** (additional):
- `400 coupon.locked.active_pricing_field` — when state is `active` and a pricing field (`type`, `value`, `amount_off_minor_per_market`, `cap_minor`, `valid_from`, `valid_to`) is in the patch (FR-004).
- `403 coupon.expired.read_only` — when state is `expired`.
- `409 commercial.row.version_conflict`.

**Audit**: `coupon.updated` with `before_jsonb` / `after_jsonb` / `diff_jsonb`.

---

### 2.3 `POST /v1/admin/commercial/coupons/{id}/schedule` — Schedule (lifecycle: draft → scheduled or active)

**Auth**: `commercial.operator`.

**Request**: `{}` (current values used).

**Response 200**: full coupon body with new `state` (`scheduled` if `valid_from > now`, else `active`).

**Errors**:
- `403 coupon.activation.requires_approval` — high-impact gate trips; route to `§7.2`.
- `409 commercial.row.version_conflict`.
- `400 commercial.schedule.invalid_state` — current state is not `draft`.

**Audit**: `coupon.lifecycle_transitioned`.

**Domain events**: `CouponActivated` (if state becomes `active`).

---

### 2.4 `POST /v1/admin/commercial/coupons/{id}/deactivate`

**Auth**: `commercial.operator`.

**Request**: `{ "reason_note": "abuse detected — incident #4421" }`.

**Errors**:
- `400 commercial.deactivation.reason_required` — note < 10 chars.
- `400 commercial.deactivation.invalid_state` — current state not in `{scheduled, active}`.
- `409 commercial.row.version_conflict`.

**Audit**: `coupon.lifecycle_transitioned` + sub-kind `coupon.deactivated`.

**Domain event**: `CouponDeactivated { in_flight_grace_seconds: <from threshold row> }`.

---

### 2.5 `POST /v1/admin/commercial/coupons/{id}/reactivate`

**Auth**: `commercial.operator`.

**Request**: `{ "reason_note": "false positive resolved" }` (≥ 10 chars).

**Errors**:
- `400 commercial.reactivation.expired_terminal` — current state is `expired`.
- `400 commercial.reactivation.invalid_state` — current state not `deactivated`.
- `403 coupon.activation.requires_approval` — high-impact gate trips on reactivation.

**Audit + event**: as `lifecycle_transitioned` + `CouponReactivated`.

---

### 2.6 `POST /v1/admin/commercial/coupons/{id}/clone-as-draft`

**Auth**: `commercial.operator`.

Creates a new draft coupon with the prior body and an empty schedule. `code` field receives a `_DRAFT_<short-uuid>` suffix until the operator edits it.

**Response 201**: new coupon body in `state=draft`.

**Errors**: `400 coupon.clone.source_invalid` — source state not `expired` or `deactivated`.

---

### 2.7 `GET /v1/admin/commercial/coupons` — List

**Auth**: `commercial.operator` or `support` or `viewer.finance` (returns shape varies by role per RBAC §1).

**Query**: `state`, `markets`, `q` (substring code or label), `created_after`, `cursor`, `limit` (default 50, max 200).

**Response 200**: `{ items: [...], next_cursor?: "..." }`.

---

### 2.8 `GET /v1/admin/commercial/coupons/{id}` — Read

**Auth**: as `§2.7`.

**Response 200**: full coupon body, including `state`, lifecycle metadata, `audit_summary` (last 10 events from `commercial_audit_events`).

---

### 2.9 `DELETE /v1/admin/commercial/coupons/{id}` — Forbidden

**Always returns**: `405 commercial.row.delete_forbidden`. (FR-005a; documented for explicitness.)

---

## 3. Promotions

Mirror of §2 with these substitutions:
- Path prefix: `/v1/admin/commercial/promotions`.
- Body fields: `kind` (`percent_off | amount_off | bogo | bundle`), `applies_to[]` (SKU list, max 500), `priority`, `stacks_with_other_promotions`, `stacks_with_coupons`, `banner_eligible`, plus the same labels / schedule fields.
- `bogo` requires `reward_sku`; `bundle` requires `bundle_sku`.
- Schedule action surfaces non-blocking warning `promotion.overlap.warning` with overlapping rule ids when `stacks_with_other_promotions=false` and overlap exists; client must include `acknowledge_overlap: true` in the request to proceed.
- Errors: `400 promotion.applies_to.too_many` (> 500), `400 promotion.overlap.warning` (without acknowledgement), `400 promotion.locked.active_pricing_field`, `400 promotion.target_sku_invalid` (BOGO/bundle target archived).
- Audit kinds: `promotion.created/updated/lifecycle_transitioned`. Domain events: `PromotionActivated/Expired/Deactivated/Reactivated`.

---

## 4. Business Pricing

### 4.1 `PUT /v1/admin/commercial/business-pricing/tier-rows` — Upsert tier row

**Auth**: `commercial.b2b_authoring`.

**Request**:
```json
{ "tier_id": "...", "sku": "GLV-NTR-100", "market_code": "SA", "net_minor": 8800 }
```

**Response 200**: row body.

**Errors**:
- `403 commercial.business_pricing.forbidden` — operator lacks `commercial.b2b_authoring`.
- `400 business_pricing.below_cogs.warning` — non-blocking; client retries with `acknowledge_below_cogs: true`.
- `409 commercial.row.version_conflict`.
- `400 business_pricing.row.duplicate` — uniqueness violation.

**Audit**: `business_pricing.row_changed`.

### 4.2 `PUT /v1/admin/commercial/business-pricing/company-overrides` — Upsert company override

**Auth**: `commercial.b2b_authoring`.

**Request**: as 4.1 plus `company_id` + optional `copied_from_tier_id`.

Errors: as 4.1 plus `400 business_pricing.company_invalid` (company suspended or unknown).

### 4.3 `POST /v1/admin/commercial/business-pricing/bulk-import/preview`

**Auth**: `commercial.b2b_authoring`.

**Request**: `multipart/form-data` with field `file` (CSV), header row strict snake_case (`tier_code,sku,net_minor,markets`).

**Response 200**:
```json
{
  "preview_token": "...",
  "preview_token_expires_at": "...",
  "would_insert": [...],
  "would_update": [...],
  "would_skip": [...],
  "rejected": [{"row": 12, "reason": "...", "code": "commercial.bulk_import.row_invalid"}]
}
```

### 4.4 `POST /v1/admin/commercial/business-pricing/bulk-import/commit`

**Auth**: `commercial.b2b_authoring`.

**Request**: `{ "preview_token": "...", "idempotency_key": "..." }`.

**Errors**:
- `409 commercial.bulk_import.snapshot_changed` — DB rows changed since preview; embeds fresh preview.
- `400 commercial.bulk_import.token_expired` — token > 15 min old.
- `400 commercial.bulk_import.invalid_header` — header mismatch.

**Audit**: `business_pricing.bulk_imported` with row counts.

### 4.5 `POST /v1/admin/commercial/business-pricing/{id}/deactivate` and `/reactivate`

Mirror of §2.4 / §2.5 against the `BusinessPricingState` machine; no schedule.

### 4.6 `GET /v1/admin/commercial/business-pricing` — List

Filters: `tier_id?`, `company_id?`, `sku?`, `state`, `q`, paging.

### 4.7 `DELETE /v1/admin/commercial/business-pricing/{id}` — Conditionally forbidden

Returns `405 commercial.row.delete_forbidden` when the row is referenced by any historical `PriceExplanation` (FR-005a + §10 of data-model). Returns `204 No Content` for a never-saved draft row owned by the caller.

---

## 5. Campaigns

### 5.1 `POST /v1/admin/commercial/campaigns` — Create

**Auth**: `commercial.operator`.

**Request**:
```json
{
  "name": { "ar": "تخفيضات العيد 2026", "en": "Eid Sale 2026" },
  "valid_from": "...",
  "valid_to": "...",
  "markets": ["SA"],
  "landing_query": "category=hand-instruments",
  "campaign_link": { "kind": "promotion", "target_id": "..." },
  "notes_internal": "..."
}
```

**Errors**:
- `400 campaign.link.target_expired` — linked rule is `expired`.
- `400 campaign.link.coupon_not_displayable` — link kind `coupon` but target's `display_in_banners=false`.
- `400 campaign.link.invalid_kind` — invalid `kind`.
- `400 campaign.markets.empty_or_invalid`.

### 5.2 `PATCH /v1/admin/commercial/campaigns/{id}`, `POST .../schedule`, `POST .../deactivate`

Mirror of §2; lifecycle is the shared `LifecycleState` machine.

### 5.3 `GET /v1/admin/commercial/campaigns` — List

### 5.4 `GET /v1/admin/commercial/campaigns/lookups` — Banner-link picker (consumed by spec 024 cms)

**Query**: `market_code` (required), `q?`, `cursor?`.

**Response 200**: campaigns where `markets[]` overlaps the given market AND state in `{scheduled, active}`.

(This is the lookup endpoint named in FR-020; spec 024's banner editor consumes it.)

---

## 6. Preview Profiles

### 6.1 `PUT /v1/admin/commercial/preview-profiles` — Upsert

**Auth**: `commercial.operator`.

**Request**:
```json
{
  "id": null,
  "name": "KSA Tier-2 clinic, 3-line cart",
  "market_code": "SA",
  "locale": "ar",
  "account_kind": "b2b",
  "tier_id": "...",
  "verification_state": "approved",
  "cart_lines": [{"sku":"GLV-NTR-100","qty":3,"restricted":false}],
  "visibility": "personal"
}
```

`visibility` defaults to `personal`. Setting `shared` directly is rejected with `403 preview_profile.shared.requires_promotion` — operators must save as `personal` first then call §6.2.

### 6.2 `POST /v1/admin/commercial/preview-profiles/{id}/promote-to-shared`

**Auth**: `commercial.approver` or `super_admin`.

**Request**: `{ "reason_note": "canonical KSA Tier-2 clinic profile for ramp/training" }` (≥ 10 chars).

**Errors**:
- `403 preview_profile.promotion.requires_approver` — caller lacks `commercial.approver`.
- `400 preview_profile.already_shared`.

**Audit**: `preview_profile.visibility_changed` with `before.visibility='personal'` and `after.visibility='shared'`.

### 6.3 `GET /v1/admin/commercial/preview-profiles` — List

Returns the union of (`personal` profiles owned by the caller) + (all `shared` profiles); `super_admin` sees all.

### 6.4 `DELETE /v1/admin/commercial/preview-profiles/{id}`

Allowed for `personal` profiles owned by the caller, or any profile by `super_admin`. Returns `204 No Content`. (Hard-delete IS permitted here per data-model §11 / FR-005a exception.)

---

## 7. Preview Tool

### 7.1 `POST /v1/admin/commercial/preview/price-explanation`

**Auth**: `commercial.operator` or higher.

**Request**:
```json
{
  "preview_profile_id": "0192c8d0-...",
  "in_flight_rule": {
    "kind": "coupon" | "promotion" | "business_pricing",
    "rule_body": { ... full rule JSON as it would be persisted ... },
    "rule_id": null
  }
}
```

`rule_id` is null for an unsaved (in-flight) draft; if set, the engine resolves with that saved rule's current persisted body but applies the body's pending in-flight changes from `rule_body` (used during edit-then-preview without saving).

**Response 200**:
```json
{
  "with_rule": {
    "explanation_hash": "sha256:...",
    "lines": [{"sku":"...", "net_minor":..., "tax_minor":..., "gross_minor":..., "explanation":[...]}],
    "totals": { "subtotal_minor":..., "discount_minor":..., "tax_minor":..., "grand_total_minor":... }
  },
  "without_rule": { ... same shape ... },
  "delta_per_line": [{"sku":"...","delta_minor":-450,"reason":"coupon.percent_off"}],
  "elapsed_ms": 87
}
```

p95 ≤ 200 ms for a 20-line cart (SC-002).

**Errors**:
- `400 preview.profile.not_found`.
- `400 preview.profile.sku_archived` — one or more SKUs in the profile are archived; profile may be edited and retried.
- `400 preview.rule.invalid_body` — rule body fails the same FluentValidator that the create endpoint runs.

**Audit**: NONE. Preview is read-only.

---

## 8. Approval queue (high-impact gate)

### 8.1 `GET /v1/admin/commercial/approvals/pending`

**Auth**: `commercial.approver`.

**Response 200**: list of drafts where `HighImpactGate.IsTriggered(rule, threshold) == true`, excluding the caller's own drafts.

### 8.2 `POST /v1/admin/commercial/approvals` — Record approval

**Auth**: `commercial.approver`.

**Request**:
```json
{
  "target_entity_kind": "coupon",
  "target_entity_id": "...",
  "cosign_note": "high-impact volume aligned with Q2 plan; risk acknowledged"
}
```

**Errors**:
- `403 commercial.self_approval.forbidden` — caller is the draft author.
- `400 commercial.approval.note_too_short` — note < 10 chars.
- `409 commercial.approval.already_recorded` — second concurrent approval (R12 layer 2).
- `400 commercial.approval.gate_not_required` — draft does not trip the high-impact gate.

**Side-effect**: writes the `commercial_approvals` row AND advances the draft to `scheduled` / `active` (same code path as §2.3 schedule).

**Audit**: `commercial.approval_recorded`.

### 8.3 `POST /v1/admin/commercial/approvals/reject`

**Auth**: `commercial.approver`.

**Request**: `{ "target_entity_kind", "target_entity_id", "reason_note": "..." }` (≥ 10 chars).

**Side-effect**: leaves the draft in `draft` state; records the rejection as a `commercial.approval_recorded` audit row with `outcome=rejected`. Author may re-edit and re-submit.

---

## 9. Commercial Thresholds

### 9.1 `GET /v1/admin/commercial/thresholds/{market_code}`

**Auth**: `commercial.operator` or higher (read).

**Response 200**: full threshold row.

### 9.2 `PATCH /v1/admin/commercial/thresholds/{market_code}`

**Auth**: `commercial.threshold_admin` (= `super_admin`).

**Request**:
```json
{
  "gate_enabled": true,
  "threshold_percent_off": 25,
  "threshold_amount_off_minor": 5000000,
  "threshold_duration_days": 14,
  "coupon_in_flight_grace_seconds": 1800,
  "promotion_in_flight_grace_seconds": 1800
}
```

Setting `gate_enabled=false` disables the entire gate for the market (audited).
Setting any single criterion to `null` disables that criterion only.

**Errors**:
- `403 commercial.threshold.forbidden` — caller lacks `commercial.threshold_admin`.
- `400 commercial.threshold.grace_out_of_range` — outside 300-7200.
- `400 commercial.threshold.percent_out_of_range` — outside 0-100.
- `400 commercial.threshold.amount_negative`.
- `400 commercial.threshold.duration_negative`.

**Audit**: `commercial.threshold_changed` with full diff.

**Domain event**: `CommercialThresholdChanged`.

---

## 10. Lookups (consumed by spec 015 admin pickers)

### 10.1 `GET /v1/admin/commercial/lookups/skus`
Consumes spec 005 catalog search; pagination + page-cap 200; respects RBAC.

### 10.2 `GET /v1/admin/commercial/lookups/companies`
Consumes spec 021 company search.

### 10.3 `GET /v1/admin/commercial/lookups/segments`
Consumes spec 019 admin-customers segment search.

(These three exist purely so the admin UI does not take a direct dependency on each upstream module's auth surface; this contract enforces a single permission boundary.)

---

## 11. Reason-code inventory

Stable across V1; ICU keys live in `Modules/Pricing/Messages/pricing.commercial.{en,ar}.icu`. The canonical owned set below totals **49 codes** across 6 sub-namespaces (`coupon.*`, `promotion.*`, `business_pricing.*`, `campaign.*`, `preview*.*`, `commercial.*`). The trailing block enumerates engine-emitted `pricing.*` codes for cross-reference; those are owned by spec 007-a, not 007-b.

```
coupon.code.duplicate
coupon.label.required_bilingual
coupon.schedule.invalid_window
coupon.value.invalid
coupon.activation.requires_approval
coupon.locked.active_pricing_field
coupon.expired.read_only
coupon.clone.source_invalid

promotion.overlap.warning
promotion.applies_to.too_many
promotion.locked.active_pricing_field
promotion.target_sku_invalid
promotion.expired.read_only

business_pricing.below_cogs.warning
business_pricing.row.duplicate
business_pricing.company_invalid

campaign.link.target_expired
campaign.link.coupon_not_displayable
campaign.link.invalid_kind
campaign.markets.empty_or_invalid
campaign.expired.read_only

preview_profile.shared.requires_promotion
preview_profile.promotion.requires_approver
preview_profile.already_shared
preview.profile.not_found
preview.profile.sku_archived
preview.rule.invalid_body

commercial.markets.empty_or_invalid
commercial.usage_limit.zero_forbidden
commercial.deactivation.reason_required
commercial.deactivation.invalid_state
commercial.reactivation.expired_terminal
commercial.reactivation.invalid_state
commercial.row.version_conflict
commercial.row.delete_forbidden
commercial.schedule.invalid_state
commercial.business_pricing.forbidden
commercial.bulk_import.row_invalid
commercial.bulk_import.snapshot_changed
commercial.bulk_import.token_expired
commercial.bulk_import.invalid_header
commercial.self_approval.forbidden
commercial.approval.note_too_short
commercial.approval.already_recorded
commercial.approval.gate_not_required
commercial.threshold.forbidden
commercial.threshold.grace_out_of_range
commercial.threshold.percent_out_of_range
commercial.threshold.amount_negative
commercial.threshold.duration_negative
commercial.thresholds.unconfigured
commercial.rate_limit_exceeded

pricing.target.archived (advisory only — never a hard error; surfaces in PriceExplanation.advisory[])
pricing.coupon.deactivated (engine-emitted; not a 007-b API code)
pricing.coupon.expired (engine-emitted)
pricing.promotion.deactivated (engine-emitted)
pricing.promotion.expired (engine-emitted)
pricing.coupon.suppressed_by_promotion_no_stack (engine-emitted)
```

(The 6 trailing `pricing.*` lines are engine-emitted by spec 007-a — listed here for cross-reference only and excluded from the 49-code 007-b-owned total.)

---

## 12. Cross-module shared interfaces

Declared in `Modules/Shared/`. See data-model §7 for full signatures.

| Interface | Direction | Producer | Consumer |
|---|---|---|---|
| `ICatalogSkuArchivedSubscriber` | inbound | spec 005 | spec 007-b |
| `ICatalogSkuArchivedPublisher` | outbound | spec 005 | spec 007-b's fake (testing) |
| `IB2BCompanySuspendedSubscriber` | inbound | spec 021 | spec 007-b |
| `IB2BCompanySuspendedPublisher` | outbound | spec 021 | spec 007-b's fake (testing) |
| `ICheckoutGraceWindowProvider` | inbound | spec 007-b | spec 010 (rare; ad-hoc) |

---

## 13. Domain-event payloads

See data-model §6 for the complete set of 10 event types and their payloads.

---

## 14. OpenAPI artifact

Generated to `services/backend_api/openapi.pricing.commercial.json` via `dotnet swagger tofile` on every PR build (R18). The artifact is checked into the repository for diff review; consumer projects (admin web 015) regenerate clients from this file.

---

## 15. Versioning

This contract is **v1** at launch. Backward-incompatible changes require:
1. A new `/v2/admin/commercial/...` parallel surface (per spec 003 versioning rules).
2. A deprecation notice in the spec 015 admin UI.
3. A 90-day overlap window before v1 retirement.

Additive changes (new fields, new endpoints, new reason codes) are non-breaking and may land in v1 with a minor OpenAPI version bump.
