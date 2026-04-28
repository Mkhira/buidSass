# Data Model: Reviews & Moderation (Spec 022 · Phase 1D)

**Date**: 2026-04-28
**Inputs**: spec.md, plan.md, research.md (this directory).
**Scope**: 7 net-new tables in a new `reviews` schema. 1 explicit state machine. 14 audit-event kinds. 8 domain events. 6 cross-module shared interfaces.

---

## 1. ERD

```text
                                ┌─────────────────────────────────┐
                                │ reviews.reviews_market_schemas   │  one row per market
                                │ pk: market_code                  │
                                │ - eligibility_window_days        │
                                │ - edit_window_days               │
                                │ - community_report_threshold     │
                                │ - community_report_window_days   │
                                │ - report_qualifying_account_age_days
                                │ - report_qualifying_requires_verified_buyer
                                └─────────────────────────────────┘
                                                ▲ read-only
                                                │
              ┌─────────────────────────────────────────────────────┐
              │ reviews.reviews                                      │
              │ pk: id (uuid)                                        │
              │ - customer_id, product_id, order_line_id             │
              │ - rating (1-5)                                       │
              │ - headline, body                                     │
              │ - locale ('ar' | 'en')                               │
              │ - media_urls jsonb                                   │
              │ - market_code                                        │
              │ - state (pending_moderation|visible|flagged|hidden|deleted)
              │ - filter_trip_terms[] text                           │
              │ - media_attachment_review_required bool              │
              │ - pending_moderation_started_at?                     │
              │ - edit_count int default 0                           │
              │ - vendor_id?                                         │
              │ - row_version (xmin)                                 │
              │ unique partial: (customer_id, product_id) WHERE state != 'deleted'
              └─────────────────────────────────────────────────────┘
                ▲                ▲                ▲                ▲
                │ 1 → many        │ 1 → many       │ 1 → many        │ 1 → 1
                │                │                │                │
       ┌────────┴────────┐  ┌────┴──────┐  ┌──────┴───────┐  ┌─────┴──────────────────┐
       │ review_moder...  │  │ review_   │  │ review_admin │  │ product_rating_         │
       │ ation_decisions  │  │ flags     │  │ _notes       │  │ aggregates              │
       │ (append-only)    │  │ (append-  │  │ (append-only)│  │ pk: (product_id,        │
       │                  │  │  only)    │  │              │  │     market_code)        │
       │ - review_id      │  │ - review_ │  │ - review_id  │  │ - avg_rating            │
       │ - actor_id       │  │   id      │  │ - actor_id   │  │ - review_count          │
       │ - actor_role     │  │ - reporter│  │ - note text  │  │ - distribution[1..5]    │
       │ - from_state     │  │   _id     │  │ - created_at │  │ - last_updated_utc      │
       │ - to_state       │  │ - reason  │  │              │  │ - vendor_id?            │
       │ - reason_note?   │  │ - note?   │  │              │  └────────────────────────┘
       │ - admin_note?    │  │ - is_     │  │              │
       │ - triggered_by   │  │   qualifi │  │              │
       │ - created_at     │  │   ed (bool│  │              │
       └─────────────────┘  │   captured│  │              │
                            │   at        │  │              │
                            │   report)   │  │              │
                            │ - created_at│  │              │
                            │ unique:     │  │              │
                            │ (review_id, │  │              │
                            │  reporter_  │  │              │
                            │  id)        │  │              │
                            └────────────┘  │              │

                            ┌────────────────────────────────┐
                            │ reviews.reviews_filter_wordlists│
                            │ pk: (market_code, term)         │
                            │ - severity?                     │
                            │ - created_by, created_at        │
                            └────────────────────────────────┘

                                                ▲
                                                │ written to (canonical)
                                ┌─────────────────────────────────────┐
                                │ shared.audit_log_entries (spec 003)  │
                                └─────────────────────────────────────┘
```

---

## 2. Tables

