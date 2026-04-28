# Research: Reviews & Moderation (Spec 022 · Phase 1D)

**Date**: 2026-04-28
**Inputs**: spec.md (this directory), plan.md (this directory), specs/phase-1D/{020-verification, 021-quotes-and-b2b, 007-b-promotions-ux-and-campaigns}/research.md, constitution v1.0.0, ADR-001/003/004/010/022/023, project-memory rules.

This document resolves the design unknowns surfaced during plan authoring. Every NEEDS-CLARIFICATION marker that would otherwise have surfaced has been addressed inline below. Each section follows the spec-kit format: **Decision · Rationale · Alternatives considered · Verification hook**.

---

## R1. Eligibility query — read-only contract owned by spec 011 vs in-process replication

**Decision**: Declare `IOrderLineDeliveryEligibilityQuery` in `Modules/Shared/`; spec 011 implements. The query signature is `Task<EligibilityResult> IsEligibleForReview(Guid customerId, Guid productId, CancellationToken ct)` returning `{ eligible: bool, reason_code?: string, delivered_at?: DateTimeOffset, order_line_id?: Guid }`. 022 calls this from the `SubmitReview` handler in the same MediatR scope. No data is replicated into the reviews schema beyond `order_line_id` (stored on the `reviews.reviews` row for audit traceability).

**Rationale**: Spec 011 owns order-line lifecycle (delivered, refunded states); duplicating that query in 022 would create two truths. The interface lives in `Modules/Shared/` so 022 doesn't take a compile-time dependency on `Modules/Orders/` (project-memory rule). Returning `delivered_at` lets 022 compute the eligibility-window check (FR-007) without a second cross-module call.

**Alternatives considered**:
- **Replicate order-line state into a 022-side read model**: invites divergence and a CDC-style sync job for no real benefit at V1 scale.
- **Pass eligibility result from the customer client**: no — clients can't be trusted with eligibility decisions.
- **Compute eligibility in 022 directly against the `orders` schema**: hard cross-module dependency; project-memory rule forbids.

**Verification hook**: integration test `SubmitReview_RejectsCustomerWithoutDeliveredOrderLine` uses a fake `IOrderLineDeliveryEligibilityQuery` returning `{eligible:false, reason_code:"review.eligibility.no_delivered_purchase"}`; asserts the handler returns the error.

---

## R2. Refund-event subscription — in-process bus pattern reused from spec 020 / 021 / 007-b

**Decision**: Declare `IRefundCompletedSubscriber` + `IRefundCompletedPublisher` (and `IRefundReversedSubscriber` + `IRefundReversedPublisher`) in `Modules/Shared/`. Spec 013 publishes; 022 implements the subscribers in `Modules/Reviews/Subscribers/`. The bus is the same in-process MediatR `INotification` channel used by specs 020 / 021 / 007-b. Idempotency is per-event: each subscriber checks "has this review already been auto-hidden by this refund event?" via the `triggered_by_event_id` column and no-ops if true.

**Rationale**: Same pattern, same channel, same testability harness. The subscriber rebuilds the rating aggregate in the same transaction.

**Alternatives considered**:
- **Outbox + DB-poll**: overkill for an in-process concern.
- **Direct dependency on `Modules/Returns`**: forces 013 to take a 022 dependency at compile time — circular.

**Verification hook**: integration test `RefundCompleted_AutoHidesAffectedReviews` emits a fake `RefundCompletedEvent`; asserts every `visible`/`flagged` review on the affected order line transitions to `hidden` within 60 s and the rating aggregate is recomputed.

---

## R3. Aggregate refresh strategy — immediate-on-transition + daily reconciliation worker

