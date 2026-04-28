# Implementation Plan: Reviews & Moderation

**Branch**: `phase_1D_creating_specs` (working) · target merge: `022-reviews-moderation` | **Date**: 2026-04-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/phase-1D/022-reviews-moderation/spec.md`

## Summary

Deliver the Phase-1D customer-reviews module that turns Principle 15 ("only verified buyers MAY submit reviews; admins MUST be able to hide or delete; moderation support is REQUIRED") into a single backend module covering all 8 deliverable items from the implementation plan:

1. **Review entity** (Principle 24): one explicit `Review` state machine `pending_moderation | visible → flagged → hidden ↔ visible | deleted (terminal)`. Encoded in `ReviewStateMachine.cs`. Every transition writes an append-only audit row with structured `triggered_by` discriminator.
2. **Verified-buyer gate** (constitutional core): submission requires a `delivered`, non-refunded order line owned by the customer per spec 011's `IOrderLineDeliveryEligibilityQuery`. One review per `(customer_id, product_id)` globally enforced via a unique partial index; eligibility window default 180 days per market (Clarification Q1 of /speckit-specify session).
3. **Profanity filter + media-pending hold**: per-market wordlist (AR + EN, Arabic-normalized via spec 006's normalization). Text trips → `pending_moderation` with `filter_trip_terms[]`. Any media attachment → `pending_moderation` regardless of text result, with `media_attachment_review_required=true` (Clarification Q2 of /speckit-clarify).
4. **Single-locale authoring** (Clarification Q1 of /speckit-clarify): customer authors in one locale (`ar` or `en`); the unauthored locale stays NULL forever; storefront renders the authored locale with an `Original written in {locale}` annotation when the viewer's locale differs.
5. **Edit during pending-moderation** (Clarification Q3): edits ALLOWED in any non-deleted state including `pending_moderation`; each edit re-stamps `pending_moderation_started_at` and advances `xmin` row_version, invalidating in-progress moderator decisions via `409 reviews.moderation.version_conflict`.
6. **Community report flow with quality weighting** (Clarification Q4): 5 fixed reasons; signed-in customers only; one report per `(reporter, review)` (idempotent); reports are weighted by reporter quality (`account_age ≥ 14d` AND verified-buyer status; both per-market tunable); only "qualified" reports count toward the auto-flag threshold (default 3 in 30 days, per-market tunable).
7. **Admin moderation queue + canonical reviewer-display rule** (Clarification Q5): queue surfaces `pending_moderation` + `flagged` reviews with media inline + filter-tripped terms + community-reports + author display rendered as `first_name + " " + last_initial + "."` OR `review_display_handle` if set. Same display rule applies to storefront and queue (single source of truth).
8. **Auto-hide cascades**: subscribes to spec 013's `RefundCompleted` and spec 004's `CustomerAccountLocked` / `Deleted` events; auto-transitions affected reviews to `hidden` with `triggered_by=refund_event` / `account_locked` and recomputes the rating aggregate within 60 s.
9. **Rating aggregate** (FR-025–FR-029): per `(product_id, market_code)` denormalized read-side with `avg_rating`, `review_count`, 5-bucket `distribution`, `last_updated_utc`. Recomputed within 60 s of any countable transition by an immediate-on-transition path + a `RatingAggregateRebuildWorker` reconciliation safety net. Unauthenticated read.
10. **Multi-vendor readiness** (Principle 6): `vendor_id` slot reserved on every new row; never populated in V1.
11. **`reviews-v1` seeder**: idempotent; populates ≥ 1 row in each of 5 lifecycle states; seeds AR + EN profanity wordlists; bilingual editorial-grade reason-code keys.

No customer-facing UI ships in this spec. Customer storefront is owned by Phase 1C spec 014; the moderation queue UI is owned by spec 015. 022 ships only the backend contracts and seeders against which 014 / 015 build their screens.

## Technical Context

**Language/Version**: C# 12 / .NET 9 (LTS), PostgreSQL 16 (per spec 004 + ADR-022).

**Primary Dependencies**:
- `MediatR` v12.x + `FluentValidation` v11.x — vertical-slice handlers (ADR-003).
- `Microsoft.EntityFrameworkCore` v9.x — code-first migrations on the new `reviews` schema (ADR-004).
- `Microsoft.AspNetCore.Authorization` (built-in) — `[RequirePermission("reviews.*")]` attributes from spec 004's RBAC.
- `Modules/AuditLog/IAuditEventPublisher` (existing) — every state transition + every wordlist edit + every threshold edit + every admin note + every report submission.
- `Modules/Identity` consumables — RBAC primitives + new permissions `reviews.moderator`, `reviews.policy_admin`. The existing `ICustomerAccountLifecycleSubscriber` from spec 020 / 021 is reused for FR-031.
- `Modules/Shared/IAuditEventPublisher`, `Modules/Shared/AppDbContext` — existing; reused.
- New shared interfaces declared under `Modules/Shared/` (see Project Structure):
  - `IOrderLineDeliveryEligibilityQuery` — read-side eligibility check; spec 011 implements.
  - `IRefundCompletedSubscriber` + `IRefundCompletedPublisher` — events from spec 013.
  - `IRefundReversedSubscriber` + `IRefundReversedPublisher` — events from spec 013.
  - `IProductDisplayLookup` — minimal product-name-by-id lookup; spec 005 implements (NOT a foreign key — same loose-coupling pattern as specs 020 / 021 / 007-b).
  - `IRatingAggregateReader` — read API exposed by 022 for spec 005 (product detail) and spec 006 (search-result decoration) to consume.
  - `IReviewDisplayHandleQuery` — reads `(first_name, last_name, review_display_handle?)` from spec 019 customer profile; 019 implements (or 022 ships a stub fallback if 019 hasn't shipped the field yet — per spec 022 Assumptions).
  - `ReviewDomainEvents.cs` — 8 `INotification` records subscribed by spec 025.
- Profanity / Arabic-normalization: reuse spec 006 search's `IArabicNormalizer` (declared in `Modules/Search/Internal/`). 022 takes a read dependency on the published interface.
- `MessageFormat.NET` (already vendored by spec 003) — ICU AR/EN keys for every customer-visible / operator-visible reason code.

**Storage**: PostgreSQL (Azure Saudi Arabia Central per ADR-010). New `reviews` schema; 7 new tables:

- `reviews.reviews` — the lifecycled review entity.
- `reviews.review_moderation_decisions` — append-only per-decision audit detail (denormalized cache of the canonical audit log).
- `reviews.review_admin_notes` — append-only operator-side notes.
- `reviews.review_flags` — community-report rows with reporter `is_qualified` snapshot.
- `reviews.product_rating_aggregates` — per `(product_id, market_code)` denormalized read-side.
- `reviews.reviews_filter_wordlists` — per-market profanity / abuse terms.
- `reviews.reviews_market_schemas` — per-market policy (eligibility window, edit window, community thresholds, qualifying-reporter rules).

State writes use EF Core optimistic concurrency via Postgres `xmin` mapped as `IsRowVersion()` (the same pattern adopted in specs 020 / 021 / 007-b) for the concurrent-edit + concurrent-moderation cases.

**Testing**: xUnit + FluentAssertions + `WebApplicationFactory<Program>` integration harness. Testcontainers Postgres (per spec 003 contract — no SQLite shortcut). Contract tests assert HTTP shape parity between every `spec.md` Acceptance Scenario and the live handler. Property tests for state-machine invariants (no terminal→non-terminal, no double-decision, idempotent transitions). Concurrency tests for FR-019 + FR-022 (two moderators / two reporters). Cross-module subscriber tests use fake publishers shipped in `Modules/Shared/Testing/`. Time-driven tests use `FakeTimeProvider`. Profanity-filter coverage uses a wordlist-coverage matrix test suite (SC-010).

**Target Platform**: Backend-only in this spec. `services/backend_api/` ASP.NET Core 9 modular monolith. No Flutter, no Next.js — Phase 1C specs 014 / 015 deliver UI.

**Project Type**: .NET vertical-slice module under the modular monolith (ADR-023). Net-new top-level module: `Modules/Reviews/`.

**Performance Goals**:
- **Submission write path**: p95 ≤ 800 ms for a text-only review (eligibility query + filter scan + persist + audit + aggregate refresh).
- **Submission write path with media**: p95 ≤ 1200 ms (additional time for media-URL validation; persist; queue surfacing).
- **Aggregate read path**: p95 ≤ 50 ms (single-row PK lookup); cacheable for 60 s by upstream HTTP layer (FR-029).
- **Moderator queue list**: p95 ≤ 600 ms with 5 000 pending items per market, default page (50).
- **Review detail load** (admin): p95 ≤ 800 ms with full audit + flags + admin notes.
- **Aggregate refresh latency from transition**: p95 ≤ 60 s (SC-005); the immediate-on-transition path targets ≤ 5 s and the reconciliation worker provides the safety net.
- **Moderation decision write**: p95 ≤ 800 ms.
- **Report submission write**: p95 ≤ 500 ms.

**Constraints**:
- **Idempotency**: every state-transitioning POST endpoint requires `Idempotency-Key` (per spec 003 platform middleware); duplicates within 24 h return the original 200 response.
- **Concurrency guard**: every state-transitioning command uses an EF Core `RowVersion` (xmin) optimistic-concurrency check; the loser sees `409 reviews.moderation.version_conflict` (or its review-side analog `409 review.row.version_conflict`).
- **Hard-delete prohibition** (FR-005a): the API layer MUST return `405 review.row.delete_forbidden` for any `DELETE /v1/admin/reviews/{id}` route. Soft-state `deleted` is the only deletion path. Append-only tables (`review_moderation_decisions`, `review_admin_notes`, `review_flags`) MUST be guarded by Postgres `BEFORE UPDATE OR DELETE` triggers.
- **PII at rest**: review body + headline are customer-supplied free text and may contain PII; stored as plain TEXT (TDE covers at-rest). `review_display_handle` is sourced from spec 019's customer profile and is NOT mirrored in 022's tables.
- **PII in logs**: `ILogger` destructuring filters block any review body content from log output. Audit events MAY include the body in `before_jsonb` / `after_jsonb` for moderator forensics; access to those columns is gated by `reviews.moderator` permission.
- **Time source**: every state transition + every rate-limit window + every aggregate `last_updated_utc` reads `TimeProvider.System.GetUtcNow()`; tests inject `FakeTimeProvider`.
- **Worker idempotency**: `RatingAggregateRebuildWorker` (reconciliation safety net) and `ReviewIntegrityScanWorker` (SC-004) are safe to re-run; rebuilds are computed-from-scratch and overwrite atomically. Workers use the existing Postgres advisory-lock pattern from spec 020 to coordinate horizontally.
- **Storefront read-cache TTL**: 60 s (FR-029); the response includes `Cache-Control: public, max-age=60` headers consumed by upstream HTTP cache layers.
- **AR editorial**: every system-generated customer-visible string (reason codes, `Original written in {locale}` annotation, queue messages, notification copy) MUST have both `ar` and `en` ICU keys; AR strings flagged in `AR_EDITORIAL_REVIEW.md`.
- **Single-locale review content**: customer-supplied headline + body are stored in the customer's authoring `locale` only; 022 MUST NOT auto-translate (Principle 4).

**Scale/Scope**: ~22 HTTP endpoints (customer: 6, customer reports: 2, admin moderation: 6, admin notes: 2, admin policy: 4, lookups: 1, aggregate read: 1). **46 functional requirements** (FR-001–FR-042 plus FR-003a, FR-005a, FR-014a, FR-016a interleaved). 10 SCs. 7 key entities + 1 read-side aggregate. 1 five-state lifecycle. 7 net-new tables. 2 hosted workers. 8 lifecycle domain events. Target capacity at V1 launch: 5 000 reviews / day across both markets at steady state, peaks of 50 concurrent submissions, 100 concurrent storefront aggregate reads / second per market, 5 active moderators on the queue at any given time.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / ADR | Gate | Status |
|---|---|---|
| P3 Experience Model | Aggregate read endpoint is unauthenticated (FR-029) — browse remains unauth. Submission, edit, report, all moderation paths require auth. | PASS |
| P4 Arabic / RTL editorial | System-generated strings (reason codes, queue messages, "Original written in {locale}" annotation) bilingual-required (FR-035). Customer-supplied review content is single-locale per FR-006 — not machine-translated (Principle 4 protected). AR-locale screen render verified in SC-007. | PASS |
| P5 Market Configuration | `reviews_market_schemas` rows hold every per-market knob (eligibility window, edit window, community-report threshold + window, qualifying-reporter age + verified-buyer flag); per-market wordlists. No hardcoded EG/KSA branches. | PASS |
| P6 Multi-vendor-ready | `vendor_id` slot reserved on every new row. V1 always null and indexed. | PASS |
| P15 Reviews | Verified-buyer gate (FR-007); admin moderation hide / delete (FR-001, FR-004); review data linked to delivered + non-refunded order line. ALL of Principle 15's MUSTs covered. | PASS |
| P19 Notifications | 8 domain events declared; spec 025 subscribes; no in-line notification calls (FR-038). | PASS |
| P22 Fixed Tech | .NET 9, PostgreSQL 16, EF Core 9, MediatR — no deviation. | PASS |
| P23 Architecture | New vertical-slice module `Modules/Reviews/`; reuses existing seams (`IAuditEventPublisher`, RBAC, `IArabicNormalizer`, customer-account-lifecycle subscriber). No premature service extraction. | PASS |
| P24 State Machines | One explicit state machine (`ReviewState`, 5 states) documented in `data-model.md §3` with allowed states, transitions, triggers, actors, failure handling. | PASS |
| P25 Audit | Every state transition + every report + every wordlist edit + every threshold edit + every admin note emits an audit row (FR-002, FR-033, FR-034). SC-003 verifies. | PASS |
| P27 UX Quality | No UI here, but error payloads carry stable reason codes (`review.eligibility.no_delivered_purchase`, `review.report.cannot_report_own_review`, `reviews.moderation.delete_requires_super_admin`, etc.) for spec 014 / 015 to render. | PASS |
| P28 AI-Build Standard | Contracts file enumerates every endpoint's request / response / errors / reason codes. | PASS |
| P29 Required Spec Output | Goal, roles, rules, flow, states, data model, validation, API, edge cases, acceptance, phase, deps — all present in spec.md. | PASS |
| P30 Phasing | Phase 1D Milestone 7. Image-classifier hook, customer Q&A, helpful-vote, vendor responses all explicitly Out of Scope. | PASS |
| P31 Constitution Supremacy | No conflict. | PASS |
| ADR-001 Monorepo | Code lands under `services/backend_api/Modules/Reviews/`. | PASS |
| ADR-003 Vertical slice | One folder per slice under `Reviews/Customer/`, `Reviews/Admin/`, `Reviews/Aggregate/`, `Reviews/PolicyAdmin/`. | PASS |
| ADR-004 EF Core 9 | Code-first migrations under `Modules/Reviews/Persistence/Migrations/`. `SaveChangesInterceptor` audit hook from spec 003 reused. `ManyServiceProvidersCreatedWarning` suppressed in `ReviewsModule.cs` (project-memory rule). | PASS |
| ADR-010 KSA residency | All tables in the KSA-region Postgres; no cross-region replication. | PASS |

**No violations**. Complexity Tracking below documents intentional non-obvious design choices.

### Post-design re-check (after Phase 1 artifacts)

Re-evaluated after `data-model.md`, `contracts/reviews-and-moderation-contract.md`, `quickstart.md`, and `research.md` were authored. **No new violations introduced.**

- **P15 (re-emphasized)**: every constitutional MUST is bound to a specific FR + table column + acceptance scenario. Verified end-to-end. ✅
- **P5**: every market-tunable knob is sourced from `reviews_market_schemas` rows. ✅
- **P24**: the 5-state machine is encoded in `ReviewStateMachine.cs` with compile-time transition guards. ✅
- **P25**: 14 audit-event kinds documented in `data-model.md §5`. ✅
- **P28**: contracts file enumerates 22 endpoints + 6 cross-module interfaces with full reason-code inventory (~35 owned codes). ✅

## Project Structure

### Documentation (this feature)

```text
specs/phase-1D/022-reviews-moderation/
├── plan.md                  # This file
├── research.md              # Phase 0 — eligibility query design, refund-event subscription, Arabic-normalization reuse, aggregate refresh strategy, qualified-reporter evaluation, single-locale renderer, media-pending workflow, optimistic-concurrency on edits-in-pending
├── data-model.md            # Phase 1 — 7 tables, 1 state machine, ERD, 14 audit-event kinds, 8 domain events
├── contracts/
│   └── reviews-and-moderation-contract.md   # Phase 1 — every customer + admin moderation + admin policy + aggregate-read endpoint, every reason code, every domain event
├── quickstart.md            # Phase 1 — implementer walkthrough, first slice, aggregate-refresh smoke
├── checklists/
│   └── requirements.md      # quality gate (pass)
└── tasks.md                 # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
services/backend_api/
├── Modules/
│   ├── Shared/                                              # EXTENDED
│   │   ├── IOrderLineDeliveryEligibilityQuery.cs            # NEW — spec 011 implements
│   │   ├── IRefundCompletedSubscriber.cs                    # NEW — spec 013 publishes
│   │   ├── IRefundCompletedPublisher.cs                     # NEW
│   │   ├── IRefundReversedSubscriber.cs                     # NEW
│   │   ├── IRefundReversedPublisher.cs                      # NEW
│   │   ├── IProductDisplayLookup.cs                         # NEW — spec 005 implements (loose-coupling)
│   │   ├── IRatingAggregateReader.cs                        # NEW — spec 022 publishes; specs 005, 006 consume
│   │   ├── IReviewDisplayHandleQuery.cs                     # NEW — spec 019 implements (or stub if 019 hasn't shipped the field)
│   │   ├── ReviewDomainEvents.cs                            # NEW — 8 INotification records (Submitted/Published/HeldForModeration/Flagged/Hidden/Deleted/Reinstated/AutoHidden)
│   │   └── (existing files unchanged; ICustomerAccountLifecycleSubscriber reused from spec 020)
│   ├── Reviews/                                             # NEW MODULE
│   │   ├── ReviewsModule.cs                                 # AddReviewsModule(IServiceCollection); MediatR scan; AddDbContext suppressing ManyServiceProvidersCreatedWarning; register subscribers; register IRatingAggregateReader implementation
│   │   ├── Primitives/
│   │   │   ├── ReviewState.cs                               # enum: PendingModeration, Visible, Flagged, Hidden, Deleted
│   │   │   ├── ReviewStateMachine.cs                        # transition rules + guard predicates
│   │   │   ├── ReviewActorKind.cs                           # enum: Customer, Moderator, PolicyAdmin, SuperAdmin, System
│   │   │   ├── ReviewReasonCode.cs                          # enum + ICU-key mapper for all owned reason codes
│   │   │   ├── ReviewMarketPolicy.cs                        # value-object resolved from reviews_market_schemas row
│   │   │   ├── QualifiedReporterPolicy.cs                   # pure function: (reporter, threshold) → bool (FR-023)
│   │   │   ├── ReviewerDisplayRenderer.cs                   # canonical FR-016a render rule; pure function
│   │   │   └── ReviewTriggerKind.cs                         # enum: customer_submission, customer_edit, community_report_threshold, refund_event, account_locked, moderator_action, manual_super_admin
│   │   ├── Customer/
│   │   │   ├── SubmitReview/                                # text + optional media; runs filter; persists pending or visible
│   │   │   ├── UpdateReview/                                # within edit window; re-runs filter; re-stamps pending if needed
│   │   │   ├── ListMyReviews/
│   │   │   ├── GetMyReview/
│   │   │   ├── ReportReview/                                # community report
│   │   │   └── GetReportReasons/                            # static lookup of the 5 fixed reasons + ICU keys
│   │   ├── Admin/
│   │   │   ├── ListModerationQueue/
│   │   │   ├── GetReviewDetail/
│   │   │   ├── DecideModeration/                            # visible | hidden (super_admin: deleted)
│   │   │   ├── AddAdminNote/
│   │   │   ├── ListAdminNotes/
│   │   │   └── ListReviewsByCustomer/                       # support / dispute investigation
│   │   ├── Aggregate/
│   │   │   ├── ReadProductRating/                           # public unauth endpoint
│   │   │   └── RatingAggregateRecomputer.cs                 # in-process service; called inline on transition + by reconciliation worker
│   │   ├── PolicyAdmin/
│   │   │   ├── ListWordlistTerms/
│   │   │   ├── UpsertWordlistTerm/
│   │   │   ├── DeleteWordlistTerm/
│   │   │   └── UpdateMarketSchema/                          # eligibility window, edit window, community thresholds, qualifying-reporter rules
│   │   ├── Subscribers/
│   │   │   ├── RefundCompletedHandler.cs                    # auto-hide reviews on refund (FR-030)
│   │   │   ├── RefundReversedHandler.cs                     # surfaces "needs review" indicator; does NOT auto-reinstate (FR-032)
│   │   │   └── CustomerAccountLifecycleHandler.cs           # auto-hide on account_locked / deleted (FR-031)
│   │   ├── Workers/
│   │   │   ├── RatingAggregateRebuildWorker.cs              # daily reconciliation; advisory-lock guarded
│   │   │   └── ReviewIntegrityScanWorker.cs                 # daily; finds visible/flagged reviews tied to refunded order lines (SC-004)
│   │   ├── Filtering/
│   │   │   ├── ProfanityFilter.cs                           # consumes IArabicNormalizer from Modules/Search/; applies wordlist
│   │   │   └── MediaAttachmentDetector.cs                   # pure function: (review) → bool (FR-014a)
│   │   ├── Authorization/
│   │   │   └── ReviewsPermissions.cs                        # reviews.moderator, reviews.policy_admin
│   │   ├── Entities/
│   │   │   ├── Review.cs
│   │   │   ├── ReviewModerationDecision.cs
│   │   │   ├── ReviewAdminNote.cs
│   │   │   ├── ReviewFlag.cs
│   │   │   ├── ProductRatingAggregate.cs
│   │   │   ├── ReviewsFilterWordlist.cs
│   │   │   └── ReviewsMarketSchema.cs
│   │   ├── Persistence/
│   │   │   ├── ReviewsDbContext.cs
│   │   │   ├── Configurations/                              # IEntityTypeConfiguration<T> per entity
│   │   │   └── Migrations/                                  # net-new; creates `reviews` schema + 7 tables + append-only triggers
│   │   ├── Messages/
│   │   │   ├── reviews.en.icu                               # system-generated EN keys
│   │   │   ├── reviews.ar.icu                               # system-generated AR keys (editorial-grade)
│   │   │   └── AR_EDITORIAL_REVIEW.md
│   │   └── Seeding/
│   │       ├── ReviewsReferenceDataSeeder.cs                # KSA + EG market schemas + seed wordlists (Dev+Staging+Prod, idempotent)
│   │       └── ReviewsV1DevSeeder.cs                        # synthetic reviews spanning all 5 states (Dev+Staging only, SeedGuard)
└── tests/
    └── Reviews.Tests/
        ├── Unit/                                            # state machine, qualified-reporter policy, reviewer-display renderer, profanity-filter (wordlist coverage matrix), reason-code mapper
        ├── Integration/                                     # WebApplicationFactory + Testcontainers Postgres; every customer + admin slice; concurrency guards; aggregate refresh latency; subscriber tests; integrity-scan worker
        └── Contract/                                        # asserts every Acceptance Scenario from spec.md against live handlers