### 2.1 `reviews.reviews` — NEW

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK, default `gen_random_uuid()` |  |
| `customer_id` | `uuid` | NOT NULL | FK to `identity.customers`, app-layer enforced |
| `product_id` | `uuid` | NOT NULL | FK to spec 005 catalog, app-layer enforced |
| `order_line_id` | `uuid` | NOT NULL | proof-of-purchase pointer |
| `market_code` | `text` | NOT NULL, CHECK `IN ('SA','EG')` |  |
| `rating` | `int` | NOT NULL, CHECK `1 <= rating <= 5` |  |
| `headline` | `text` | NOT NULL, CHECK `length(headline) BETWEEN 1 AND 100` |  |
| `body` | `text` | NOT NULL, CHECK `length(body) BETWEEN 1 AND 4000` |  |
| `locale` | `text` | NOT NULL, CHECK `IN ('ar','en')` | authoring locale |
| `media_urls` | `jsonb` | NOT NULL, default `'[]'::jsonb`, CHECK `jsonb_array_length(media_urls) <= 4` | signed-URL list from spec 015 storage |
| `state` | `reviews.review_state` enum | NOT NULL, default `'visible'` | initial may be `pending_moderation` if filter trips or media attached |
| `state_changed_at_utc` | `timestamptz` | NOT NULL, default `now()` |  |
| `state_changed_by_actor_id` | `uuid` | NOT NULL | system actor for system transitions |
| `state_changed_reason_note` | `text` | NULL | required on `hidden`/`deleted` per FR-003 |
| `state_changed_admin_note` | `text` | NULL | required on `visible` reinstate per FR-003 |
| `triggered_by` | `text` | NOT NULL, CHECK `IN ('customer_submission','customer_edit','community_report_threshold','refund_event','account_locked','moderator_action','manual_super_admin')` |  |
| `pending_moderation_started_at` | `timestamptz` | NULL | set when state becomes `pending_moderation`; re-stamped on every edit per FR-009 |
| `filter_trip_terms` | `text[]` | NOT NULL, default `'{}'::text[]` | matched wordlist terms; admin-only field |
| `media_attachment_review_required` | `bool` | NOT NULL, default `false` | FR-014a |
| `edit_count` | `int` | NOT NULL, default 0 |  |
| `created_at_utc` | `timestamptz` | NOT NULL, default `now()` | for edit-window computation |
| `delivered_at_utc` | `timestamptz` | NOT NULL | snapshot of order-line delivery time at submission, for eligibility-window audit |
| `vendor_id` | `uuid` | NULL, INDEXED | always null in V1 |
| `row_version` | `xid` (system column `xmin` mapped) | system | EF Core `IsRowVersion()` |

**Unique constraint**: `ux_reviews_customer_product_active` UNIQUE PARTIAL on `(customer_id, product_id)` WHERE `state != 'deleted'` — enforces FR-008 one-review-per-(customer, product).

**Indexes**: `idx_reviews_state` partial WHERE `state IN ('pending_moderation','flagged')`; `idx_reviews_product_market_state` on `(product_id, market_code, state)` for aggregate recompute; `idx_reviews_customer_state` on `(customer_id, state)` for "list my reviews"; `idx_reviews_market_pending_age` on `(market_code, pending_moderation_started_at)` partial WHERE `state = 'pending_moderation'` for queue ordering + SLA.

### 2.2 `reviews.review_moderation_decisions` — NEW (append-only)

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK |  |
| `review_id` | `uuid` | NOT NULL | FK to reviews.reviews(id) |
| `actor_id` | `uuid` | NOT NULL | `'00000000...'` for `'system'` |
| `actor_role` | `text` | NOT NULL | `customer`, `reviews.moderator`, `super_admin`, `system` |
| `from_state` | `reviews.review_state` | NOT NULL |  |
| `to_state` | `reviews.review_state` | NOT NULL |  |
| `triggered_by` | `text` | NOT NULL — same enum as 2.1 |  |
| `reason_note` | `text` | NULL — required on `hidden`/`deleted` |  |
| `admin_note` | `text` | NULL — required on `visible` reinstate |  |
| `before_jsonb` | `jsonb` | NULL | snapshot of body/headline at this transition |
| `after_jsonb` | `jsonb` | NULL |  |
| `correlation_id` | `uuid` | NULL | request-scoped |
| `created_at_utc` | `timestamptz` | NOT NULL, default `now()` |  |

