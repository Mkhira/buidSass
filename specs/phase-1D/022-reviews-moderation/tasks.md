---
description: "Task list — Spec 022 Reviews & Moderation (Phase 1D · Milestone 7)"
---

# Tasks: Reviews & Moderation

**Input**: Design documents from `/specs/phase-1D/022-reviews-moderation/`
**Prerequisites**: `plan.md` (required), `spec.md` (required for user stories), `research.md`, `data-model.md`, `contracts/reviews-and-moderation-contract.md`

**Tests**: Test tasks are included because the project's existing standard (specs 020 / 021 / 007-b) requires xUnit + FluentAssertions + Testcontainers Postgres + contract tests for every Acceptance Scenario. Spec 022 inherits the same standard (plan §Testing).

**Organization**: Tasks are grouped by user story (P1 → P3) so each story can be implemented and tested independently.

---

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Parallelizable — different files, no dependencies on incomplete tasks in the same phase.
- **[Story]**: Maps to spec.md user stories (`[US1]`–`[US7]`). Setup, Foundational, and Polish phases carry no story label.
- Every task description includes the exact target file path or directory.

## Path Conventions (per [plan.md §Project Structure](./plan.md))

- Backend: `services/backend_api/Modules/Reviews/...` (NEW module).
- Cross-module shared types: `services/backend_api/Modules/Shared/...`.
- Tests: `services/backend_api/tests/Reviews.Tests/{Unit,Integration,Contract}/`.
- Spec dir: `specs/phase-1D/022-reviews-moderation/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: confirm the existing module skeleton and prerequisites are at DoD, and prepare 022-specific scaffolding.

- [ ] T001 Verify spec 011 `orders` is at DoD on `main`: `Modules/Orders/` exists; an `IOrderLineDeliveryEligibilityQuery` interface is committed to `Modules/Shared/` (this spec's Phase 2D will create it if 011 hasn't yet — coordinate via the spec-011 owner).
- [ ] T002 Verify spec 015 `admin-foundation` contract (RBAC primitives, audit panel, idempotency middleware, rate-limit middleware) is merged on `main`.
- [ ] T003 Verify spec 006 `Modules/Search/Internal/IArabicNormalizer.cs` is **publicly visible** (`public interface IArabicNormalizer`); coordinate with the spec 006 owner if it is currently `internal` — the visibility change is a pre-requisite for the profanity filter (research §R4).
- [ ] T004 [P] Add the new permission constants to the project's RBAC seed list in `services/backend_api/Modules/Identity/Authorization/PermissionRegistry.cs`: `reviews.moderator`, `reviews.policy_admin`.
- [ ] T005 [P] Update the OpenAPI generation task in `services/backend_api/services.sln`'s `dotnet swagger tofile` step to emit `services/backend_api/openapi.reviews.json` (per research §R15).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: primitives, persistence (1 net-new migration creating the `reviews` schema + 7 tables + triggers), cross-module shared declarations, filter primitives, authorization wiring, and reference-data seeder. **No user-story work begins until this phase is complete.**

### Primitives (Phase A)

- [ ] T006 [P] Create `services/backend_api/Modules/Reviews/Primitives/ReviewState.cs` — enum `{PendingModeration, Visible, Flagged, Hidden, Deleted}` per data-model §3.
- [ ] T007 [P] Create `services/backend_api/Modules/Reviews/Primitives/ReviewStateMachine.cs` — pure-function `TryTransition(from, to, nowUtc, trigger, out reasonCode)` covering every transition row in data-model §3.
- [ ] T008 [P] Create `services/backend_api/Modules/Reviews/Primitives/ReviewActorKind.cs` — enum `{Customer, Moderator, PolicyAdmin, SuperAdmin, System}`.
- [ ] T009 [P] Create `services/backend_api/Modules/Reviews/Primitives/ReviewReasonCode.cs` — static class with all 35 owned codes from contract §10; xunit theory verifying every enum value has an ICU key in both locale files.
- [ ] T010 [P] Create `services/backend_api/Modules/Reviews/Primitives/ReviewMarketPolicy.cs` — value object resolving from a `reviews.reviews_market_schemas` row.
- [ ] T011 [P] Create `services/backend_api/Modules/Reviews/Primitives/QualifiedReporterPolicy.cs` — pure function `Evaluate(reporterAccountAge, hasDeliveredOrder, marketPolicy) → bool` honoring FR-023.
- [ ] T012 [P] Create `services/backend_api/Modules/Reviews/Primitives/ReviewerDisplayRenderer.cs` — pure function `Render(displayHandle?, firstName, lastName) → string` honoring FR-016a.
- [ ] T013 [P] Create `services/backend_api/Modules/Reviews/Primitives/ReviewTriggerKind.cs` — enum: `customer_submission`, `customer_edit`, `community_report_threshold`, `refund_event`, `account_locked`, `moderator_action`, `manual_super_admin`.

### Persistence — entities (Phase B)

- [ ] T014 [P] Create `services/backend_api/Modules/Reviews/Entities/Review.cs` per data-model §2.1 with all lifecycle columns, `vendor_id?`, `row_version` (xmin).
- [ ] T015 [P] Create `services/backend_api/Modules/Reviews/Entities/ReviewModerationDecision.cs` per data-model §2.2 (append-only).
- [ ] T016 [P] Create `services/backend_api/Modules/Reviews/Entities/ReviewAdminNote.cs` per data-model §2.3 (append-only).
- [ ] T017 [P] Create `services/backend_api/Modules/Reviews/Entities/ReviewFlag.cs` per data-model §2.4 with `is_qualified` + `qualifying_evaluation_jsonb`; unique constraint on `(review_id, reporter_actor_id)`.
- [ ] T018 [P] Create `services/backend_api/Modules/Reviews/Entities/ProductRatingAggregate.cs` per data-model §2.5.
- [ ] T019 [P] Create `services/backend_api/Modules/Reviews/Entities/ReviewsFilterWordlist.cs` per data-model §2.6.
- [ ] T020 [P] Create `services/backend_api/Modules/Reviews/Entities/ReviewsMarketSchema.cs` per data-model §2.7.

### Persistence — DbContext, configurations, migration (Phase B)

- [ ] T021 Create `services/backend_api/Modules/Reviews/Persistence/ReviewsDbContext.cs` — register all 7 `DbSet<>`s; suppress `ManyServiceProvidersCreatedWarning` per project-memory rule.
- [ ] T022 [P] Create `services/backend_api/Modules/Reviews/Persistence/Configurations/ReviewConfiguration.cs` — wire `state` enum mapping, all indexes (including the unique partial `ux_reviews_customer_product_active`), `IsRowVersion()` for xmin.
- [ ] T023 [P] Create `services/backend_api/Modules/Reviews/Persistence/Configurations/ReviewModerationDecisionConfiguration.cs` — append-only via raw-SQL trigger.
- [ ] T024 [P] Create `services/backend_api/Modules/Reviews/Persistence/Configurations/ReviewAdminNoteConfiguration.cs` — append-only.
- [ ] T025 [P] Create `services/backend_api/Modules/Reviews/Persistence/Configurations/ReviewFlagConfiguration.cs` — append-only + unique `(review_id, reporter_actor_id)`.
- [ ] T026 [P] Create `services/backend_api/Modules/Reviews/Persistence/Configurations/ProductRatingAggregateConfiguration.cs`.
- [ ] T027 [P] Create `services/backend_api/Modules/Reviews/Persistence/Configurations/ReviewsFilterWordlistConfiguration.cs`.
- [ ] T028 [P] Create `services/backend_api/Modules/Reviews/Persistence/Configurations/ReviewsMarketSchemaConfiguration.cs`.
- [ ] T029 Generate migration `CreateReviewsSchemaAndTables` via `dotnet ef migrations add ...`; manually adjust to: create `reviews` schema; create `review_state` enum type; create `raise_immutable_audit_violation()` function (or reuse if already created by spec 020/021/007-b); attach `BEFORE UPDATE OR DELETE` triggers to the 3 append-only tables; verify Up + Down compile and apply cleanly on Testcontainers Postgres.

### Cross-module shared declarations (Phase D)

- [ ] T030 [P] Create `services/backend_api/Modules/Shared/IOrderLineDeliveryEligibilityQuery.cs` per data-model §7; spec 011 implements.
- [ ] T031 [P] Create `services/backend_api/Modules/Shared/IRefundCompletedSubscriber.cs` and `IRefundCompletedPublisher.cs` with `RefundCompletedEvent` record.
- [ ] T032 [P] Create `services/backend_api/Modules/Shared/IRefundReversedSubscriber.cs` and `IRefundReversedPublisher.cs` with `RefundReversedEvent` record.
- [ ] T033 [P] Create `services/backend_api/Modules/Shared/IProductDisplayLookup.cs` with `ProductDisplay` record; spec 005 implements.
- [ ] T034 [P] Create `services/backend_api/Modules/Shared/IRatingAggregateReader.cs` with `RatingAggregate` record; spec 022 publishes; specs 005, 006 consume.
- [ ] T035 [P] Create `services/backend_api/Modules/Shared/IReviewDisplayHandleQuery.cs` with `CustomerDisplayInfo` record; spec 019 implements.
- [ ] T036 [P] Create `services/backend_api/Modules/Shared/ReviewDomainEvents.cs` containing all 8 `INotification` records from data-model §6.
- [ ] T037 [P] Add `services/backend_api/Modules/Shared/Testing/FakeRefundCompletedPublisher.cs`, `FakeRefundReversedPublisher.cs`, `FakeOrderLineDeliveryEligibilityQuery.cs`, `FakeProductDisplayLookup.cs`, and `FakeReviewDisplayHandleQuery.cs` for use by `Reviews.Tests` (lets integration tests exercise eligibility / display-name paths without requiring specs 011 / 005 / 019 to be at DoD on `main`).

### Filter + display primitives (Phase E)

- [ ] T038 Create `services/backend_api/Modules/Reviews/Filtering/ProfanityFilter.cs` — consumes `IArabicNormalizer` from `Modules/Search/`; loads per-market wordlist into in-process cache; refresh cycle 60 s + on-event invalidation per research §R13. Returns `(tripped: bool, matched_terms: string[])`.
- [ ] T039 [P] Create `services/backend_api/Modules/Reviews/Filtering/MediaAttachmentDetector.cs` — pure function `HasMedia(media_urls) → bool` per FR-014a.

### Authorization + reference seeder (Phase C + M)

- [ ] T040 [P] Create `services/backend_api/Modules/Reviews/Authorization/ReviewsPermissions.cs` exposing `reviews.moderator` and `reviews.policy_admin` constants for `[RequirePermission(...)]` attributes.
- [ ] T041 [P] Create `services/backend_api/Modules/Reviews/Seeding/ReviewsReferenceDataSeeder.cs` — upserts KSA + EG market schemas (per defaults in data-model §2.7) + initial AR + EN profanity wordlists; `RunInProduction = true`; idempotent across all environments.
- [ ] T042 Create `services/backend_api/Modules/Reviews/ReviewsModule.cs` — `AddReviewsModule(IServiceCollection, IConfiguration)`; register MediatR scan; `AddDbContext<ReviewsDbContext>` with warning suppression; register `ProfanityFilter` (singleton with cache); register the 3 subscribers; register `IRatingAggregateReader` impl; register the 2 hosted workers (registration ahead of code is fine; DI resolves at runtime); register seeders; register permissions.
- [ ] T043 Wire `ReviewsModule` from `services/backend_api/Program.cs`: `services.AddReviewsModule(builder.Configuration);`.

### Foundational tests

- [ ] T044 [P] Unit test `tests/Reviews.Tests/Unit/Primitives/ReviewStateMachineTests.cs` — every valid + every invalid transition + idempotency; xUnit theory.
- [ ] T045 [P] Unit test `tests/Reviews.Tests/Unit/Primitives/QualifiedReporterPolicyTests.cs` — every threshold combination (account-age cutoff, verified-buyer flag).
- [ ] T046 [P] Unit test `tests/Reviews.Tests/Unit/Primitives/ReviewerDisplayRendererTests.cs` — handle present, handle null, last-name empty, AR-name boundary cases (e.g., compound names).
- [ ] T047 [P] Unit test `tests/Reviews.Tests/Unit/Primitives/ReviewReasonCodeIcuKeyTests.cs` — every code resolves to non-empty `en` and `ar` ICU keys.
- [ ] T048 [P] Unit test `tests/Reviews.Tests/Unit/Filtering/ProfanityFilterTests.cs` — wordlist coverage matrix per market; AR-normalization corner cases (hamza, ligatures); case-insensitive EN matching (SC-010).
- [ ] T049 [P] Unit test `tests/Reviews.Tests/Unit/Filtering/MediaAttachmentDetectorTests.cs`.
- [ ] T050 Integration test `tests/Reviews.Tests/Integration/Persistence/MigrationApplicationTests.cs` — applies the migration to a Testcontainers Postgres; asserts schema shape via `pg_catalog` queries (`reviews` schema, 7 tables, all indexes, the trigger function).
- [ ] T051 Integration test `tests/Reviews.Tests/Integration/Persistence/AppendOnlyTriggersTests.cs` — confirms `UPDATE` and `DELETE` on each of the 3 append-only tables raise the trigger error.
- [ ] T052 Integration test `tests/Reviews.Tests/Integration/Seeding/ReviewsReferenceDataSeederTests.cs` — runs the seeder twice; asserts exactly 2 market-schema rows + N wordlist rows; asserts default values per market; idempotency.

**Checkpoint**: Phase 2 complete — Foundation ready. User stories may proceed in parallel.

---

## Phase 3: User Story 1 — Verified buyer submits a review (no profanity) (Priority: P1) 🎯 MVP

**Story goal**: deliver verified-buyer review submission end-to-end. After this phase, a customer with a delivered, non-refunded order line can author a review and see it published as `visible`, with the rating aggregate refreshed.

**Independent test**: sign in as a customer with a delivered, non-refunded order line for product P; submit a review with body that doesn't trip the filter; verify the review row exists in `visible`, the rating aggregate row for `(P, market_code)` is updated, and the storefront API returns the review on next read.

### Tests for User Story 1

- [ ] T053 [P] [US1] Contract test `tests/Reviews.Tests/Contract/Customer/SubmitReviewContractTests.cs` — every Acceptance Scenario from spec.md User Story 1 (5 scenarios).
- [ ] T054 [P] [US1] Integration test `tests/Reviews.Tests/Integration/Customer/SubmitReviewHappyPathTests.cs` — eligible customer, clean text, no media → state=`visible`; aggregate refreshes.
- [ ] T055 [P] [US1] Integration test `tests/Reviews.Tests/Integration/Customer/SubmitReviewEligibilityTests.cs` — `no_delivered_purchase`, `refunded`, `window_closed`, `already_reviewed`.
- [ ] T056 [P] [US1] Integration test `tests/Reviews.Tests/Integration/Customer/SubmitReviewValidationTests.cs` — rating out of range, headline length, body length, locale invalid, media too many, invalid signed URL.
- [ ] T057 [P] [US1] Integration test `tests/Reviews.Tests/Integration/Customer/SubmitReviewUniquenessTests.cs` — concurrent inserts for `(customer, product)` — only one wins via the unique partial index.
- [ ] T058 [P] [US1] Integration test `tests/Reviews.Tests/Integration/Customer/UpdateReviewWithinEditWindowTests.cs` — edit allowed; `xmin` advances; audit row.
- [ ] T059 [P] [US1] Integration test `tests/Reviews.Tests/Integration/Customer/UpdateReviewAfterEditWindowTests.cs` — `400 review.edit.window_closed` after the window.
- [ ] T060 [P] [US1] Integration test `tests/Reviews.Tests/Integration/Customer/UpdateReviewNotAuthorTests.cs` — `403 review.edit.not_author`.
- [ ] T061 [P] [US1] Integration test `tests/Reviews.Tests/Integration/Customer/SubmissionRateLimitTests.cs` — 5 / hour / customer cap; over-limit `429 review.rate_limit.submission_exceeded`.

### Implementation for User Story 1

- [ ] T062 [P] [US1] Implement `services/backend_api/Modules/Reviews/Customer/SubmitReview/{Endpoint,Request,Response,Handler,Validator,Mapper}.cs` per contract §2.1 and quickstart §4. Handler calls `IOrderLineDeliveryEligibilityQuery`, applies eligibility-window check, runs `ProfanityFilter`, runs `MediaAttachmentDetector`, persists in `visible` (clean) or `pending_moderation` (tripped/media), writes audit row, fires domain event, refreshes aggregate when `visible`.
- [ ] T063 [P] [US1] Implement `services/backend_api/Modules/Reviews/Customer/UpdateReview/...` per contract §2.2. Re-runs filter + media detection; transitions to `pending_moderation` and re-stamps `pending_moderation_started_at` if either trips (FR-009); advances `xmin` via EF `IsRowVersion()`.
- [ ] T064 [P] [US1] Implement `services/backend_api/Modules/Reviews/Customer/ListMyReviews/...` per contract §2.3 — returns own reviews regardless of state (so the customer can see held + hidden); paged.
- [ ] T065 [P] [US1] Implement `services/backend_api/Modules/Reviews/Customer/GetMyReview/...` per contract §2.4.
- [ ] T066 [P] [US1] Implement `services/backend_api/Modules/Reviews/Aggregate/RatingAggregateRecomputer.cs` — pure SQL `INSERT...ON CONFLICT DO UPDATE` per quickstart §8 SQL block; computes from scratch over `state IN ('visible','flagged')`.

**Checkpoint**: User Story 1 fully implemented. MVP slice (without preview) ready to demo.

---

## Phase 4: User Story 2 — Profanity-filtered review held for moderation (Priority: P1) 🎯 MVP

**Story goal**: deliver the filter-trip + media-attachment hold path end-to-end. After this phase, a profanity-tripped or media-bearing submission lands in `pending_moderation` and surfaces in the moderator queue.

**Independent test**: submit a review whose body contains a seeded profanity term; verify state is `pending_moderation`; verify the queue endpoint returns the review with the `filter-tripped` badge; sign in as `reviews.moderator` and approve / hide; verify the audit row.

### Tests for User Story 2

- [ ] T067 [P] [US2] Contract test `tests/Reviews.Tests/Contract/Customer/SubmitReviewFilterTripContractTests.cs` — every Acceptance Scenario from spec.md User Story 2 (5 scenarios).
- [ ] T068 [P] [US2] Integration test `tests/Reviews.Tests/Integration/Customer/SubmitReviewFilterTripTests.cs` — submission with seeded EN profanity term → state `pending_moderation`, `filter_trip_terms[]` populated, aggregate NOT updated, response carries `pending_review=true`.
- [ ] T069 [P] [US2] Integration test `tests/Reviews.Tests/Integration/Customer/SubmitReviewMediaAutoHoldTests.cs` — submission with clean text + 1 media URL → state `pending_moderation`, `media_attachment_review_required=true` (FR-014a).
- [ ] T070 [P] [US2] Integration test `tests/Reviews.Tests/Integration/Customer/EditReviewFiltersOnEditTests.cs` — edit that adds profanity → review transitions to `pending_moderation` with `triggered_by=customer_edit`; edit that removes media → no auto-hold trigger.
- [ ] T071 [P] [US2] Integration test `tests/Reviews.Tests/Integration/Customer/EditDuringPendingRestampsTests.cs` — edit while in `pending_moderation` re-stamps `pending_moderation_started_at` and advances `xmin` (R9 verification hook).
- [ ] T072 [P] [US2] Integration test `tests/Reviews.Tests/Integration/Wordlist/WordlistRefreshWithin60sTests.cs` — admin adds new term; submission with that term trips the filter within 60 s (research §R13).
- [ ] T073 [P] [US2] Integration test `tests/Reviews.Tests/Integration/Customer/EditDuringPendingInvalidatesModeratorDecisionTests.cs` — moderator reads with row_version v1; customer edits → v2; moderator submits decide with If-Match v1 → `409 reviews.moderation.version_conflict` (R9 verification hook).

### Implementation for User Story 2

(Logic for filter-trip + media auto-hold + edit re-stamp is implemented in T062 and T063 from US1; this phase adds the dedicated tests + verifies behavior end-to-end. No new handlers required.)

**Checkpoint**: User Story 2 fully validated. Filter and media-hold paths green.

---

## Phase 5: User Story 3 — Customer reports an inappropriate review with threshold escalation (Priority: P1) 🎯 MVP

**Story goal**: deliver the community-report flow with qualified-reporter weighting and threshold-driven escalation to `flagged`.

**Independent test**: sign in as 3 distinct qualified customers; report a `visible` review; verify it auto-transitions to `flagged` after the third report; sign in as `reviews.moderator`; resolve.

### Tests for User Story 3

- [ ] T074 [P] [US3] Contract test `tests/Reviews.Tests/Contract/Customer/ReportReviewContractTests.cs` — every Acceptance Scenario (5).
- [ ] T075 [P] [US3] Integration test `tests/Reviews.Tests/Integration/Customer/ReportReviewUnauthenticatedTests.cs` — `401 review.report.unauthenticated`.
- [ ] T076 [P] [US3] Integration test `tests/Reviews.Tests/Integration/Customer/ReportReviewSelfRejectedTests.cs` — `400 review.report.cannot_report_own_review`.
- [ ] T077 [P] [US3] Integration test `tests/Reviews.Tests/Integration/Customer/ReportReviewIdempotentTests.cs` — same `(reporter, review)` twice → second returns `409 review.report.already_reported_by_actor`; counter does NOT double-count.
- [ ] T078 [P] [US3] Integration test `tests/Reviews.Tests/Integration/Customer/ReportReviewQualifiedThresholdTests.cs` — 3 distinct qualified reporters within 30 days → review state `visible → flagged`; `ReviewFlagged` event fired (FR-023).
- [ ] T079 [P] [US3] Integration test `tests/Reviews.Tests/Integration/Customer/ReportReviewUnqualifiedNotCountedTests.cs` — 3 unqualified reporters (account < 14 days OR no delivered orders) → review stays `visible`; flags persisted with `is_qualified=false` for moderator visibility (FR-023, R5).
- [ ] T080 [P] [US3] Integration test `tests/Reviews.Tests/Integration/Customer/ReportReasonValidationTests.cs` — `other_with_required_note` without note < 10 chars → `400 review.report.note_required`; invalid reason → `400 review.report.reason_invalid`.
- [ ] T081 [P] [US3] Integration test `tests/Reviews.Tests/Integration/Customer/ReportRateLimitTests.cs` — 5 reports / hour / customer cap; over-limit `429 review.rate_limit.report_exceeded`.

### Implementation for User Story 3

- [ ] T082 [US3] Implement `services/backend_api/Modules/Reviews/Customer/ReportReview/{Endpoint,Request,Response,Handler,Validator,Mapper}.cs` per contract §2.5. Handler verifies caller is signed-in customer (else 401); rejects self-report; evaluates `QualifiedReporterPolicy` and persists the boolean snapshot on the `ReviewFlag` row (R5); inserts the flag (let unique constraint catch double-reports); recounts qualified reports within window; if `>= threshold` AND review is `visible`, transitions to `flagged` and fires `ReviewFlagged` event in same transaction.
- [ ] T083 [P] [US3] Implement `services/backend_api/Modules/Reviews/Customer/GetReportReasons/...` per contract §2.6 — static lookup of the 5 fixed reasons + ICU keys.

**Checkpoint**: User Story 3 fully implemented.

---

## Phase 6: User Story 4 — Moderator hides, reinstates, and (super_admin) deletes a review (Priority: P2)

**Story goal**: deliver the full moderation queue + decision lifecycle including the `hidden ↔ visible` reversibility and `super_admin`-only `deleted` terminal.

**Independent test**: walk a single review through `visible → hidden → visible → deleted` via four moderator actions; verify each transition has an audit row with the correct from/to states; verify the rating aggregate updates correctly at each transition.

### Tests for User Story 4

- [ ] T084 [P] [US4] Contract test `tests/Reviews.Tests/Contract/Admin/DecideModerationContractTests.cs` — every Acceptance Scenario (5).
- [ ] T085 [P] [US4] Integration test `tests/Reviews.Tests/Integration/Admin/ListModerationQueueTests.cs` — filters by `state`, `market_code`, `triggered_by`, `community_report_count_min`, `media_only`; queue ordered by `pending_moderation_started_at`.
- [ ] T086 [P] [US4] Integration test `tests/Reviews.Tests/Integration/Admin/GetReviewDetailTests.cs` — full body + audit history + flags + admin notes + customer's other reviews summary.
- [ ] T087 [P] [US4] Integration test `tests/Reviews.Tests/Integration/Admin/DecideModerationHiddenWithReasonTests.cs` — `hidden` requires reason ≥ 10 chars; aggregate refreshes if from `visible`/`flagged`.
- [ ] T088 [P] [US4] Integration test `tests/Reviews.Tests/Integration/Admin/DecideModerationReinstateTests.cs` — `hidden → visible` requires admin_note ≥ 10 chars; aggregate refreshes.
- [ ] T089 [P] [US4] Integration test `tests/Reviews.Tests/Integration/Admin/DecideModerationDeleteRequiresSuperAdminTests.cs` — moderator without super_admin attempts delete → `403 reviews.moderation.delete_requires_super_admin`.
- [ ] T090 [P] [US4] Integration test `tests/Reviews.Tests/Integration/Admin/DecideModerationDeletedTerminalTests.cs` — `deleted → *` always rejected with `400 reviews.moderation.delete_terminal`.
- [ ] T091 [P] [US4] Integration test `tests/Reviews.Tests/Integration/Admin/DecideModerationVersionConflictTests.cs` — concurrent decisions: one wins, other gets `409 reviews.moderation.version_conflict` (FR-019).
- [ ] T092 [P] [US4] Integration test `tests/Reviews.Tests/Integration/Admin/AddAdminNoteTests.cs` — note ≥ 10 chars persisted append-only; all prior notes still visible.
- [ ] T093 [P] [US4] Integration test `tests/Reviews.Tests/Integration/Admin/ModerationRateLimitTests.cs` — 60 decisions / hour / actor cap; over-limit `429 reviews.moderation.rate_limit_exceeded`.
- [ ] T094 [P] [US4] Integration test `tests/Reviews.Tests/Integration/Admin/HardDeleteForbiddenTests.cs` — `DELETE /v1/admin/reviews/{id}` always returns `405 review.row.delete_forbidden` (FR-005a).

### Implementation for User Story 4

- [ ] T095 [P] [US4] Implement `services/backend_api/Modules/Reviews/Admin/ListModerationQueue/...` per contract §3.1 — uses `idx_reviews_market_pending_age` for ordering; renders FR-016 fields (locale, reviewer-display rule, all flag reports, filter terms, media inline, audit summary, admin notes summary, edited-since-last-surface indicator).
- [ ] T096 [P] [US4] Implement `services/backend_api/Modules/Reviews/Admin/GetReviewDetail/...` per contract §3.2.
- [ ] T097 [US4] Implement `services/backend_api/Modules/Reviews/Admin/DecideModeration/...` per contract §3.3 and quickstart §7. RBAC: `reviews.moderator` for `visible`/`hidden`; `super_admin` for `deleted`. State-machine validates transition; row_version optimistic concurrency; reason-or-admin-note required (≥ 10 chars); writes audit row + denormalized `ReviewModerationDecision`; refreshes aggregate inline on countable state changes; fires the appropriate domain event.
- [ ] T098 [P] [US4] Implement `services/backend_api/Modules/Reviews/Admin/AddAdminNote/...` per contract §3.4 — append-only `ReviewAdminNote` row.
- [ ] T099 [P] [US4] Implement `services/backend_api/Modules/Reviews/Admin/ListAdminNotes/...` per contract §3.5 — ordered DESC by `created_at_utc`.
- [ ] T100 [P] [US4] Implement `services/backend_api/Modules/Reviews/Admin/ListReviewsByCustomer/...` per contract §3.6 — for support / dispute investigation.
- [ ] T101 [P] [US4] Wire `DELETE /v1/admin/reviews/{id}` to return `405 review.row.delete_forbidden` per contract §3.7 (FR-005a).

**Checkpoint**: User Story 4 fully implemented.

---

## Phase 7: User Story 5 — Refund auto-hides previously published review (Priority: P2)

**Story goal**: deliver the refund-event subscriber that auto-hides reviews when their underlying order line is refunded.

**Independent test**: publish a review for a delivered order line; trigger a refund event for the same line; verify the review auto-transitions to `hidden`, the rating aggregate excludes it, and an audit row with `actor_id='system'` and `triggered_by=refund_event` is present.

### Tests for User Story 5

- [ ] T102 [P] [US5] Contract test `tests/Reviews.Tests/Contract/Subscribers/RefundCompletedSubscriberContractTests.cs` — Acceptance Scenarios from US5.
- [ ] T103 [P] [US5] Integration test `tests/Reviews.Tests/Integration/Subscribers/RefundCompletedAutoHidesReviewsTests.cs` — emit fake `RefundCompletedEvent`; assert affected `visible`/`flagged` reviews transition to `hidden` within 60 s; aggregate recomputed; audit row with `triggered_by=refund_event`.
- [ ] T104 [P] [US5] Integration test `tests/Reviews.Tests/Integration/Subscribers/RefundCompletedIdempotencyTests.cs` — same event delivered twice → second is no-op (no second audit row).
- [ ] T105 [P] [US5] Integration test `tests/Reviews.Tests/Integration/Subscribers/RefundReversedNoAutoReinstateTests.cs` — emit fake `RefundReversedEvent`; assert review stays `hidden`; admin queue surfaces "needs review" indicator (FR-032).
- [ ] T106 [P] [US5] Integration test `tests/Reviews.Tests/Integration/Subscribers/AccountLockedAutoHidesReviewsTests.cs` — emit fake `CustomerAccountLockedEvent` via the existing spec 020 lifecycle subscriber harness; assert all of customer's `visible`/`flagged` reviews transition to `hidden` with `triggered_by=account_locked`.

### Implementation for User Story 5

- [ ] T107 [US5] Implement `services/backend_api/Modules/Reviews/Subscribers/RefundCompletedHandler.cs` per quickstart §9 — loads affected reviews, auto-hides each, persists denormalized `ReviewModerationDecision` rows, refreshes aggregate, fires `ReviewAutoHidden` event per review.
- [ ] T108 [P] [US5] Implement `services/backend_api/Modules/Reviews/Subscribers/RefundReversedHandler.cs` — does NOT auto-reinstate (FR-032); writes a structured operator advisory entry surfaced as a "needs review" badge on affected reviews.
- [ ] T109 [P] [US5] Implement `services/backend_api/Modules/Reviews/Subscribers/CustomerAccountLifecycleHandler.cs` — registers a new handler against the existing `ICustomerAccountLifecycleSubscriber` interface from spec 020 (R12); auto-hides authored reviews on `customer_account_locked` and `customer_account_deleted`.

**Checkpoint**: User Story 5 fully implemented. Cross-module subscribers green.

---

## Phase 8: User Story 6 — Storefront consumes rating aggregate (Priority: P1) 🎯 MVP

**Story goal**: deliver the public unauthenticated rating-aggregate read endpoints (single + batch) consumed by spec 005 product detail and spec 006 search-result decoration.

**Independent test**: seed 50 reviews across all 5 states for product P; call the aggregate endpoint; verify only `visible` and `flagged` reviews count; verify avg_rating + distribution math.

### Tests for User Story 6

- [ ] T110 [P] [US6] Contract test `tests/Reviews.Tests/Contract/Public/ReadProductRatingContractTests.cs` — every Acceptance Scenario (4).
- [ ] T111 [P] [US6] Integration test `tests/Reviews.Tests/Integration/Public/AggregateExcludesPendingHiddenDeletedTests.cs` — seed reviews across all 5 states; assert only `visible` and `flagged` count; avg + distribution math.
- [ ] T112 [P] [US6] Integration test `tests/Reviews.Tests/Integration/Public/AggregateRefreshesWithin60sTests.cs` — transition triggers recompute; `last_updated_utc` advances; SC-005 soak test over 100 random transitions.
- [ ] T113 [P] [US6] Integration test `tests/Reviews.Tests/Integration/Public/AggregateNullAvgWhenZeroTests.cs` — product with 0 reviews → `review_count=0`, `avg_rating=null` (FR-028).
- [ ] T114 [P] [US6] Integration test `tests/Reviews.Tests/Integration/Public/AggregateUnauthenticatedTests.cs` — 200 OK without auth header; `Cache-Control: public, max-age=60` present (FR-029).
- [ ] T115 [P] [US6] Performance test `tests/Reviews.Tests/Integration/Performance/AggregateReadP95Tests.cs` — single-row PK lookup p95 ≤ 50 ms (plan §Performance).
- [ ] T116 [P] [US6] Integration test `tests/Reviews.Tests/Integration/Public/AggregateBatchReadTests.cs` — batch endpoint with up to 100 product ids (contract §5.2).

### Implementation for User Story 6

- [ ] T117 [P] [US6] Implement `services/backend_api/Modules/Reviews/Aggregate/ReadProductRating/...` per contract §5.1 — public unauth; single-row PK lookup; `Cache-Control: public, max-age=60` header.
- [ ] T118 [P] [US6] Implement the batch read endpoint per contract §5.2 — bulk PK lookup; result-set capped at 100.
- [ ] T119 [P] [US6] Implement `services/backend_api/Modules/Reviews/Aggregate/RatingAggregateReader.cs` (the `IRatingAggregateReader` impl declared in T034) — used by spec 005 / 006 in-process consumers.

**Checkpoint**: User Story 6 fully implemented. Storefront-consumable aggregate live.

---

## Phase 9: User Story 7 — `reviews-v1` seeder for staging and local development (Priority: P3)

**Story goal**: ship the dev seeder that populates every state for QA / training in Dev + Staging only.

**Independent test**: `seed --dataset=reviews-v1 --mode=apply` against a fresh staging DB; verify per-state distribution; verify the wordlist rows; verify aggregate consistency.

### Tests for User Story 7

- [ ] T120 [P] [US7] Integration test `tests/Reviews.Tests/Integration/Seeding/ReviewsV1SeederIdempotencyTests.cs` — runs the seeder twice; row count after run 2 = row count after run 1.
- [ ] T121 [P] [US7] Integration test `tests/Reviews.Tests/Integration/Seeding/ReviewsV1SeederStateCoverageTests.cs` — asserts ≥ 1 row in each of `visible`, `pending_moderation`, `flagged`, `hidden`, `deleted` (SC-008).
- [ ] T122 [P] [US7] Integration test `tests/Reviews.Tests/Integration/Seeding/ReviewsV1SeederGuardTests.cs` — `RunInProduction=false` SeedGuard prevents the seeder from executing in Production environment.
- [ ] T123 [P] [US7] Integration test `tests/Reviews.Tests/Integration/Seeding/ReviewsV1SeederDryRunTests.cs` — `--mode=dry-run` exits 0 with planned-changes report and writes nothing.

### Implementation for User Story 7

- [ ] T124 [US7] Implement `services/backend_api/Modules/Reviews/Seeding/ReviewsV1DevSeeder.cs` — synthetic reviews: 30 visible across 6 products and 5 ratings; 5 `pending_moderation` (filter-tripped on seeded EN + AR terms); 4 `flagged` (with seeded community reports from qualified reporters); 3 `hidden`; 2 `deleted` (with super_admin actor); each tied to a synthetic customer + delivered, non-refunded order line; `RunInProduction=false`; idempotent.
- [ ] T125 [P] [US7] Author bilingual editorial-grade content for every customer-visible string in the seeder; flag every AR string in `services/backend_api/Modules/Reviews/Messages/AR_EDITORIAL_REVIEW.md`.
- [ ] T126 [P] [US7] Author the system-generated reason-code ICU keys in `services/backend_api/Modules/Reviews/Messages/reviews.en.icu` and `reviews.ar.icu` — every code from contract §10; AR strings flagged.

**Checkpoint**: User Story 7 fully implemented.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: cross-cutting subsystems and DoD-level checks that integrate the user-story slices.

### Policy-admin slices (Phase J)

- [ ] T127 [P] Implement `services/backend_api/Modules/Reviews/PolicyAdmin/ListWordlistTerms/...` per contract §4.1.
- [ ] T128 [P] Implement `services/backend_api/Modules/Reviews/PolicyAdmin/UpsertWordlistTerm/...` per contract §4.2 — normalizes + lowercases term at write time; emits `WordlistUpdatedEvent` for filter cache invalidation.
- [ ] T129 [P] Implement `services/backend_api/Modules/Reviews/PolicyAdmin/DeleteWordlistTerm/...` per contract §4.3.
- [ ] T130 [P] Implement `services/backend_api/Modules/Reviews/PolicyAdmin/UpdateMarketSchema/...` per contract §4.4 — `reviews.policy_admin` only; audited; range-validated per check constraints.
- [ ] T131 [P] Integration test `tests/Reviews.Tests/Integration/PolicyAdmin/UpdateMarketSchemaForbiddenTests.cs` — non-policy-admin → `403 reviews.policy.forbidden`.
- [ ] T132 [P] Integration test `tests/Reviews.Tests/Integration/PolicyAdmin/UpdateMarketSchemaOutOfRangeTests.cs` — values outside check-constraint ranges → `400 reviews.policy.market.value_out_of_range`.

### Workers (Phase L)

- [ ] T133 Implement `services/backend_api/Modules/Reviews/Workers/RatingAggregateRebuildWorker.cs` per quickstart §10 — daily at 03:00 UTC; advisory-lock guarded via `pg_try_advisory_lock(hashtext('reviews.rating_rebuild'))`; recomputes every `(product_id, market_code)` aggregate from scratch; idempotent.
- [ ] T134 Implement `services/backend_api/Modules/Reviews/Workers/ReviewIntegrityScanWorker.cs` per quickstart §10 — daily; SQL scan finds `visible`/`flagged` reviews tied to currently-refunded order lines; logs to `reviews.integrity` channel + emits `reviews_integrity_violations_total{kind, market}` metric (SC-004); does NOT auto-correct.
- [ ] T135 [P] Integration test `tests/Reviews.Tests/Integration/Workers/RatingAggregateRebuildWorkerTests.cs` — `FakeTimeProvider`; corrupts an aggregate row; runs the worker; asserts the row is rebuilt to truth.
- [ ] T136 [P] Integration test `tests/Reviews.Tests/Integration/Workers/ReviewIntegrityScanWorkerTests.cs` — seeds an intentionally-stale `visible` review on a refunded order line (bypass subscriber via raw SQL); runs the scanner; asserts the violation is logged + metric increments; clean DB → zero violations (SC-004).

### Domain events + spec 025 contract (Phase N)

- [ ] T137 Wire publication of all 8 domain events from `services/backend_api/Modules/Shared/ReviewDomainEvents.cs` at the lifecycle-transition + report + auto-hide call sites; ensure events fire AFTER the transaction commits (FR-038 — never block lifecycle on notification success).
- [ ] T138 [P] Integration test `tests/Reviews.Tests/Integration/Events/ReviewDomainEventsPublishedTests.cs` — collects published events via `FakeMediator` for a representative end-to-end flow; asserts each event fires exactly once with the correct payload shape.

### OpenAPI artifact (Phase O)

- [ ] T139 Regenerate `services/backend_api/openapi.reviews.json` via `dotnet swagger tofile`; commit; PR diff reviewed against contract §1-§10.

### Audit coverage (Phase R)

- [ ] T140 [P] Implement `scripts/audit-coverage/reviews.sh` — runs the spec-015 audit-coverage script over a 100-action operator + customer session; asserts 100 % of state transitions + report submissions + wordlist edits + threshold edits + admin notes produce a matching audit row + a denormalized `review_moderation_decisions` (or matching detail-table) row (SC-003).
- [ ] T141 [P] Integration test `tests/Reviews.Tests/Integration/Audit/AuditCoverageTests.cs` — programmatically exercises every authoring slice + lifecycle transition + report + admin note + wordlist edit + threshold edit; asserts the 14 audit-event kinds from data-model §5 are reachable.

### AR editorial sweep (Phase P)

- [ ] T142 AR editorial review: every system-generated string ICU-keyed in `reviews.ar.icu` MUST be reviewed by an editorial-grade reviewer (Principle 4 / SC-007). Update `AR_EDITORIAL_REVIEW.md` with the sign-off list. Blocks launch, not the PR.

### Concurrency + rate-limit hardening

- [ ] T143 [P] Concurrency stress test `tests/Reviews.Tests/Integration/Concurrency/ConcurrentReportsAcrossActorsTests.cs` — 100 concurrent reporters: 50 unique reporter-ids, 2 concurrent attempts each; assert exactly 50 unique `ReviewFlag` rows (FR-022, SC-009).
- [ ] T143a [P] Latency test `tests/Reviews.Tests/Integration/Performance/QueueSurfaceP95Tests.cs` — submit 100 filter-tripped or media-bearing reviews; for each, measure `now() - pending_moderation_started_at` at the moment the review first appears in the moderator queue list (`GET /v1/admin/reviews/queue?state=pending_moderation`); assert p95 ≤ 60 s (SC-006).

### DoD checklist + fingerprint (Phase R)

- [ ] T144 Compute constitution + ADR fingerprint via `scripts/compute-fingerprint.sh` and paste into the PR body; verify against locked v1.0.0 baseline.
- [ ] T145 Walk through the DoD checklist from `docs/dod.md` (DoD version 1.0); attach completion ticks to the PR description.
- [ ] T146 Manual smoke through Postman / curl for one slice from each top-level surface (customer submit, customer edit, customer report, admin queue, admin decide, public aggregate read) per quickstart §13.
- [ ] T147 [P] CI grep guard: extend `scripts/ci/assert-warning-suppressed.sh` to also scan `Modules/Reviews/ReviewsModule.cs` for the `ManyServiceProvidersCreatedWarning` suppression (project-memory rule, R14).

---

## Dependencies & Execution Order

### Phase Dependencies

```text
Phase 1 (Setup)
    ↓
