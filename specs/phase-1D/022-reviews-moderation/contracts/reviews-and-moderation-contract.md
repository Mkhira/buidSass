# API Contract: Reviews & Moderation (Spec 022 · Phase 1D)

**Date**: 2026-04-28
**Inputs**: spec.md, plan.md, research.md, data-model.md (this directory).
**Generated artifact**: `services/backend_api/openapi.reviews.json` (regenerated each PR).

This contract enumerates every HTTP endpoint, every request body, every response body, every reason code, every domain event, and every cross-module interface introduced by spec 022. It is the single source of truth consumed by the contract-test suite (`tests/Reviews.Tests/Contract/`).

**Conventions across all sections**:
- Customer base path: `/v1/customer/reviews/`. Admin base path: `/v1/admin/reviews/`. Public aggregate read: `/v1/public/reviews/aggregates/`.
- All POST / PUT / PATCH require `Idempotency-Key: <uuid-v4>` header.
- All mutations on existing rows require `If-Match: <row_version>`. Mismatch → `409 reviews.moderation.version_conflict` (or `review.row.version_conflict` for the customer-edit path).
- All responses use `application/json; charset=utf-8`.
- Money + integer IDs as standard.
- Customer paths require auth (spec 004 cookie / bearer); admin paths require auth + RBAC; public aggregate path is unauthenticated.
- Per-customer customer-write rate-limits: 5 / hour / customer for submission, edit, report (separate buckets); 20 / day for submission. Over-limit → `429 review.rate_limit.submission_exceeded` (or per-bucket variants).
- Per-actor admin write rate-limits: 60 decisions / hour. Over-limit → `429 reviews.moderation.rate_limit_exceeded`.

---

## 1. RBAC

| Permission | Granted to | Endpoints |
|---|---|---|
| `customer` (signed-in) | every customer | §2 customer endpoints |
| `reviews.moderator` | new role | §3 admin moderation queue, decisions, admin notes |
| `reviews.policy_admin` | new role | §4 policy-admin (wordlist + market-schema) |
| `super_admin` | spec 015 | implicit superset; only role allowed to decide `deleted` (FR-004) |
| `support` (spec 023) | support agents | read-only on §3 review-detail (no decision power) |
| `viewer.finance` | spec 015 | read-only on every endpoint |
| (none) | unauthenticated | §5 public aggregate read |

---

## 2. Customer endpoints

### 2.1 `POST /v1/customer/reviews` — Submit review

**Auth**: signed-in `customer`.

**Request**:
```json
{
  "product_id": "...",
  "rating": 4,
  "headline": "Great gloves, fit perfectly",
  "body": "I've used these for two weeks across three clinics...",
  "locale": "en",
  "media_urls": ["https://storage.../signed/abc", "https://storage.../signed/def"]
}
```

**Response 201**:
```json
{
  "id": "...",
  "state": "visible",
  "row_version": "...",
  "created_at_utc": "...",
  "pending_review": false
}
```

When the filter trips OR `media_urls.length > 0`, the response is:
```json
{ "id": "...", "state": "pending_moderation", "pending_review": true, ... }
```

**Errors**:
- `400 review.eligibility.no_delivered_purchase` — no delivered, non-refunded order line for `(customer, product)`.
- `400 review.eligibility.refunded` — order line has been refunded.
- `400 review.eligibility.window_closed` — `(now − delivered_at) > eligibility_window_days`.
- `400 review.eligibility.already_reviewed` — existing non-deleted review for `(customer, product)`. Response embeds the existing review id.
- `400 review.headline.length_invalid` (1-100 chars).
- `400 review.body.length_invalid` (1-4000 chars).
- `400 review.rating.out_of_range` (1-5).
- `400 review.locale.invalid` (`ar` or `en`).
- `400 review.media.too_many` (max 4).
- `400 review.media.invalid_signed_url`.
- `429 review.rate_limit.submission_exceeded`.

**Audit**: `review.submitted` (+ `review.published` if state=`visible`, or `review.held_for_moderation` if state=`pending_moderation`).