**Append-only trigger**: `BEFORE UPDATE OR DELETE ON reviews.review_moderation_decisions FOR EACH ROW EXECUTE FUNCTION raise_immutable_audit_violation()`. Same pattern as spec 020 / 021 / 007-b.

Indexes: `idx_rmd_review` on `(review_id, created_at_utc DESC)`; `idx_rmd_actor` on `(actor_id, created_at_utc DESC)`.

### 2.3 `reviews.review_admin_notes` — NEW (append-only)

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK |  |
| `review_id` | `uuid` | NOT NULL |  |
| `actor_id` | `uuid` | NOT NULL |  |
| `note` | `text` | NOT NULL, CHECK `length(note) BETWEEN 1 AND 4000` |  |
| `created_at_utc` | `timestamptz` | NOT NULL, default `now()` |  |

Append-only via trigger. Indexes: `idx_ran_review` on `(review_id, created_at_utc DESC)`.

### 2.4 `reviews.review_flags` — NEW (append-only)

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `uuid` | PK |  |
| `review_id` | `uuid` | NOT NULL |  |
| `reporter_actor_id` | `uuid` | NOT NULL |  |
| `reason` | `text` | NOT NULL, CHECK `IN ('inappropriate_language','spam_or_irrelevant','personal_attack','false_or_misleading','other_with_required_note')` |  |
| `note` | `text` | NULL — required when `reason = 'other_with_required_note'` (CHECK enforced) |  |
| `is_qualified` | `bool` | NOT NULL | captured at report time per R5 / FR-023 |
| `qualifying_evaluation_jsonb` | `jsonb` | NOT NULL | `{ account_age_days, has_delivered_order: bool, schema_id }` snapshot |
| `created_at_utc` | `timestamptz` | NOT NULL, default `now()` |  |

**Unique constraint**: `ux_review_flags_review_reporter` UNIQUE on `(review_id, reporter_actor_id)` — enforces FR-022 idempotent reporting.

Indexes: `idx_rf_review_qualified_recent` on `(review_id, is_qualified, created_at_utc DESC)` for FR-023 threshold check; `idx_rf_reporter` on `(reporter_actor_id, created_at_utc DESC)`.

### 2.5 `reviews.product_rating_aggregates` — NEW (denormalized)

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `product_id` | `uuid` | PK part 1 |  |
| `market_code` | `text` | PK part 2, CHECK `IN ('SA','EG')` |  |
| `avg_rating` | `numeric(3,2)` | NULL | NULL when `review_count = 0` (FR-028) |
| `review_count` | `int` | NOT NULL, default 0 |  |
| `distribution_1` | `int` | NOT NULL, default 0 |  |
| `distribution_2` | `int` | NOT NULL, default 0 |  |
| `distribution_3` | `int` | NOT NULL, default 0 |  |
| `distribution_4` | `int` | NOT NULL, default 0 |  |
| `distribution_5` | `int` | NOT NULL, default 0 |  |
| `last_updated_utc` | `timestamptz` | NOT NULL, default `now()` |  |
| `vendor_id` | `uuid` | NULL, INDEXED | always null in V1 |

Indexes: `idx_pra_last_updated` for the reconciliation worker's "stale-aggregate" scan.

### 2.6 `reviews.reviews_filter_wordlists` — NEW

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `market_code` | `text` | PK part 1, CHECK `IN ('SA','EG')` |  |
| `term` | `text` | PK part 2, lowercased + Arabic-normalized at write time |  |
| `severity` | `text` | NULL, CHECK `IN ('block','warn')` if set | reserved for future tiered moderation; V1 treats every match as "trip the filter" |
| `created_by_actor_id` | `uuid` | NOT NULL |  |
| `created_at_utc` | `timestamptz` | NOT NULL, default `now()` |  |

Index: `idx_rfw_market` on `(market_code)` for in-process cache rebuild.