Phase 2 (Foundational) ── blocks all user stories
    ↓
    ┌──────── parallel start ────────┐
    ↓                                   ↓
Phase 3 (US1 submit)   Phase 4 (US2 filter)   Phase 5 (US3 reports)   Phase 8 (US6 aggregate)   ← P1 stories run concurrently
    ↓                       ↓                       ↓                       ↓
Phase 6 (US4 moderation) ── depends on US1's review-row creation paths
Phase 7 (US5 refund hooks) ── depends on US4's auto-hide audit flow
Phase 9 (US7 seeder) ── lightweight; depends on Phases 3-8 schema + handlers being stable
    ↓
Phase 10 (Polish) ── cross-cutting; some tasks (workers, OpenAPI, audit-coverage, fingerprint, DoD) require all stories complete
```

### Within each user story

- Tests run in parallel with each other (different files).
- Implementation tasks marked `[P]` run in parallel with each other (different files).
- Tasks NOT marked `[P]` are sequential because they touch shared modules or DI registrations.

### Parallel execution examples

- **Phase 2 entities**: T014–T020 (seven new entities) all run in parallel.
- **Phase 2 EF configurations**: T022–T028 in parallel.
- **Phase 2 cross-module declarations**: T030–T037 in parallel.
- **Phase 2 unit tests**: T044–T049 in parallel.
- **Phase 3 tests**: T053–T061 in parallel.
- **Phase 3 implementation**: T062–T066 in parallel (all touch different folders/files).
- **Phase 4**: T067–T073 in parallel; no new implementation (US2 leverages US1 handlers).
- **Phase 5 tests**: T074–T081 in parallel; T082 sequential (touches the report handler shared state).
- **Phase 6 tests**: T084–T094 in parallel; T097 sequential (DecideModeration is the central state-transition handler).
- **Phase 7**: T102–T106 tests in parallel; T107 sequential; T108–T109 in parallel.
- **Phase 8**: T110–T116 tests in parallel; T117–T119 implementations in parallel.
- **Phase 10 policy-admin slices**: T127–T132 in parallel.

---

## Implementation Strategy

### MVP scope (US1 + US2 + US3 + US6 — the 4 P1 stories)

After Phases 1, 2, 3, 4, 5, and 8 land (~119 tasks), the following slice is demoable:

- A verified-buyer can submit, edit, list their own reviews.
- Profanity-tripped + media-bearing reviews are held for moderation.
- Customers can report inappropriate reviews; the threshold escalates community-reported reviews to `flagged`.
- The storefront consumes the rating aggregate via the public unauth read endpoint.
- Admin moderation (US4) and refund cascades (US5) are not yet live, but the schema is forward-compatible.

### Incremental delivery

| Increment | Tasks | Demo gain |
|---|---|---|
| MVP-α (US1+US2) | T001-T073 | Customer review submission + filter hold live |
| +US3 | T074-T083 | Community reports live |
| +US6 | T110-T119 | Public aggregate read live; storefront can consume |
| +US4 | T084-T101 | Admin moderation queue + decisions live |
| +US5 | T102-T109 | Refund + account-locked auto-hide live |
| +US7 | T120-T126 | Dev seeder ready for QA / training |
| Polish | T127-T147 | Policy admin, workers, OpenAPI, audit, DoD |

Each increment is mergable on its own and produces a usable system. Spec 014 (storefront UI) and spec 015 (admin UI) can begin consuming the contracts as soon as Phase 2 lands and ship UI per increment.

---

## Risk callouts

- **`IArabicNormalizer` visibility change in spec 006**: T003 verifies; if it's still `internal` on `main`, spec 022 stalls until 006 publishes the interface. Coordinate ahead of Phase 2.
- **`IOrderLineDeliveryEligibilityQuery` in spec 011**: if 011 hasn't shipped on `main`, 022 ships against the interface stub + fakes; integration tests pass with the fake. Production wiring requires 011's implementation.
- **`review_display_handle` field in spec 019**: per spec.md Assumptions, 022 falls back to first-name + last-initial only if 019 hasn't shipped the field. No implementation block on 022.
- **AR editorial debt** (T142): blocks launch, not merge. Track in `AR_EDITORIAL_REVIEW.md`.
- **Cross-module subscriber dependencies** (T107–T109): require spec 013 + spec 004 to publish the corresponding events on `main`. Subscribers ship "wired but quiet" if those PRs are still in flight; fakes prove correctness.

---

**Total tasks**: 148 (T001–T147 + T143a, added by `/speckit-analyze` remediation for SC-006 latency coverage).

**Per user story**:
- Setup: 5 (T001-T005)
- Foundational: 47 (T006-T052)
- US1: 14 (T053-T066)
- US2: 7 (T067-T073)
- US3: 10 (T074-T083)
- US4: 18 (T084-T101)
- US5: 8 (T102-T109)
- US6: 10 (T110-T119)
- US7: 7 (T120-T126)
- Polish: 22 (T127-T147 + T143a)

**Format validation**: every task above starts with `- [ ]`, has a sequential `T###` ID, carries `[P]` when parallelizable, carries `[USn]` only inside user-story phases, and includes an exact file path or directory.