**Domain events**: `ReviewSubmitted`, then either `ReviewPublished` or `ReviewHeldForModeration`.

---

### 2.2 `PATCH /v1/customer/reviews/{id}` — Edit own review

**Auth**: signed-in `customer`. Customer must be the author.

**Request**: any subset of `{rating, headline, body, locale, media_urls}`.

**Response 200**: review body with new state.

**Errors**:
- `400 review.edit.window_closed` — `(now − created_at) > edit_window_days`.
- `403 review.edit.not_author` — caller is not the review's author.
- `400 review.edit.deleted_terminal` — review is in `deleted` state.
- `409 review.row.version_conflict` — `row_version` mismatch (someone else edited or admin moderated).
- `429 review.rate_limit.edit_exceeded`.

**Audit**: `review.edited` (always); `review.held_for_moderation` if the edit transitions to `pending_moderation`.

---

### 2.3 `GET /v1/customer/reviews/me` — List my reviews

**Auth**: signed-in `customer`. Returns own reviews regardless of state (so the customer can see held + hidden).

**Query**: `state?`, `cursor?`, `limit` (default 20, max 100).

**Response 200**: `{ items, next_cursor? }`. Each item carries `state`, `pending_review` flag, last decision summary if hidden.

---

### 2.4 `GET /v1/customer/reviews/me/{id}` — Read my review

Same as 2.3 narrowed to one id.

---

### 2.5 `POST /v1/customer/reviews/{id}/report` — Report another customer's review

**Auth**: signed-in `customer`. Reporter must NOT be the review's author.

**Request**:
```json
{
  "reason": "personal_attack",
  "note": "..."
}
```

`note` is required when `reason = 'other_with_required_note'`.

**Response 201**: `{ flag_id, qualified: bool, threshold_progress: {qualified_count, threshold} }`.

**Errors**:
- `400 review.report.cannot_report_own_review`.
- `400 review.report.reason_invalid`.
- `400 review.report.note_required` (when reason requires it and note < 10 chars).
- `409 review.report.already_reported_by_actor` — caller has already reported this review (idempotent — does NOT double-count).
- `401 review.report.unauthenticated`.
- `429 review.rate_limit.report_exceeded`.

**Audit**: `review.report_submitted`. **Side-effect**: if the report is qualified and the qualified-count crosses the threshold within the window, the review state advances to `flagged` (audit row `review.flagged`, domain event `ReviewFlagged`).

---

### 2.6 `GET /v1/customer/reviews/report-reasons` — Lookup the 5 fixed reasons + ICU keys

**Auth**: signed-in `customer`. Returns the static list with `i18n_key.ar` + `i18n_key.en`.

---

## 3. Admin moderation endpoints

### 3.1 `GET /v1/admin/reviews/queue` — List moderation queue

**Auth**: `reviews.moderator`.

**Query**: `state?` (`pending_moderation` | `flagged` | `all_attention`), `market_code?`, `triggered_by?`, `community_report_count_min?`, `created_at_range?`, `media_only?`, `cursor?`, `limit` (default 50, max 200).

**Response 200**: `{ items, next_cursor? }`. Each item carries the FR-016 fields (review headline + body truncated, locale, reviewer-display-rendered name, product name, delivered_at, all flag reports, filter_trip_terms[], media_urls inline with `media-pending` flag, audit history summary, prior admin notes, `edited_since_last_surface` indicator).

---

### 3.2 `GET /v1/admin/reviews/{id}` — Review detail

**Auth**: `reviews.moderator` or `support` (read-only) or `viewer.finance` (read-only).

**Response 200**: full review body + audit history + flags + admin notes + the customer's other reviews summary (count, avg rating).

---

### 3.3 `POST /v1/admin/reviews/{id}/decide` — Decide moderation

**Auth**: `reviews.moderator` for `visible` / `hidden`; `super_admin` for `deleted`.

**Request**:
```json
{
  "to_state": "hidden",
  "reason_note": "personal attack on competitor; violates community standard 3.2",
  "admin_note": null
}
```

`reason_note` required when `to_state` is `hidden` or `deleted`. `admin_note` required when `to_state` is `visible` (reinstate). Both ≥ 10 chars.