### 2.7 `reviews.reviews_market_schemas` — NEW

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `market_code` | `text` | PK, CHECK `IN ('SA','EG')` |  |
| `eligibility_window_days` | `int` | NOT NULL, CHECK `30 <= value <= 730`, default 180 |  |
| `edit_window_days` | `int` | NOT NULL, CHECK `7 <= value <= 90`, default 30 |  |
| `community_report_threshold` | `int` | NOT NULL, CHECK `1 <= value <= 10`, default 3 |  |
| `community_report_window_days` | `int` | NOT NULL, default 30 |  |
| `report_qualifying_account_age_days` | `int` | NOT NULL, CHECK `0 <= value <= 90`, default 14 |  |
| `report_qualifying_requires_verified_buyer` | `bool` | NOT NULL, default `true` |  |
| `pending_moderation_sla_hours` | `int` | NOT NULL, default 168 (7 days) | for "stale" badge in queue |
| `updated_at_utc` | `timestamptz` | NOT NULL, default `now()` |  |
| `updated_by_actor_id` | `uuid` | NOT NULL |  |
| `row_version` | `xid` | system | xmin |

Seeded by `ReviewsReferenceDataSeeder`. Updates audited.

---

## 3. State machine

### `ReviewState` (5 states)

| State | Meaning | Counted in aggregate? | Customer-visible on storefront? |
|---|---|---|---|
| `pending_moderation` | Held for moderator decision (filter trip OR media attached) | No | No (visible only to author) |
| `visible` | Published; default | Yes | Yes |
| `flagged` | Community-reported; awaiting moderator | Yes | Yes |
| `hidden` | Moderator or system action; reversible | No | No |
| `deleted` | Terminal soft-delete | No | No |

### Transitions

| From | To | Trigger | Actor | Required |
|---|---|---|---|---|
| (none) | `visible` | `customer_submission` (filter clean, no media) | `customer` | eligibility query passes |
| (none) | `pending_moderation` | `customer_submission` (filter trip OR media) | `customer` | eligibility query passes |
| `pending_moderation` | `visible` | `moderator_action` | `reviews.moderator` | admin_note ≥ 10 chars |
| `pending_moderation` | `hidden` | `moderator_action` | `reviews.moderator` | reason_note ≥ 10 chars |
| `pending_moderation` | `pending_moderation` (re-stamp) | `customer_edit` | `customer` | within edit window; re-stamps `pending_moderation_started_at`; advances `xmin` |
| `visible` | `pending_moderation` | `customer_edit` (edit trips filter or media changes) | `customer` | within edit window |
| `visible` | `flagged` | `community_report_threshold` | `system` | qualified-report count ≥ threshold within window |
| `visible` | `hidden` | `moderator_action` | `reviews.moderator` | reason_note |
| `visible` | `hidden` | `refund_event` | `system` | matched order line refunded (FR-030) |
| `visible` | `hidden` | `account_locked` | `system` | author account locked / deleted (FR-031) |
| `flagged` | `visible` | `moderator_action` | `reviews.moderator` | admin_note (false-positive reinstate) |
| `flagged` | `hidden` | `moderator_action` | `reviews.moderator` | reason_note |
| `flagged` | `hidden` | `refund_event` | `system` |  |
| `flagged` | `hidden` | `account_locked` | `system` |  |
| `hidden` | `visible` | `moderator_action` | `reviews.moderator` | admin_note (manual reinstate) |
| `hidden` | `deleted` | `manual_super_admin` | `super_admin` | reason_note ≥ 10 chars |
| `visible` | `deleted` | `manual_super_admin` | `super_admin` | reason_note |
| `flagged` | `deleted` | `manual_super_admin` | `super_admin` | reason_note |
| `pending_moderation` | `deleted` | `manual_super_admin` | `super_admin` | reason_note |
| `deleted` | (no exit) | — | — | terminal (FR-005) |

**Idempotency**: a transition into the current state is a no-op (no audit row, no aggregate refresh).

**Concurrency**: every transition asserts `xmin` row_version; loser receives `409 reviews.moderation.version_conflict`.

**Hard-delete**: forbidden for any state per FR-005a.

---

## 4. Reason codes

The complete owned reason-code surface (~35 codes) is enumerated in `contracts/reviews-and-moderation-contract.md §11`. ICU keys live in `services/backend_api/Modules/Reviews/Messages/reviews.{en,ar}.icu`.

---

## 5. Audit-event kinds (writes to `audit_log_entries` via `IAuditEventPublisher` + the denormalized `review_moderation_decisions` cache)

**14 audit-event kinds**:

| Kind | When |
|---|---|
| `review.submitted` | Customer creates a new review |
| `review.edited` | Customer edits an existing review |
| `review.published` | State → `visible` (initial or moderator approval) |
| `review.held_for_moderation` | State → `pending_moderation` |
| `review.flagged` | State → `flagged` (community threshold) |
| `review.hidden` | State → `hidden` |
| `review.reinstated` | State `hidden`/`flagged` → `visible` |
| `review.deleted` | State → `deleted` |
| `review.auto_hidden` | System-triggered hide (refund or account-locked) |
| `review.report_submitted` | New `ReviewFlag` row |
| `review.admin_note_added` | New `ReviewAdminNote` row |
| `reviews.wordlist.term_upserted` | Wordlist term added or updated |
| `reviews.wordlist.term_deleted` | Wordlist term removed |
| `reviews.market_schema_updated` | `reviews_market_schemas` row updated |

Every audit row carries `actor_id`, `actor_role`, `target_entity_id` (the review), `before_jsonb?`, `after_jsonb?`, `triggered_by`, `correlation_id?`, `recorded_at_utc`.

---

## 6. Domain events (in-process bus → consumed by spec 025)

Declared in `Modules/Shared/ReviewDomainEvents.cs`. All implement `MediatR.INotification`.

| Event | Payload | Subscribers |
|---|---|---|
| `ReviewSubmitted` | `{ review_id, customer_id, product_id, market_code, locale, rating, has_media, was_held }` | spec 025 (customer ack) |
| `ReviewPublished` | `{ review_id, product_id, market_code, rating, transitioned_at_utc }` | spec 025 |
| `ReviewHeldForModeration` | `{ review_id, customer_id, hold_reason: 'filter_trip'|'media_attachment'|'edit_trip', term_count? }` | spec 025 (queue digest), spec 015 |
| `ReviewFlagged` | `{ review_id, qualified_report_count, threshold }` | spec 025, spec 015 |
| `ReviewHidden` | `{ review_id, actor_id, reason_note }` | spec 025 (customer notification) |
| `ReviewDeleted` | `{ review_id, actor_id }` | spec 025 |
| `ReviewReinstated` | `{ review_id, actor_id, prior_state }` | spec 025 |
| `ReviewAutoHidden` | `{ review_id, trigger: 'refund_event'|'account_locked', source_event_id }` | spec 025 (customer notification with appropriate copy) |

**Subscription contract**: spec 025 subscribes via the same in-process bus pattern used by specs 020 / 021 / 007-b. State writes do NOT block on notification success (FR-038). Failure to deliver a notification does NOT roll back the lifecycle transition.

---

## 7. Cross-module contracts (live in `Modules/Shared/`)

```csharp
// Modules/Shared/IOrderLineDeliveryEligibilityQuery.cs
public interface IOrderLineDeliveryEligibilityQuery
{
    Task<EligibilityResult> IsEligibleForReviewAsync(Guid customerId, Guid productId, CancellationToken ct);
}

public sealed record EligibilityResult(
    bool Eligible,
    string? ReasonCode,
    DateTimeOffset? DeliveredAt,
    Guid? OrderLineId);

// Modules/Shared/IRefundCompletedSubscriber.cs / IRefundCompletedPublisher.cs
public sealed record RefundCompletedEvent(
    Guid OrderLineId,
    Guid CustomerId,
    DateTimeOffset CompletedAtUtc,
    Guid ActorId);

// Modules/Shared/IRefundReversedSubscriber.cs / IRefundReversedPublisher.cs
public sealed record RefundReversedEvent(
    Guid OrderLineId,
    Guid CustomerId,
    DateTimeOffset ReversedAtUtc,
    Guid ActorId,
    string ReasonNote);

// Modules/Shared/IProductDisplayLookup.cs
public interface IProductDisplayLookup
{
    Task<ProductDisplay?> GetAsync(Guid productId, string marketCode, string locale, CancellationToken ct);
}

public sealed record ProductDisplay(Guid ProductId, string Name, string ImageUrl, string MarketCode);

// Modules/Shared/IRatingAggregateReader.cs
public interface IRatingAggregateReader
{
    Task<RatingAggregate?> GetAsync(Guid productId, string marketCode, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, RatingAggregate>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds, string marketCode, CancellationToken ct);
}

public sealed record RatingAggregate(
    Guid ProductId,
    string MarketCode,
    decimal? AvgRating,
    int ReviewCount,
    int Dist1, int Dist2, int Dist3, int Dist4, int Dist5,
    DateTimeOffset LastUpdatedUtc);

// Modules/Shared/IReviewDisplayHandleQuery.cs
public interface IReviewDisplayHandleQuery
{
    Task<CustomerDisplayInfo?> GetAsync(Guid customerId, CancellationToken ct);
}

public sealed record CustomerDisplayInfo(
    string FirstName,
    string LastName,
    string? ReviewDisplayHandle); // null when customer has not chosen a handle

// Modules/Shared/ReviewDomainEvents.cs (extracted highlights)
public sealed record ReviewHidden(
    Guid ReviewId,
    Guid ActorId,
    string ReasonNote) : INotification;
// ... 7 other records
```