```

**Structure Decision**: Net-new `Modules/Reviews/` vertical-slice module under the modular monolith. Cross-module event types and read interfaces live under `Modules/Shared/` to avoid module dependency cycles (project-memory rule). The `Customer/`, `Admin/`, `Aggregate/`, `PolicyAdmin/` sibling layout enforces visibly that the four actor surfaces consume the same state machine but expose disjoint endpoints with disjoint RBAC. The `Subscribers/` folder houses the cross-module event consumers; the `Workers/` folder houses the reconciliation safety nets.

## Implementation Phases

The `/speckit-tasks` run will expand each phase into dependency-ordered tasks. Listed here so reviewers can sanity-check ordering before tasks generation.

| Phase | Scope | Blockers cleared |
|---|---|---|
| A. Primitives | `ReviewState`, `ReviewStateMachine`, `ReviewReasonCode`, `ReviewMarketPolicy`, `QualifiedReporterPolicy`, `ReviewerDisplayRenderer`, `ReviewTriggerKind` | Foundation for all slices |
| B. Persistence + migrations | 7 entities + EF configurations + initial migration; `ReviewsDbContext` with warning suppression; append-only triggers on the 3 audit-detail tables | Unblocks all slices and workers |
| C. Reference seeder | `ReviewsReferenceDataSeeder` (KSA + EG market schemas + KSA + EG seed wordlists; idempotent across all envs) | Unblocks integration tests + Staging/Prod boot |
| D. Cross-module shared declarations | `IOrderLineDeliveryEligibilityQuery`, `IRefundCompleted/ReversedSubscriber+Publisher`, `IProductDisplayLookup`, `IRatingAggregateReader`, `IReviewDisplayHandleQuery`, `ReviewDomainEvents` | Unblocks specs 011 / 013 / 005 / 019 / 025 to author their PRs in parallel |
| E. Filter + display primitives | `ProfanityFilter` consumes `IArabicNormalizer` from Modules/Search/; `MediaAttachmentDetector`; `ReviewerDisplayRenderer` with `IReviewDisplayHandleQuery` | Foundation for submission + queue rendering |
| F. Customer slices — submission | SubmitReview → UpdateReview → ListMyReviews → GetMyReview | FR-006–FR-010 |
| G. Customer slices — reporting | ReportReview → GetReportReasons | FR-020–FR-024 |
| H. Admin moderation slices | ListModerationQueue → GetReviewDetail → DecideModeration → AddAdminNote → ListAdminNotes → ListReviewsByCustomer | FR-015–FR-019, FR-033 |
| I. Aggregate slices + recomputer | ReadProductRating (unauth) + `RatingAggregateRecomputer` invoked inline on every countable transition | FR-025–FR-029 |
| J. PolicyAdmin slices | List/Upsert/DeleteWordlistTerm + UpdateMarketSchema (super_admin / reviews.policy_admin) | FR-011, FR-023, P5 |
| K. Subscribers (cross-module event consumers) | RefundCompletedHandler, RefundReversedHandler, CustomerAccountLifecycleHandler | FR-030, FR-031, FR-032 |
| L. Workers (reconciliation + integrity) | RatingAggregateRebuildWorker (daily), ReviewIntegrityScanWorker (daily, SC-004) | SC-004, SC-005 reconciliation safety net |
| M. Authorization wiring | `ReviewsPermissions.cs` constants + `[RequirePermission]` attributes; spec 015 wires role bindings on its PR | Permission boundary |
| N. Domain events + 025 contract | Publish 8 events on each lifecycle transition; subscribed by spec 025 (lands on 025's PR, not here) | FR-037, FR-038 |
| O. Contracts + OpenAPI | Regenerate `openapi.reviews.json`; assert contract test suite green; document every reason code | Guardrail #2 |
| P. AR/EN editorial | All system-generated strings ICU-keyed; AR strings flagged in `AR_EDITORIAL_REVIEW.md` | P4 |
| Q. `reviews-v1` dev seeder | `ReviewsV1DevSeeder` — synthetic verified-buyer reviews spanning all 5 states; profanity-tripped; community-reported; auto-hidden samples | FR's seeder requirement, SC-008 |
| R. Integration / DoD | Full Testcontainers run; aggregate-refresh latency test (SC-005); profanity-coverage matrix (SC-010); concurrency-guard test (FR-019, FR-022); subscriber tests; integrity scan; fingerprint; DoD checklist; audit-coverage script | PR gate |

## Complexity Tracking

> Constitution Check passed without violations. The rows below are *intentional non-obvious design choices* captured so future maintainers don't undo them accidentally.

| Design choice | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Net-new `Modules/Reviews/` module rather than co-locating with `Catalog` or `Orders` | Reviews carry their own state machine, RBAC, audit, and cross-module subscribers (refund + account-lifecycle) — none of which belong in catalog or orders. | Co-location would force catalog or orders to take a hard dependency on reviews logic and break the modular-monolith boundary. |
| Single 5-state lifecycle (`pending_moderation \| visible → flagged → hidden ↔ visible \| deleted`) rather than two-state-plus-flags | Compile-time guarantee that no transition path is silently legal. Mirror of the pattern in specs 020 / 021 / 007-b. | A two-state `visible \| hidden` plus orthogonal flags loses transition-guard expressiveness; auditors can't tell if a row is "currently held for moderation" vs "moderator approved earlier and customer has now edited". |
| Append-only `review_moderation_decisions` + `review_admin_notes` + `review_flags` tables guarded by `BEFORE UPDATE OR DELETE` triggers | Principle 25 — moderation history must be immutable for dispute resolution + legal traceability. Same pattern as spec 020 / 021 / 007-b's audit-event tables. | A single mutable JSONB `audit_log` column on the `reviews` row destroys append-only guarantees and is hard to query for "decisions by moderator X this month". |
| `product_rating_aggregates` denormalized (NOT computed on read) | FR-029 + storefront read p95 ≤ 50 ms. Computing on read would force a JOIN + aggregation across potentially millions of rows per product. | An on-read aggregate query against `reviews` cannot meet the 50 ms p95 at scale. |
| Aggregate refresh has TWO paths (immediate-on-transition + daily reconciliation worker) | Immediate path keeps SC-005 ≤ 60 s in 99 %+ cases; reconciliation worker catches missed events (process crash, transactional rollback after immediate-publish, etc.) | Immediate-only risks divergence on rare failures. Reconciliation-only blows SC-005. Both together is the reliable design. |
| Single-locale review content with `null` for the unauthored locale | Clarification Q1 of /speckit-clarify locks: editorial-grade Arabic forbids machine translation (Principle 4). Forcing bilingual authoring at submit blocks 95 % of legitimate submissions. | A bilingual-required field would either kill submission rates or invite garbage second-locale content. |
| Image-bearing reviews always held for moderation regardless of text-filter result | Clarification Q2 — image-content classification is non-trivial; the safe stance at V1 is human review. | Auto-publishing images relies on community reports as the only safety net, exposing the storefront to abuse on day 1. |
| Edits during `pending_moderation` re-stamp `pending_moderation_started_at` and advance `xmin` row_version | Clarification Q3 — balances "I want to fix a typo" UX with "moderator can't approve a stale version". | Blocking edits is the worst customer UX; allowing edits without re-stamp races the moderator. |
| Qualified-reporter evaluation captured at report time (not at threshold-evaluation time) | Clarification Q4 — the threshold counter must be reproducible during audit; capturing the evaluation at report time pins the policy at that moment. | Re-evaluating at threshold-check time creates a moving target where the same report is "qualified" or not depending on when the threshold check runs. |
| Canonical reviewer-display rule computed at read time (not denormalized onto the review row) | Clarification Q5 — a customer's name change should apply retroactively to all their reviews; denormalized would require a backfill. | Denormalizing forces every name-change in spec 019 to schedule a 022 backfill job. |
| `ReviewsMarketSchema` table for per-market policy (rather than hardcoded constants or env-var config) | Principle 5 — every market-tunable knob without a code deploy. Same pattern as specs 020 / 021 / 007-b. | Env-var config requires a deploy for KSA-vs-EG variance; hardcoded constants are non-compliant with P5. |
| `vendor_id` slot reserved on every new row but never populated in V1 | P6 multi-vendor-readiness without paying schema-migration cost in Phase 2. Same pattern as specs 020 / 021 / 007-b. | Omitting forces a migration of every reviews-schema table when vendor-scoped review moderation lands. |
| Refund-reversed event does NOT auto-reinstate the review | Clarification Q5 of /speckit-specify — refund reversals are rare and often manual; auto-reinstating could surface a review that the customer no longer wants public. Admins are notified instead. | Auto-reinstate creates surprise resurrections; admin discretion is safer. |
| `ICustomerAccountLifecycleSubscriber` reused from spec 020, NOT re-declared | Same lifecycle event; second declaration would create a duplicate subscription in spec 020's bus. | Duplicating the interface forces re-implementation in 020 + 021 + 022 — pointless. |