**Response 200**: review body with new state.

**Errors**:
- `403 reviews.moderation.forbidden` — caller lacks `reviews.moderator`.
- `403 reviews.moderation.delete_requires_super_admin` — caller has `reviews.moderator` but not `super_admin` and is attempting `deleted`.
- `400 reviews.moderation.reason_required` — note missing or < 10 chars.
- `400 reviews.moderation.invalid_state` — `to_state` not reachable from current state.
- `400 reviews.moderation.delete_terminal` — current state is `deleted` (terminal).
- `409 reviews.moderation.version_conflict` — `row_version` mismatch (customer edited mid-flight).
- `429 reviews.moderation.rate_limit_exceeded`.

**Audit**: one of `review.published`, `review.hidden`, `review.deleted`, `review.reinstated`. **Side-effect**: rating aggregate refreshes inline.

---

### 3.4 `POST /v1/admin/reviews/{id}/notes` — Add admin note

**Auth**: `reviews.moderator`.

**Request**: `{ note: "..." }` (≥ 10 chars).

**Response 201**: `{ note_id, created_at_utc }`.

**Audit**: `review.admin_note_added`.

---

### 3.5 `GET /v1/admin/reviews/{id}/notes` — List admin notes

Append-only list; ordered by `created_at_utc DESC`.

---

### 3.6 `GET /v1/admin/reviews/by-customer/{customer_id}` — All reviews by a customer (support / dispute investigation)

**Auth**: `reviews.moderator` or `support`.

**Query**: `state?`, `cursor?`, `limit` (default 50).

---

### 3.7 `DELETE /v1/admin/reviews/{id}` — Forbidden

**Always returns**: `405 review.row.delete_forbidden`. (FR-005a; documented for explicitness.)

---

## 4. Policy-admin endpoints

### 4.1 `GET /v1/admin/reviews/policy/wordlists` — List wordlist terms

**Auth**: `reviews.policy_admin`.

**Query**: `market_code` (required).

**Response 200**: `[{market_code, term, severity?}, ...]`.

### 4.2 `PUT /v1/admin/reviews/policy/wordlists` — Upsert term

**Auth**: `reviews.policy_admin`.

**Request**: `{ market_code, term, severity? }`. Term is normalized + lowercased at write time.

**Response 200**: full row.

**Errors**: `400 reviews.policy.wordlist.term_invalid` (empty or > 200 chars after normalization).

**Audit**: `reviews.wordlist.term_upserted`.

### 4.3 `DELETE /v1/admin/reviews/policy/wordlists` — Delete term

**Request**: `{ market_code, term }`.

**Response 204**.

**Audit**: `reviews.wordlist.term_deleted`.

**Note**: deleting a term does NOT auto-resolve historical `pending_moderation` reviews caused by that term (Edge Cases note in spec). Moderator must resolve manually.

### 4.4 `PATCH /v1/admin/reviews/policy/markets/{market_code}` — Update market schema

**Auth**: `reviews.policy_admin`.

**Request** (any subset of):
```json
{
  "eligibility_window_days": 180,
  "edit_window_days": 30,
  "community_report_threshold": 3,
  "community_report_window_days": 30,
  "report_qualifying_account_age_days": 14,
  "report_qualifying_requires_verified_buyer": true,
  "pending_moderation_sla_hours": 168
}
```

**Errors**:
- `400 reviews.policy.market.value_out_of_range` — value outside the per-field check-constraint range.
- `403 reviews.policy.forbidden`.

**Audit**: `reviews.market_schema_updated`.

---

## 5. Public aggregate read

### 5.1 `GET /v1/public/reviews/aggregates/{product_id}` — Read product rating aggregate

**Auth**: NONE (unauthenticated). FR-029.

**Query**: `market_code` (required).

**Response 200**:
```json
{
  "product_id": "...",
  "market_code": "SA",
  "avg_rating": 4.32,
  "review_count": 47,
  "distribution": {"1": 1, "2": 2, "3": 5, "4": 14, "5": 25},
  "last_updated_utc": "..."
}
```