**Ownership**: spec 011 implements `IOrderLineDeliveryEligibilityQuery`; spec 013 implements `IRefundCompletedPublisher` + `IRefundReversedPublisher`; spec 005 implements `IProductDisplayLookup`; spec 019 implements `IReviewDisplayHandleQuery`; spec 022 implements both refund subscribers + `IRatingAggregateReader`; spec 025 implements all 8 domain-event handlers.

---

## 8. Migration ordering

A single net-new migration `CreateReviewsSchemaAndTables` creates:

1. The `reviews` schema.
2. The `reviews.review_state` enum type.
3. All 7 tables.
4. All indexes + check constraints + unique partial indexes.
5. The `raise_immutable_audit_violation()` function (or reuses if already created by spec 020 / 021 / 007-b).
6. The `BEFORE UPDATE OR DELETE` triggers on the 3 append-only tables.

Reversible (`Down` method) for Dev rollbacks; production rollbacks are documented in the runbook.

---

## 9. Volume estimates (V1)

| Table | Initial rows | Growth/month |
|---|---|---|
| `reviews.reviews` | 0 (per Out of Scope: no migration import) | ~150 000/month at steady state (5 000/day × 30) |
| `reviews.review_moderation_decisions` | 0 | ~30 000/month (most reviews never transition; ~20% do) |
| `reviews.review_admin_notes` | 0 | ~5 000/month |
| `reviews.review_flags` | 0 | ~10 000/month |
| `reviews.product_rating_aggregates` | 0 | grows with catalog × 2 markets; reaches ~20 000 rows once catalog is populated; rebuilt daily |
| `reviews.reviews_filter_wordlists` | ~200 (seeded per market) | rare changes |
| `reviews.reviews_market_schemas` | 2 (seeded) | rare changes |

Total storage at end of year 1: < 5 GB (review bodies dominate; well within Azure Postgres Flexible Server's default tier).

---

## 10. Read-side hot paths and cache strategy

| Read path | Frequency | Caching |
|---|---|---|
| `IRatingAggregateReader.GetAsync(productId, market)` for product detail | ~100 rps / market | DB single-row PK lookup p95 ≤ 10 ms; HTTP layer caches at `Cache-Control: max-age=60` (FR-029) |
| `IRatingAggregateReader.GetManyAsync(productIds, market)` for search-result decoration | spiky | bulk PK lookup; no extra cache |
| Customer's own review listing | low | EF Core with `idx_reviews_customer_state` |
| Storefront product reviews list (paginated, visible+flagged only) | spiky | EF Core with `idx_reviews_product_market_state`; HTTP cache `max-age=30` for the first page |
| Moderator queue list | ~tens/min | EF Core with `idx_reviews_market_pending_age`; no cache (admin freshness > 30 s would surprise moderators) |
| Wordlist load on filter init | once / 60 s / app instance | in-process cache (R13) |

---

## 11. Out-of-scope schema concerns

- **Helpful / unhelpful vote counts** — Phase 1.5; column NOT pre-reserved (a future spec adds it).
- **Vendor-response thread** — Phase 2; not pre-reserved.
- **Review-import staging table** — Out of Scope per spec.md (no migration imports).
- **Review-translation table** — Out of Scope per Clarification Q1; no `headline_other` / `body_other` columns.
- **AI-summary column** — Phase 2.