**Decision**: Two-path refresh. (1) **Immediate path**: every `RatingAggregateRecomputer.RefreshAsync(productId, marketCode, ct)` call runs in the same transaction as the lifecycle transition that triggered it (or the first transaction that can hold the affected aggregate row's lock). The recomputer rebuilds `avg_rating`, `review_count`, and `distribution[1..5]` from a single SQL query against `reviews.reviews` filtered to `state IN ('visible','flagged')`. Targets ≤ 5 s p95. (2) **Reconciliation path**: `RatingAggregateRebuildWorker` runs daily at 03:00 UTC, advisory-lock-guarded, recomputes every `(product_id, market_code)` aggregate from scratch. Catches misses from process crash, transactional rollback after publish, etc.

**Rationale**: SC-005 demands ≤ 60 s drift. Immediate-only is fast but unreliable. Reconciliation-only blows the SLA. Both together is reliable.

**Alternatives considered**:
- **Materialized view + `REFRESH MATERIALIZED VIEW CONCURRENTLY`**: PG materialized views can't be incremental; full refresh is too expensive for the hot path.
- **CDC-driven projection**: too operationally heavy for the V1 scale (5 000 reviews / day).
- **Compute-on-read**: blows the 50 ms p95 read budget at scale.

**Verification hook**: integration test `Aggregate_RefreshesWithin60s_AfterTransition` asserts the aggregate's `last_updated_utc` advances within 60 s of any countable transition, measured over 100 random transitions in a soak test.

---

## R4. Profanity filter — Arabic-normalization reuse from spec 006 search

**Decision**: 022's `ProfanityFilter` consumes `IArabicNormalizer` declared in `Modules/Search/Internal/IArabicNormalizer.cs` (made public for cross-module consumption — small interface change to spec 006; coordinate with spec 006 owner). The filter applies the normalizer to both the input text and the wordlist terms, then runs a Boyer-Moore-style multi-pattern match. Wordlist is loaded into in-process memory at boot (≤ 100 terms per market expected) and refreshed every 60 s via a polling subscriber to a `commercial.threshold_changed`-style domain event from `Modules/Reviews/PolicyAdmin/` whenever the wordlist is mutated.

**Rationale**: Don't reimplement Arabic normalization (spec 006 owns it; same rules ensure consistent behavior across search + reviews). In-process wordlist with 60 s refresh is fast enough for V1 scale without a Redis cache.

**Alternatives considered**:
- **Reimplement normalization in 022**: divergence risk; AR rules are subtle.
- **Per-request DB query for wordlist**: hot-path cost for a wordlist that changes rarely.
- **Postgres FTS / regex for matching**: less control over the matched-term return shape (we need to surface matched terms in `filter_trip_terms[]` for moderator visibility).

**Verification hook**: unit test `ProfanityFilter_NormalizesAndMatchesAr_BoundaryCases` covers AR ligature, hamza variations, and the existing spec 006 search test corpus.

---

## R5. Qualified-reporter evaluation — at report time, not at threshold-check time

**Decision**: When a customer submits a `ReviewFlag`, the handler calls `QualifiedReporterPolicy.Evaluate(reporter, marketSchema)` and stores the boolean result on the `ReviewFlag.is_qualified` column. The threshold check (FR-023) reads this stored value, NOT the live reporter state. The threshold counter is computed by `SELECT COUNT(*) FROM reviews.review_flags WHERE review_id = $1 AND is_qualified = true AND created_at >= now() - interval '30 days'`.

**Rationale**: Reproducibility (Clarification Q4 lock). The audit trail must answer "why did this review trip the threshold?" with the same answer in 6 months as today. Re-evaluating at threshold-check time would mean a reporter who completes their first purchase mid-window retroactively becomes qualifying — non-deterministic.

**Alternatives considered**:
- **Re-evaluate at threshold-check time**: non-deterministic; audit-incompatible.
- **Re-evaluate at every report's anniversary**: complex and unjustified.

**Verification hook**: integration test `QualifiedReporter_StoredAtReportTime_RemainsStableAcrossPolicyChanges`.

---

## R6. Reviewer-display rule — computed at read time

**Decision**: `ReviewerDisplayRenderer.Render(displayHandle?, firstName, lastName) → string` is a pure function. Computed at every read site (storefront review feed, moderator queue, review detail). The reviewer's `first_name`, `last_name`, `review_display_handle?` are looked up via `IReviewDisplayHandleQuery` (spec 019 implements). 022 does NOT mirror these fields into its tables.

**Rationale**: Clarification Q5 lock. A customer changing their name in spec 019's profile should apply retroactively to all their reviews without a backfill job.

**Alternatives considered**:
- **Denormalize display name onto each review**: forces a backfill on every name change.
- **Cache the rendered display in 022 with TTL**: adds a cache-invalidation concern for negligible benefit.

**Verification hook**: integration test `ReviewerDisplay_ChangedHandle_AppliesToHistoricalReviews_AfterRead`.

---

## R7. Single-locale review content — storage and rendering

**Decision**: The `reviews.reviews` row carries `headline` (TEXT) + `body` (TEXT) + `locale` (`'ar' | 'en'`). The unauthored locale is NOT stored (no `headline_other`, `body_other` columns). Every storefront read returns the headline + body as-is, accompanied by `locale`. The storefront renderer (owned by spec 014) prepends the `Original written in {locale}` annotation when the viewer's locale differs from `locale`.

**Rationale**: Clarification Q1 of /speckit-clarify lock. Two columns per locale would invite machine-translation pressure (Principle 4 forbids).

**Alternatives considered**:
- **Two columns + nullable**: invites someone to fill the other side later; project-wide drift risk.
- **Locale-keyed jsonb `{ ar?: ..., en?: ... }`**: same drift risk.

**Verification hook**: schema test `Reviews_NoSecondLocaleColumns_ExistOnSchema`.

---

## R8. Media moderation — auto-hold on attachment

**Decision**: The `SubmitReview` and `UpdateReview` handlers call `MediaAttachmentDetector.HasMedia(review)` (returns `media_urls.Length > 0`). If true, the review is forced into `pending_moderation` regardless of text-filter result, with a separate `media_attachment_review_required` boolean column set to `true`. Moderator queue surfaces both tripped-text + media-pending as distinct badges.

**Rationale**: Clarification Q2 lock. Image classification is out of scope; conservative human review is the safe default.

**Alternatives considered**: documented in plan's Complexity Tracking.

**Verification hook**: integration test `Submit_WithMediaButCleanText_HoldsForModeration`.

---

## R9. Edits during pending — re-stamp + xmin invalidation

**Decision**: The `UpdateReview` handler:
1. Loads the review with `xmin` row_version.
2. Validates the customer's edit-window (FR-009).
3. Re-runs `ProfanityFilter` on the updated body + `MediaAttachmentDetector` on the updated media list.
4. If filter trips OR media changed, sets `state = pending_moderation`, `pending_moderation_started_at = now()`, increments `edit_count`.
5. Persists; EF Core's optimistic-concurrency advances `xmin`.
6. Audit row carries `triggered_by = customer_edit` plus `before_jsonb` / `after_jsonb`.

The `DecideModeration` handler reads the review's current `xmin` and includes it in the `If-Match` header. Mid-flight edit by the customer advances `xmin`; the moderator's decision returns `409 reviews.moderation.version_conflict` with the latest review body embedded.

**Rationale**: Clarification Q3 lock. Pure EF Core idiom; no custom locking.

**Alternatives considered**:
- **Block edits during pending**: documented as rejected in plan's Complexity Tracking.
- **Allow edits, ignore moderator stale-version**: race condition; potentially approves text the customer has already removed.

**Verification hook**: concurrency test `EditDuringPending_InvalidatesModeratorDecision_With409`.

---

## R10. Audit-event-kind list

**Decision**: 14 audit-event kinds documented in `data-model.md §5`. Each `Review` lifecycle transition produces one canonical row in `audit_log_entries` (spec 003) AND one denormalized row in `reviews.review_moderation_decisions` (immediate-read for moderator UI).

**Rationale**: Same dual-write pattern as 007-b (R5).

**Alternatives considered**: same as 007-b R5.

**Verification hook**: contract test `AuditCoverage_AllReviewKindsReachable`.

---

## R11. Hard-delete prohibition + 405 envelope

**Decision**: The route `DELETE /v1/admin/reviews/{id}` is wired to a fixed handler returning `405 review.row.delete_forbidden` with no DB read. Soft `state=deleted` is the only deletion path (FR-005a).

**Rationale**: Same pattern as 007-b's coupon/promotion delete prohibition.

**Alternatives considered**: documented in 007-b R3.

**Verification hook**: contract test `DeleteReviewRoute_Returns405_Always`.

---

## R12. Customer-account-lifecycle reuse

**Decision**: 022 does NOT declare a new account-lifecycle subscriber. It registers a new handler against the existing `ICustomerAccountLifecycleSubscriber` interface from spec 020. The handler iterates the customer's reviews and auto-hides each `visible`/`flagged` row.

**Rationale**: Same lifecycle event; second declaration would create duplicate processing.

**Alternatives considered**: re-declaring the interface (rejected; duplicate processing).

**Verification hook**: integration test `AccountLocked_AutoHidesAuthoredReviews`.

---

## R13. Profanity wordlist — refresh strategy

**Decision**: The `ProfanityFilter` keeps an in-process cache of all wordlist terms per market. Cache is refreshed every 60 s by a polling background loop (or on-demand when an admin updates the wordlist via `UpsertWordlistTerm` / `DeleteWordlistTerm` — those handlers publish a `WordlistUpdatedEvent` that the filter consumes for instant invalidation). At V1 expected wordlist size (≤ 100 terms / market), in-memory storage is trivially small.

**Rationale**: Spec 015's settings UI is not designed for hot-path access; in-process cache eliminates a per-submission DB round-trip while keeping <60 s freshness.

**Alternatives considered**:
- **Per-request DB lookup**: hot-path cost.
- **Redis cache**: extra dependency for ≤ 100 rows.
- **Bloom filter**: overkill for ≤ 100 terms.

**Verification hook**: integration test `Wordlist_NewTerm_TripsFilter_Within60s`.

---

## R14. EF Core warning suppression and DI scope (project-memory rule)

**Decision**: `ReviewsModule.cs` MUST suppress `ManyServiceProvidersCreatedWarning` per the project-memory rule. The CI grep guard introduced for spec 007-b is generalized to scan every `*Module.cs` file under `Modules/`; failing CI if the suppression line is missing.

**Rationale**: Project-memory rule.

**Verification hook**: build-time grep in CI.

---

## R15. OpenAPI artifact convention

**Decision**: Generate `services/backend_api/openapi.reviews.json` via `dotnet swagger tofile` on every PR build. Same convention as `openapi.b2b.json` (021), `openapi.pricing.commercial.json` (007-b).

**Verification hook**: PR diff against the regenerated file.

---

## R16. Rate-limit envelope

**Decision**: Customer review-submission + edit + report writes are rate-limited per `customer_id`: 5 / hour and 20 / day for submissions; 5 / hour for edits; 5 / hour for reports. Admin moderation actions: 60 / hour / actor. Implemented via the existing spec 003 rate-limit middleware with a `policy: "reviews.customer.write"` and `policy: "reviews.admin.write"` registration.

**Verification hook**: integration test `Submit_OverLimit_Returns429`.

---

## Open items deferred (with justification)

- **Image-classifier hook for V1.5+** — explicit deferral per Clarification Q2 of /speckit-clarify and spec.md Out of Scope.
- **Customer Q&A on product detail** — separate concept; deferred to Phase 1.5.
- **Review syndication to external platforms (Trustpilot, Google)** — Out of Scope.
- **AI-driven review summarization / sentiment** — Phase 2.
- **`reviews.translator` role for second-locale editorial pass** — explicit Phase 1.5 deferral per Clarification Q1 of /speckit-clarify.
- **Per-product-vendor scoping** — V1 single-vendor; `vendor_id` reserved.
- **Photo-only reviews (no text body)** — V1 requires text body (FR-006); deferred.
- **Helpful / unhelpful voting on reviews** — Phase 1.5.

---

## Cross-spec consistency checks

- **Spec 020 alignment**: `ICustomerAccountLifecycleSubscriber` reused (R12). Lifecycle audit-event pattern matches.
- **Spec 021 alignment**: append-only audit-detail table pattern matches.
- **Spec 007-b alignment**: hard-delete-forbidden envelope (R11), aggregate-refresh dual-path pattern (R3, mirrors 007-b's broken-reference auto-deactivation worker), Arabic-normalizer reuse pattern.
- **Spec 011 alignment**: `IOrderLineDeliveryEligibilityQuery` interface name + signature must be agreed with 011 owner; declared here, implemented there.
- **Spec 013 alignment**: `IRefundCompleted/ReversedSubscriber+Publisher` interface names + payloads agreed with 013 owner.
- **Spec 005 alignment**: `IProductDisplayLookup` interface signature agreed with 005 owner.
- **Spec 006 alignment**: `IArabicNormalizer` made public (small interface visibility change in 006); coordinate with 006 owner.
- **Spec 019 alignment**: `IReviewDisplayHandleQuery` and the new `review_display_handle` field agreed with 019 owner.
- **Spec 025 alignment**: 8 domain events (`ReviewSubmitted/Published/HeldForModeration/Flagged/Hidden/Deleted/Reinstated/AutoHidden`) subscribed by 025.
- **Spec 023 alignment**: dispute path documented; 023 ticket creation against a review id is a 023-side concern; 022 only exposes the review-state read API consumed by support agents.
- **ADR-022** (PostgreSQL 16): functional indexes (e.g., partial unique on `(customer_id, product_id) WHERE state != 'deleted'`) are PG ≥ 12 features — fine.