When `review_count = 0`, `avg_rating` is `null` (FR-028).

**Cache header**: `Cache-Control: public, max-age=60`.

**Errors**: `400 reviews.aggregate.market_invalid` — unknown `market_code`.

---

### 5.2 `GET /v1/public/reviews/aggregates` (batch) — Read many aggregates (consumed by spec 006 search-result decoration)

**Query**: `product_ids=<csv>&market_code=<code>` (max 100 ids per call).

**Response 200**: `{ items: [...] }` array of the same shape as 5.1.

---

## 6. Cross-module shared interfaces

Declared in `Modules/Shared/`. See data-model §7 for full signatures.

| Interface | Direction | Producer | Consumer |
|---|---|---|---|
| `IOrderLineDeliveryEligibilityQuery` | inbound | spec 011 | spec 022 |
| `IRefundCompletedSubscriber` / `Publisher` | inbound | spec 013 | spec 022 |
| `IRefundReversedSubscriber` / `Publisher` | inbound | spec 013 | spec 022 |
| `IProductDisplayLookup` | inbound | spec 005 | spec 022 |
| `IRatingAggregateReader` | outbound | spec 022 | spec 005 product detail, spec 006 search |
| `IReviewDisplayHandleQuery` | inbound | spec 019 | spec 022 |
| `ICustomerAccountLifecycleSubscriber` (existing) | inbound | spec 020 declares; spec 004 publishes | spec 022 (new handler in `CustomerAccountLifecycleHandler.cs`) |

---

## 7. Domain-event payloads

See data-model §6 for the complete set of 8 event types and their payloads.

---

## 8. Versioning

This contract is **v1** at launch. Backward-incompatible changes require:
1. A new `/v2/` parallel surface (per spec 003 versioning rules).
2. Deprecation notice in spec 015 admin UI.
3. 90-day overlap window before v1 retirement.

Additive changes (new fields, new endpoints, new reason codes) are non-breaking and may land in v1 with a minor OpenAPI version bump.

---

## 9. OpenAPI artifact

Generated to `services/backend_api/openapi.reviews.json` via `dotnet swagger tofile` on every PR build. Checked in for diff review.

---

## 10. Reason-code inventory (canonical list)

ICU keys live in `Modules/Reviews/Messages/reviews.{en,ar}.icu`. ~35 owned codes.

**Namespace convention** (deliberate; not drift): codes prefixed `review.*` describe entity-level actions on a single `Review` row (e.g., `review.submitted`, `review.flagged`, `review.deleted` — these double as audit-event kinds). Codes prefixed `reviews.*` describe module-level concerns: RBAC role names (`reviews.moderator`, `reviews.policy_admin`), market-schema operations (`reviews.market_schema_updated`), and wordlist administration (`reviews.wordlist.term_upserted`, `reviews.wordlist.term_deleted`). Same pattern as `pricing.*` (module) vs `coupon.*` (entity) in spec 007-b.



```
review.eligibility.no_delivered_purchase
review.eligibility.refunded
review.eligibility.window_closed
review.eligibility.already_reviewed
review.headline.length_invalid
review.body.length_invalid
review.rating.out_of_range
review.locale.invalid
review.media.too_many
review.media.invalid_signed_url
review.edit.window_closed
review.edit.not_author
review.edit.deleted_terminal
review.row.version_conflict
review.row.delete_forbidden
review.report.cannot_report_own_review
review.report.reason_invalid
review.report.note_required
review.report.already_reported_by_actor
review.report.unauthenticated

review.rate_limit.submission_exceeded
review.rate_limit.edit_exceeded
review.rate_limit.report_exceeded

reviews.moderation.forbidden
reviews.moderation.delete_requires_super_admin
reviews.moderation.reason_required
reviews.moderation.invalid_state
reviews.moderation.delete_terminal
reviews.moderation.version_conflict
reviews.moderation.rate_limit_exceeded

reviews.policy.forbidden
reviews.policy.wordlist.term_invalid
reviews.policy.market.value_out_of_range

reviews.aggregate.market_invalid
```
