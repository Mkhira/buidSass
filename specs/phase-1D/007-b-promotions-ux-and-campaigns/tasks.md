---
description: "Task list — Spec 007-b Promotions UX & Campaigns (Phase 1D · Milestone 7)"
---

# Tasks: Promotions UX & Campaigns

**Input**: Design documents from `/specs/phase-1D/007-b-promotions-ux-and-campaigns/`
**Prerequisites**: `plan.md` (required), `spec.md` (required for user stories), `research.md`, `data-model.md`, `contracts/promotions-ux-and-campaigns-contract.md`

**Tests**: Test tasks are included because the project's existing standard (specs 020 / 021) requires xUnit + FluentAssertions + Testcontainers Postgres + contract tests for every Acceptance Scenario. Spec 007-b inherits the same standard (plan §Testing).

**Organization**: Tasks are grouped by user story (P1 → P3) so each story can be implemented and tested independently.

---

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Parallelizable — different files, no dependencies on incomplete tasks in the same phase.
- **[Story]**: Maps to spec.md user stories (`[US1]`–`[US7]`). Setup, Foundational, and Polish phases carry no story label.
- Every task description includes the exact target file path or directory.

## Path Conventions (per [plan.md §Project Structure](./plan.md))

- Backend: `services/backend_api/Modules/Pricing/...` (extending the existing 007-a module).
- Cross-module shared types: `services/backend_api/Modules/Shared/...`.
- Tests: `services/backend_api/tests/Pricing.Tests/{Unit,Integration,Contract}/`.
- Spec dir: `specs/phase-1D/007-b-promotions-ux-and-campaigns/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: confirm the existing module skeleton and prerequisites are at DoD, and prepare 007-b-specific scaffolding.

- [ ] T001 Verify 007-a `Pricing` module is at DoD on `main`: `IPriceCalculator`, the four engine tables, and the `Preview` mode hook are all present in `services/backend_api/Modules/Pricing/`.
- [ ] T002 Verify spec 015 `admin-foundation` contract (RBAC primitives, audit panel, idempotency middleware) is merged on `main`.
- [ ] T003 [P] Verify `ManyServiceProvidersCreatedWarning` suppression is still present in `services/backend_api/Modules/Pricing/PricingModule.cs` (project-memory rule R14); add a CI grep guard `scripts/ci/assert-pricing-warning-suppressed.sh`.
- [ ] T004 [P] Add the new permission constants to the project's RBAC seed list in `services/backend_api/Modules/Identity/Authorization/PermissionRegistry.cs`: `commercial.operator`, `commercial.b2b_authoring`, `commercial.approver`, `commercial.threshold_admin`.
- [ ] T005 [P] Update the OpenAPI generation task in `services/backend_api/services.sln`'s `dotnet swagger tofile` step to emit `services/backend_api/openapi.pricing.commercial.json` (per research §R18).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: primitives, persistence (3 migrations + entities + DbContext extensions), cross-module shared declarations, authorization wiring, and reference-data seeder. **No user-story work begins until this phase is complete.**

### Primitives

- [ ] T006 [P] Create `services/backend_api/Modules/Pricing/Primitives/LifecycleState.cs` — enum `{Draft, Scheduled, Active, Deactivated, Expired}` per data-model §3.1.
- [ ] T007 [P] Create `services/backend_api/Modules/Pricing/Primitives/LifecycleStateMachine.cs` — pure-function `TryTransition(from, trigger, nowUtc, out to, out reasonCode)` covering every transition row in data-model §3.1.
- [ ] T008 [P] Create `services/backend_api/Modules/Pricing/Primitives/BusinessPricingState.cs` — enum `{Active, Deactivated}`.
- [ ] T009 [P] Create `services/backend_api/Modules/Pricing/Primitives/BusinessPricingStateMachine.cs` — `TryTransition` for the 4 row-row pairs in data-model §3.2.
- [ ] T010 [P] Create `services/backend_api/Modules/Pricing/Primitives/CommercialReasonCode.cs` — static class with all 49 owned codes from contract §11; xunit theory verifying every enum value has an ICU key in both locale files (R10 verification hook).
- [ ] T011 [P] Create `services/backend_api/Modules/Pricing/Primitives/CommercialActorKind.cs` — enum `{Operator, B2BAuthor, Approver, SuperAdmin, System}`.
- [ ] T012 [P] Create `services/backend_api/Modules/Pricing/Primitives/CommercialThresholdPolicy.cs` — value object resolving from a `pricing.commercial_thresholds` row; encapsulates per-criterion null-disable semantics.
- [ ] T013 [P] Create `services/backend_api/Modules/Pricing/Primitives/HighImpactGate.cs` — pure function `IsTriggered(rule, threshold) → bool` honoring FR-025 four criteria.

### Persistence — entities

- [ ] T014 Amend `services/backend_api/Modules/Pricing/Entities/Coupon.cs` — add lifecycle columns (state, state_changed_at_utc, state_changed_by_actor_id, state_changed_reason_note), `vendor_id?`, `display_in_banners`, `applies_to_broken`, `applies_to_broken_at_utc?`, `row_version` per data-model §2.1.
- [ ] T015 Amend `services/backend_api/Modules/Pricing/Entities/Promotion.cs` — same as T014 plus `banner_eligible`; verify `priority` column already present from 007-a per data-model §2.2.
- [ ] T016 Amend `services/backend_api/Modules/Pricing/Entities/ProductTierPrice.cs` — add `company_id?`, `copied_from_tier_id?`, `state`, lifecycle metadata, `company_link_broken*`, `vendor_id?`, `row_version` per data-model §2.3.
- [ ] T017 [P] Create `services/backend_api/Modules/Pricing/Entities/Campaign.cs` per data-model §2.4 (with `name_ar`/`name_en`, lifecycle columns, `vendor_id?`).
- [ ] T018 [P] Create `services/backend_api/Modules/Pricing/Entities/CampaignLink.cs` per data-model §2.5.
- [ ] T019 [P] Create `services/backend_api/Modules/Pricing/Entities/PreviewProfile.cs` per data-model §2.6 (with `visibility`, `created_by`, `cart_lines` jsonb).
- [ ] T020 [P] Create `services/backend_api/Modules/Pricing/Entities/CommercialThreshold.cs` per data-model §2.8.
- [ ] T021 [P] Create `services/backend_api/Modules/Pricing/Entities/CommercialApproval.cs` per data-model §2.7 (with the unique `(target_kind, target_id)` constraint and the `approver_actor_id <> author_actor_id` check).
- [ ] T022 [P] Create `services/backend_api/Modules/Pricing/Entities/CommercialAuditEvent.cs` per data-model §2.9 (append-only via trigger).

### Persistence — DbContext, configurations, migrations

- [ ] T023 Amend `services/backend_api/Modules/Pricing/Persistence/PricingDbContext.cs` — register the 6 new `DbSet<>`s (Campaign, CampaignLink, PreviewProfile, CommercialThreshold, CommercialApproval, CommercialAuditEvent).
- [ ] T024 [P] Add `services/backend_api/Modules/Pricing/Persistence/Configurations/CouponConfiguration.cs` (or amend existing) — wire `state` enum mapping, default value, the new indexes, and the `IsRowVersion()` mapping for `xmin`.
- [ ] T025 [P] Add `services/backend_api/Modules/Pricing/Persistence/Configurations/PromotionConfiguration.cs` (or amend existing) — same pattern.
- [ ] T026 [P] Add `services/backend_api/Modules/Pricing/Persistence/Configurations/ProductTierPriceConfiguration.cs` (or amend existing) — XOR check `chk_tier_xor_company`, two unique partial indexes, `IsRowVersion()`.
- [ ] T027 [P] Add `services/backend_api/Modules/Pricing/Persistence/Configurations/CampaignConfiguration.cs` (and `CampaignLinkConfiguration.cs`).
- [ ] T028 [P] Add `services/backend_api/Modules/Pricing/Persistence/Configurations/PreviewProfileConfiguration.cs`.
- [ ] T029 [P] Add `services/backend_api/Modules/Pricing/Persistence/Configurations/CommercialThresholdConfiguration.cs` and `CommercialApprovalConfiguration.cs`.
- [ ] T030 [P] Add `services/backend_api/Modules/Pricing/Persistence/Configurations/CommercialAuditEventConfiguration.cs` — wire the append-only trigger via raw SQL `OnDelete + OnUpdate -> raise_immutable_audit_violation()` (data-model §2.9).
- [ ] T031 Generate migration `AddLifecycleColumnsToCouponsAndPromotions` via `dotnet ef migrations add ...`; verify Up + Down compile and apply cleanly on a Testcontainers Postgres.
- [ ] T032 Generate migration `ExtendProductTierPricesForCompanyOverrides`; include the XOR check constraint and the two unique partial indexes.
- [ ] T033 Generate migration `AddCommercialAuthoringTables`; include the append-only trigger function `raise_immutable_audit_violation()` and its `BEFORE UPDATE OR DELETE` trigger on `pricing.commercial_audit_events`.

### Cross-module shared declarations

- [ ] T034 [P] Create `services/backend_api/Modules/Shared/ICatalogSkuArchivedSubscriber.cs` and `ICatalogSkuArchivedPublisher.cs` per data-model §7.
- [ ] T035 [P] Create `services/backend_api/Modules/Shared/IB2BCompanySuspendedSubscriber.cs` and `IB2BCompanySuspendedPublisher.cs`.
- [ ] T036 [P] Create `services/backend_api/Modules/Shared/ICheckoutGraceWindowProvider.cs`.
- [ ] T037 [P] Create `services/backend_api/Modules/Shared/CommercialDomainEvents.cs` containing all 10 `INotification` records from data-model §6.
- [ ] T038 [P] Add `services/backend_api/Modules/Shared/Testing/FakeCatalogSkuArchivedPublisher.cs` and `FakeB2BCompanySuspendedPublisher.cs` for use by `Pricing.Tests` (research §R3 verification harness).

### Authorization + threshold seeder

- [ ] T039 [P] Create `services/backend_api/Modules/Pricing/Authorization/CommercialPermissions.cs` exposing the 4 permission constants for `[RequirePermission(...)]` attributes.
- [ ] T040 [P] Create `services/backend_api/Modules/Pricing/Seeding/PricingThresholdsSeeder.cs` — upserts KSA + EG rows per research §R8 (gate ON, conservative seeded thresholds, 1800 s grace); idempotent across all environments.
- [ ] T041 Amend `services/backend_api/Modules/Pricing/PricingModule.cs` — register all new MediatR handlers, the threshold seeder, the new RBAC permissions, the `ICheckoutGraceWindowProvider` implementation, and the upcoming workers (registration is fine before worker code lands; DI resolves at runtime).

### Foundational tests

- [ ] T042 [P] Unit test `services/backend_api/tests/Pricing.Tests/Unit/Primitives/LifecycleStateMachineTests.cs` — every valid transition + every invalid transition + idempotency; xUnit theory.
- [ ] T043 [P] Unit test `tests/Pricing.Tests/Unit/Primitives/BusinessPricingStateMachineTests.cs`.
- [ ] T044 [P] Unit test `tests/Pricing.Tests/Unit/Primitives/HighImpactGateTests.cs` — each criterion individually + combined; gate-disabled per market via `gate_enabled=false` short-circuit.
- [ ] T045 [P] Unit test `tests/Pricing.Tests/Unit/Primitives/CommercialThresholdPolicyTests.cs` — null-criterion-disables-only-that-criterion; loaded from a fake `pricing.commercial_thresholds` row.
- [ ] T046 [P] Unit test `tests/Pricing.Tests/Unit/Primitives/CommercialReasonCodeIcuKeyTests.cs` — every code resolves to non-empty `en` and `ar` ICU keys (R10 verification hook).
- [ ] T047 Integration test `tests/Pricing.Tests/Integration/Persistence/MigrationApplicationTests.cs` — applies all 3 new migrations to a Testcontainers Postgres in order; asserts schema shape via `pg_catalog` queries.
- [ ] T048 Integration test `tests/Pricing.Tests/Integration/Persistence/CommercialAuditEventAppendOnlyTests.cs` — confirms `UPDATE` and `DELETE` on `pricing.commercial_audit_events` raise the trigger error.
- [ ] T049 Integration test `tests/Pricing.Tests/Integration/Seeding/PricingThresholdsSeederTests.cs` — runs the seeder twice; asserts exactly 2 rows; asserts `gate_enabled=true` and seeded values per market.

**Checkpoint**: Phase 2 complete — Foundation ready. User stories may proceed in parallel.

---

## Phase 3: User Story 1 — Operator creates scheduled coupon and previews it (Priority: P1) 🎯 MVP

**Story goal**: deliver coupon authoring + preview tool end-to-end. After this phase, an operator can author a coupon, preview it against a sample profile, and schedule it.

**Independent test**: sign in as `commercial.operator`, create a coupon with future `valid_from`, open Preview against a seeded sample profile, save. Verify state `scheduled`, audit row, preview matched runtime explanation hash post-activation.

### Tests for User Story 1

- [ ] T050 [P] [US1] Contract test `tests/Pricing.Tests/Contract/Coupons/CreateCouponContractTests.cs` — every Acceptance Scenario from spec.md User Story 1 (5 scenarios).
- [ ] T051 [P] [US1] Integration test `tests/Pricing.Tests/Integration/Admin/Coupons/CreateCouponTests.cs` — happy path + duplicate-code + bilingual-required + invalid-window + value-out-of-range.
- [ ] T052 [P] [US1] Integration test `tests/Pricing.Tests/Integration/Admin/Coupons/ScheduleCouponTests.cs` — `valid_from` future → `scheduled`, past → `active`; `LifecycleTimerWorker` integration deferred to Polish phase.
- [ ] T053 [P] [US1] Integration test `tests/Pricing.Tests/Integration/Admin/Coupons/UpdateCouponTests.cs` — pricing-field lock when `active`, non-pricing-field passes; row_version mismatch returns `409 commercial.row.version_conflict`.
- [ ] T054 [P] [US1] Integration test `tests/Pricing.Tests/Integration/Admin/Preview/PreviewPriceExplanationTests.cs` — preview output hash matches runtime engine output for the same profile + saved coupon (research §R2 verification hook).
- [ ] T055 [P] [US1] Performance test `tests/Pricing.Tests/Integration/Performance/PreviewP95Tests.cs` — p95 ≤ 200 ms over 100 calls for a 20-line cart (SC-002).

### Implementation for User Story 1

- [ ] T056 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/Coupons/CreateCoupon/{Endpoint,Request,Response,Handler,Validator,Mapper}.cs` per contract §2.1 and quickstart §4.
- [ ] T057 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/Coupons/UpdateCoupon/...` per contract §2.2.
- [ ] T058 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/Coupons/ScheduleCoupon/...` per contract §2.3, including the high-impact-gate fork (returns `403 coupon.activation.requires_approval` when triggered).
- [ ] T059 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/Coupons/DeactivateCoupon/...` per contract §2.4 — emits `CouponDeactivated` with `in_flight_grace_seconds` from threshold row.
- [ ] T060 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/Coupons/ReactivateCoupon/...` per contract §2.5.
- [ ] T061 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/Coupons/CloneAsDraft/...` per contract §2.6.
- [ ] T062 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/Coupons/ListCoupons/...` per contract §2.7 with paging + filters + RBAC-shaped responses.
- [ ] T063 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/Coupons/GetCoupon/...` per contract §2.8 (includes `audit_summary` from last 10 `commercial_audit_events`).
- [ ] T064 [P] [US1] Wire the `DELETE /v1/admin/commercial/coupons/{id}` route to return `405 commercial.row.delete_forbidden` per contract §2.9 (FR-005a).
- [ ] T065 [US1] Implement `services/backend_api/Modules/Pricing/Admin/Preview/PreviewPriceExplanation/{Endpoint,Request,Response,Handler}.cs` per contract §7 and quickstart §8 — wires through 007-a's `IPriceCalculator.Calculate(ctx)` in Preview mode with an `IInFlightRuleOverlay` scope.
- [ ] T066 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/PreviewProfiles/UpsertPreviewProfile/...` per contract §6.1.
- [ ] T067 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/PreviewProfiles/PromoteToShared/...` per contract §6.2 — guarded on `commercial.approver`.
- [ ] T068 [P] [US1] Implement `services/backend_api/Modules/Pricing/Admin/PreviewProfiles/ListPreviewProfiles/...` per contract §6.3 — RBAC-scoped to `personal-by-creator` + all `shared`.

**Checkpoint**: User Story 1 fully implemented and tested. MVP slice ready to demo.

---

## Phase 4: User Story 2 — Operator schedules promotion targeting SKU list with stacking (Priority: P1) 🎯 MVP

**Story goal**: deliver promotion authoring + SKU-overlap warning + stacking semantics.

**Independent test**: create a promotion with `stacks_with_coupons=false`, run preview against a profile with a valid coupon, verify the engine returns `appliedAmount=0` for the coupon layer with reason `pricing.coupon.suppressed_by_promotion_no_stack`.

### Tests for User Story 2

- [ ] T069 [P] [US2] Contract test `tests/Pricing.Tests/Contract/Promotions/CreatePromotionContractTests.cs` — every Acceptance Scenario (5).
- [ ] T070 [P] [US2] Integration test `tests/Pricing.Tests/Integration/Admin/Promotions/SchedulePromotionOverlapWarningTests.cs` — overlap warning + acknowledgement flow.
- [ ] T071 [P] [US2] Integration test `tests/Pricing.Tests/Integration/Admin/Promotions/PromotionPricingFieldLockTests.cs` — pricing-field edits rejected when `active`.
- [ ] T072 [P] [US2] Integration test `tests/Pricing.Tests/Integration/Admin/Promotions/BogoBundleTargetSkuTests.cs` — BOGO requires `reward_sku`; bundle requires `bundle_sku`; archived target → `400 promotion.target_sku_invalid`.

### Implementation for User Story 2

- [ ] T073 [P] [US2] Implement `services/backend_api/Modules/Pricing/Admin/Promotions/CreatePromotion/...` per contract §3.
- [ ] T074 [P] [US2] Implement `services/backend_api/Modules/Pricing/Admin/Promotions/UpdatePromotion/...`.
- [ ] T075 [P] [US2] Implement `services/backend_api/Modules/Pricing/Admin/Promotions/SchedulePromotion/...` — with the SKU-overlap warning logic from quickstart §5; honors `acknowledge_overlap` flag.
- [ ] T076 [P] [US2] Implement `services/backend_api/Modules/Pricing/Admin/Promotions/DeactivatePromotion/...`.
- [ ] T077 [P] [US2] Implement `services/backend_api/Modules/Pricing/Admin/Promotions/ReactivatePromotion/...`.
- [ ] T078 [P] [US2] Implement `services/backend_api/Modules/Pricing/Admin/Promotions/CloneAsDraft/...`.
- [ ] T079 [P] [US2] Implement `services/backend_api/Modules/Pricing/Admin/Promotions/ListPromotions/...` and `GetPromotion/...`.
- [ ] T080 [P] [US2] Wire `DELETE /v1/admin/commercial/promotions/{id}` to `405 commercial.row.delete_forbidden`.

**Checkpoint**: User Story 2 fully implemented.

---

## Phase 5: User Story 3 — B2B authoring user maintains tier table + company override (Priority: P1) 🎯 MVP

**Story goal**: deliver business-pricing authoring (tier rows + company overrides + bulk import).

**Independent test**: sign in as `commercial.b2b_authoring`, edit one tier row + one company override, verify both rows persist with correct discriminator (`tier_id` set vs `company_id` set), and the engine resolves the company override ahead of the tier row.

### Tests for User Story 3

- [ ] T081 [P] [US3] Contract test `tests/Pricing.Tests/Contract/BusinessPricing/EditTierRowContractTests.cs` and `EditCompanyOverrideContractTests.cs` — every Acceptance Scenario (5).
- [ ] T082 [P] [US3] Integration test `tests/Pricing.Tests/Integration/Admin/BusinessPricing/ForbiddenForOperatorTests.cs` — `commercial.operator` without `b2b_authoring` receives `403`.
- [ ] T083 [P] [US3] Integration test `tests/Pricing.Tests/Integration/Admin/BusinessPricing/CompanyOverrideResolutionTests.cs` — engine resolves company override ahead of tier row for the same SKU + same customer.
- [ ] T084 [P] [US3] Integration test `tests/Pricing.Tests/Integration/Admin/BusinessPricing/BulkImportPreviewCommitTests.cs` — preview-then-commit flow, token expiry at 15 min, snapshot-changed `409`.
- [ ] T085 [P] [US3] Integration test `tests/Pricing.Tests/Integration/Admin/BusinessPricing/BulkImportStrictHeadersTests.cs` — header parse fails on `Net_Minor`/etc; passes on snake_case (research §R7).

### Implementation for User Story 3

- [ ] T086 [P] [US3] Implement `services/backend_api/Modules/Pricing/Admin/BusinessPricing/EditTierRow/...` per contract §4.1.
- [ ] T087 [P] [US3] Implement `services/backend_api/Modules/Pricing/Admin/BusinessPricing/EditCompanyOverride/...` per contract §4.2 — including the `400 business_pricing.below_cogs.warning` non-blocking warning + `acknowledge_below_cogs` flag.
- [ ] T088 [US3] Implement `services/backend_api/Modules/Pricing/Admin/BusinessPricing/BulkImportTierRows/Preview/...` per contract §4.3 — strict snake_case header parse, parsed-effect report, persists transient `bulk_import_previews` row with snapshot ETag, returns 15-min `preview_token`.
- [ ] T089 [US3] Implement `services/backend_api/Modules/Pricing/Admin/BusinessPricing/BulkImportTierRows/Commit/...` per contract §4.4 — token expiry, snapshot-change check, single-transaction commit, `business_pricing.bulk_imported` audit.
- [ ] T090 [P] [US3] Implement `services/backend_api/Modules/Pricing/Admin/BusinessPricing/DeactivateBusinessPricingRow/...` and `ReactivateBusinessPricingRow/...` per contract §4.5.
- [ ] T091 [P] [US3] Implement `services/backend_api/Modules/Pricing/Admin/BusinessPricing/ListBusinessPricingRows/...` per contract §4.6 with filters.
- [ ] T092 [P] [US3] Wire `DELETE /v1/admin/commercial/business-pricing/{id}` per contract §4.7 — conditionally forbidden when referenced by a `PriceExplanation`.

**Checkpoint**: User Story 3 fully implemented.

---

## Phase 6: User Story 4 — Operator links banner-driven campaign to a promotion (Priority: P2)

**Story goal**: deliver campaign authoring + banner-link picker.

**Independent test**: create a campaign with a `campaign_link` to an active promotion. Verify the lookup endpoint returns the campaign and the campaign's promotion is reachable via the engine through normal cart pricing.

### Tests for User Story 4

- [ ] T093 [P] [US4] Contract test `tests/Pricing.Tests/Contract/Campaigns/CreateCampaignContractTests.cs` — every Acceptance Scenario (4).
- [ ] T094 [P] [US4] Integration test `tests/Pricing.Tests/Integration/Admin/Campaigns/LinkTargetExpiredTests.cs` — picking an expired promotion returns `400 campaign.link.target_expired`.
- [ ] T095 [P] [US4] Integration test `tests/Pricing.Tests/Integration/Admin/Campaigns/CouponLinkRequiresDisplayInBannersTests.cs`.
- [ ] T096 [P] [US4] Integration test `tests/Pricing.Tests/Integration/Admin/Campaigns/CampaignLinkBrokenWatcherTests.cs` — when a linked promotion is deactivated, the campaign auto-marks `link_broken=true` within 60 s (cross-references the worker tested in Polish).

### Implementation for User Story 4

- [ ] T097 [P] [US4] Implement `services/backend_api/Modules/Pricing/Admin/Campaigns/CreateCampaign/...` per contract §5.1.
- [ ] T098 [P] [US4] Implement `services/backend_api/Modules/Pricing/Admin/Campaigns/UpdateCampaign/...`.
- [ ] T099 [P] [US4] Implement `services/backend_api/Modules/Pricing/Admin/Campaigns/ScheduleCampaign/...`.
- [ ] T100 [P] [US4] Implement `services/backend_api/Modules/Pricing/Admin/Campaigns/DeactivateCampaign/...`.
- [ ] T101 [P] [US4] Implement `services/backend_api/Modules/Pricing/Admin/Campaigns/ListCampaigns/...`.
- [ ] T102 [P] [US4] Implement `services/backend_api/Modules/Pricing/Admin/Campaigns/Lookups/SearchCampaignsForBanners/...` per contract §5.4 — consumed by spec 024 cms.

**Checkpoint**: User Story 4 fully implemented.

---

## Phase 7: User Story 5 — Approver gates a high-impact rule before activation (Priority: P2)

**Story goal**: deliver the high-impact approval gate end-to-end (`HighImpactGate` wired into all activation paths + approval queue + threshold administration).

**Independent test**: configure threshold; draft a rule that exceeds it; verify operator cannot self-activate; sign in as `commercial.approver` and approve; verify both actors appear in the audit trail.

### Tests for User Story 5

- [ ] T103 [P] [US5] Contract test `tests/Pricing.Tests/Contract/Approvals/RecordApprovalContractTests.cs` — every Acceptance Scenario (5).
- [ ] T104 [P] [US5] Integration test `tests/Pricing.Tests/Integration/Admin/Approvals/SelfApprovalForbiddenTests.cs` — author cannot approve own draft (RBAC layer 1 of R12).
- [ ] T105 [P] [US5] Integration test `tests/Pricing.Tests/Integration/Admin/Approvals/ConcurrentApprovalRaceTests.cs` — two approvers click Approve simultaneously; only one row, only one activation (R12 layer 2).
- [ ] T106 [P] [US5] Integration test `tests/Pricing.Tests/Integration/Admin/Approvals/AuthorBothActorsInAuditTests.cs` — audit row carries author + approver actor ids.
- [ ] T107 [P] [US5] Integration test `tests/Pricing.Tests/Integration/Admin/Thresholds/UpdateThresholdsRequiresSuperAdminTests.cs` — `commercial.operator`, `commercial.approver`, and `commercial.threshold_admin` results all asserted.
- [ ] T108 [P] [US5] Integration test `tests/Pricing.Tests/Integration/Admin/Thresholds/GateDisabledShortCircuitsAllRulesTests.cs` — when `gate_enabled=false`, no draft trips the gate.

### Implementation for User Story 5

- [ ] T109 [P] [US5] Implement `services/backend_api/Modules/Pricing/Admin/CommercialApprovals/ListPendingApprovals/...` per contract §8.1 — excludes drafts authored by the caller.
- [ ] T110 [US5] Implement `services/backend_api/Modules/Pricing/Admin/CommercialApprovals/RecordApproval/...` per contract §8.2 — includes the self-approval guard, the unique-constraint catch (R12 layer 2), and the in-Tx call to advance the draft via the existing schedule handler.
- [ ] T111 [P] [US5] Implement `services/backend_api/Modules/Pricing/Admin/CommercialApprovals/RejectApproval/...` per contract §8.3.
- [ ] T112 [P] [US5] Implement `services/backend_api/Modules/Pricing/Admin/CommercialThresholds/GetThresholds/...` per contract §9.1.
- [ ] T113 [P] [US5] Implement `services/backend_api/Modules/Pricing/Admin/CommercialThresholds/UpdateThresholds/...` per contract §9.2 — `super_admin`-only, audited, emits `CommercialThresholdChanged` domain event.
- [ ] T114 [US5] Wire `HighImpactGate.IsTriggered` into `ScheduleCoupon`, `SchedulePromotion`, `ReactivateCoupon`, `ReactivatePromotion` handlers — when triggered, return `403 coupon.activation.requires_approval` (or `promotion.activation.requires_approval`) and route the draft to the approval queue.

**Checkpoint**: User Story 5 fully implemented. Approval gate live.

---

## Phase 8: User Story 6 — `promotions-v1` seeder for staging and local development (Priority: P2)

**Story goal**: ship the dev seeder that populates every state for QA / training in Dev + Staging only.

**Independent test**: run `seed --dataset=promotions-v1 --mode=apply` against a fresh staging DB; verify ≥ 1 row in each state for both Coupons and Promotions, plus 3 tier rows + 2 company overrides + 3 campaigns; AR labels editorial-grade.

### Tests for User Story 6

- [ ] T115 [P] [US6] Integration test `tests/Pricing.Tests/Integration/Seeding/PromotionsV1SeederIdempotencyTests.cs` — runs the seeder twice; row count after run 2 = row count after run 1.
- [ ] T116 [P] [US6] Integration test `tests/Pricing.Tests/Integration/Seeding/PromotionsV1SeederStateCoverageTests.cs` — asserts ≥ 1 row in each of `draft`, `scheduled`, `active`, `deactivated`, `expired` for Coupons + Promotions; 3 tier rows + 2 company overrides + 3 campaigns.
- [ ] T117 [P] [US6] Integration test `tests/Pricing.Tests/Integration/Seeding/PromotionsV1SeederGuardTests.cs` — `RunInProduction=false` SeedGuard prevents the seeder from executing in Production environment.

### Implementation for User Story 6

- [ ] T118 [US6] Implement `services/backend_api/Modules/Pricing/Seeding/PromotionsV1DevSeeder.cs` — synthetic coupons (6 spanning every state × {percent_off, amount_off}), promotions (4 across same states), 3 tier rows, 2 company overrides, 3 campaigns; `SeedGuard.RunInProduction = false`.
- [ ] T119 [P] [US6] Author bilingual labels for every seeded customer-visible string in the seeder; flag every AR string in `services/backend_api/Modules/Pricing/Messages/AR_EDITORIAL_REVIEW.md` for editorial-grade review (Principle 4).
- [ ] T120 [P] [US6] Author the operator-facing reason-code ICU keys in `services/backend_api/Modules/Pricing/Messages/pricing.commercial.en.icu` and `pricing.commercial.ar.icu` — every code from contract §11; AR strings flagged.

**Checkpoint**: User Story 6 fully implemented.

---

## Phase 9: User Story 7 — Operator deactivates an active rule with required reason note (Priority: P3)

**Story goal**: validate the deactivation flow as a standalone user-visible behavior. The implementation slices already shipped in Phases 3-6 (`DeactivateCoupon`, `DeactivatePromotion`, `DeactivateCampaign`, `DeactivateBusinessPricingRow`); this phase adds end-to-end tests that exercise the deactivation flow as the user describes.

**Independent test**: deactivate an `active` rule with reason ≥ 10 chars; verify state, verify the next cart pricing returns `pricing.coupon.deactivated`, verify the audit row.

### Tests for User Story 7

- [ ] T121 [P] [US7] Integration test `tests/Pricing.Tests/Integration/Admin/Coupons/CouponDeactivationFlowTests.cs` — full happy path: active → deactivate with reason → audit row → engine returns `pricing.coupon.deactivated` on next call → reactivate path → audit row.
- [ ] T122 [P] [US7] Integration test `tests/Pricing.Tests/Integration/Admin/Coupons/CouponDeactivationReasonValidationTests.cs` — reason < 10 chars rejected with `commercial.deactivation.reason_required`.
- [ ] T123 [P] [US7] Integration test `tests/Pricing.Tests/Integration/Admin/Coupons/CouponReactivationOfExpiredRejectedTests.cs` — `commercial.reactivation.expired_terminal`.
- [ ] T124 [P] [US7] Integration test `tests/Pricing.Tests/Integration/Admin/InFlightGracePayloadTests.cs` — deactivation event payload `in_flight_grace_seconds` matches the threshold row for each market (research §R4 verification hook).

**Checkpoint**: User Story 7 fully validated.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: cross-cutting subsystems and DoD-level checks that integrate the user-story slices.

### Lookup endpoints (consumed by spec 015)

- [ ] T125 [P] Implement `services/backend_api/Modules/Pricing/Admin/Lookups/SearchSkus/...` per contract §10.1 — consumes spec 005 catalog search (Meilisearch via the existing repository); page-cap 200; p95 ≤ 300 ms.
- [ ] T126 [P] Implement `services/backend_api/Modules/Pricing/Admin/Lookups/SearchCompanies/...` per contract §10.2 — consumes spec 021 company search.
- [ ] T127 [P] Implement `services/backend_api/Modules/Pricing/Admin/Lookups/SearchSegments/...` per contract §10.3 — consumes spec 019 admin-customers segment search.
- [ ] T128 [P] Performance test `tests/Pricing.Tests/Integration/Performance/SkuPickerP95Tests.cs` — p95 ≤ 300 ms against a 50 000-SKU seeded catalog (SC-006).

### Cross-module subscribers

- [ ] T129 [P] Implement `services/backend_api/Modules/Pricing/Subscribers/CatalogSkuArchivedHandler.cs` — marks `applies_to_broken=true` on referencing Coupons / Promotions / BusinessPricingRows; idempotent (no-op if already marked).
- [ ] T130 [P] Implement `services/backend_api/Modules/Pricing/Subscribers/B2BCompanySuspendedHandler.cs` — marks `company_link_broken=true` on referencing BusinessPricingRows; idempotent.
- [ ] T131 [P] Implement `services/backend_api/Modules/Pricing/Subscribers/CampaignLinkBrokenWatcher.cs` — subscribes to `CouponDeactivated` / `CouponExpired` / `PromotionDeactivated` / `PromotionExpired`; sets `pricing.campaigns.link_broken=true` and `pricing.campaign_links.link_broken_at_utc=now()` for any campaign with that target id (FR-019).
- [ ] T132 [P] Integration test `tests/Pricing.Tests/Integration/Subscribers/CatalogSkuArchivedHandlerTests.cs` — emits a `CatalogSkuArchivedEvent` via `FakeCatalogSkuArchivedPublisher`; asserts `applies_to_broken=true` on every referencing rule (research §R3 verification hook).
- [ ] T133 [P] Integration test `tests/Pricing.Tests/Integration/Subscribers/B2BCompanySuspendedHandlerTests.cs`.
- [ ] T134 [P] Integration test `tests/Pricing.Tests/Integration/Subscribers/CampaignLinkBrokenWatcherTests.cs`.

### Workers

- [ ] T135 Implement `services/backend_api/Modules/Pricing/Workers/LifecycleTimerWorker.cs` per quickstart §10 + research §R1 — 60 s tick, advisory-lock guarded, idempotent SQL `UPDATE`s for both lifecycle transitions on Coupons / Promotions / Campaigns; uses `TimeProvider`.
- [ ] T136 Implement `services/backend_api/Modules/Pricing/Workers/BrokenReferenceAutoDeactivationWorker.cs` per research §R13 — daily at 02:00 UTC, advisory-lock guarded, eligibility predicate "every reference broken AND broken ≥ 7 days"; reuses the `DeactivateCoupon` / `DeactivatePromotion` handlers with `actor_id='system'`.
- [ ] T137 [P] Integration test `tests/Pricing.Tests/Integration/Workers/LifecycleTimerWorkerDriftTests.cs` — `FakeTimeProvider`, schedules 100 coupons with `valid_from = now + 30s`, advances 90 s, asserts every row is `active` and audit rows exist; SC-005 ≤ 60 s drift.
- [ ] T138 [P] Integration test `tests/Pricing.Tests/Integration/Workers/BrokenReferenceAutoDeactivationWorkerTests.cs` — happy path (auto-deactivates after 7 days when all refs broken); negative path (skips when any ref still live).

### Domain events + spec 025 contract

- [ ] T139 Wire publication of all 10 domain events from `services/backend_api/Modules/Shared/CommercialDomainEvents.cs` at the lifecycle-transition + threshold-change call sites; ensure deactivation events carry `in_flight_grace_seconds` from the threshold row (FR-003a).
- [ ] T140 [P] Integration test `tests/Pricing.Tests/Integration/Events/CommercialDomainEventsPublishedTests.cs` — collects published events via `FakeMediator` for a representative end-to-end flow; asserts each event fires exactly once.

### `ICheckoutGraceWindowProvider` implementation

- [ ] T141 [P] Implement `services/backend_api/Modules/Pricing/Internal/CheckoutGraceWindowProvider.cs` realising `Modules/Shared/ICheckoutGraceWindowProvider`; reads from `pricing.commercial_thresholds` with the in-process 30-second cache from data-model §10.

### OpenAPI artifact

- [ ] T142 Regenerate `services/backend_api/openapi.pricing.commercial.json` via `dotnet swagger tofile`; commit; PR diff reviewed against contract §1-§9.

### Audit coverage

- [ ] T143 [P] Implement `scripts/audit-coverage/pricing-commercial.sh` — runs the spec-015 audit-coverage script over a 100-action operator session; asserts 100 % of actions produce a `commercial_audit_events` row + an `audit_log_entries` row (SC-003).
- [ ] T144 [P] Integration test `tests/Pricing.Tests/Integration/Audit/CommercialAuditCoverageTests.cs` — programmatically exercises every authoring slice + lifecycle transition + approval; asserts the 16 audit-event kinds from data-model §5 are reachable.

### AR editorial sweep

- [ ] T145 AR editorial review: every customer-visible string seeded by `PromotionsV1DevSeeder` and every reason-code key in `pricing.commercial.ar.icu` MUST be reviewed by an editorial-grade reviewer (Principle 4 / SC-007). Update `AR_EDITORIAL_REVIEW.md` with the sign-off list. Blocks launch, not the PR.

### Rate-limit + concurrency hardening

- [ ] T146 [P] Integration test `tests/Pricing.Tests/Integration/RateLimit/CommercialWriteRateLimitTests.cs` — exceeding 30 writes / min / actor returns `429 commercial.rate_limit_exceeded` per FR-035.
- [ ] T147 [P] Integration test `tests/Pricing.Tests/Integration/Concurrency/RowVersionConflictTests.cs` — two operators editing the same coupon: second save returns `409 commercial.row.version_conflict` with the current row body embedded.

### Integrity-scan job (SC-004)

- [ ] T148 [P] Implement `services/backend_api/Modules/Pricing/Workers/CommercialIntegrityScanWorker.cs` — daily background job that scans `pricing.coupons`, `pricing.promotions`, and `pricing.campaigns` for any row in state `active` with `valid_to ≤ valid_from` OR missing `label.ar` / `label.en` (or `name.ar` / `name.en` for Campaigns); writes findings to a structured log channel `commercial.integrity` and emits a metric `pricing_commercial_integrity_violations_total{kind, market}` for the alert pipeline. The worker writes NO rows; it is observation-only.
- [ ] T149 [P] Integration test `tests/Pricing.Tests/Integration/Workers/CommercialIntegrityScanWorkerTests.cs` — seeds an intentionally-malformed row via raw SQL (bypassing validators), runs the scan, asserts the violation is logged + the metric increments; verifies a clean DB produces zero violations (SC-004).

### Uniqueness-check perf test (FR-007)

- [ ] T150 [P] Performance test `tests/Pricing.Tests/Integration/Performance/CouponUniquenessP95Tests.cs` — issues 100 form-blur uniqueness lookups against a 10 000-coupon seeded DB; asserts p95 ≤ 200 ms (FR-007).

### DoD checklist + fingerprint

- [ ] T151 Compute constitution + ADR fingerprint via `scripts/compute-fingerprint.sh` and paste into the PR body; verify against locked v1.0.0 baseline.
- [ ] T152 Walk through the DoD checklist from `docs/dod.md` (DoD version 1.0); attach completion ticks to the PR description.
- [ ] T153 Manual smoke through Postman / curl for one slice from each top-level surface (coupon, promotion, business-pricing, campaign, preview, approval, threshold) per quickstart §13.

---

## Dependencies & Execution Order

### Phase Dependencies

```text
Phase 1 (Setup)
    ↓
Phase 2 (Foundational) ── blocks all user stories
    ↓
    ┌────────── parallel start ──────────┐
    ↓                                       ↓
Phase 3 (US1)   Phase 4 (US2)   Phase 5 (US3)    ← P1 stories run concurrently after Phase 2
    ↓                ↓                   ↓
Phase 6 (US4)   Phase 7 (US5)   Phase 8 (US6)    ← P2 stories; P5 (approval gate) requires Phase 5's HighImpactGate wiring point T114 to land in any of E/F to be testable end-to-end
    ↓
Phase 9 (US7) ── lightweight; depends on Phases 3-6 deactivation slices
    ↓
Phase 10 (Polish) ── cross-cutting; some tasks (workers, lookups, openapi, audit-coverage, fingerprint, DoD) require all stories complete
```

### Within each user story

- Tests run in parallel with each other (different files).
- Implementation tasks marked `[P]` run in parallel with each other (different files).
- The Preview-tool task (T065 in US1) is intentionally listed without `[P]` because it touches `IPriceCalculator.Calculate(ctx)` consumption that other US1 tasks may also reference indirectly via overlay; serialise to avoid merge churn.

### Parallel execution examples

- **Phase 2 entities**: T017–T022 (six new entities) all run in parallel.
- **Phase 2 EF configurations**: T024–T030 in parallel.
- **Phase 2 cross-module declarations**: T034–T038 in parallel.
- **Phase 3 tests**: T050–T055 in parallel.
- **Phase 3 implementation**: T056–T064 + T066–T068 in parallel; T065 (Preview tool) sequential.
- **Phase 4 implementation**: T073–T080 in parallel.
- **Phase 5 tests**: T081–T085 in parallel.
- **Phase 6 implementation**: T097–T102 in parallel.
- **Phase 7 implementation**: T109, T111, T112, T113 in parallel; T110 + T114 sequential (gate-wiring touches the same shared schedule handlers).
- **Phase 10 cross-module subscribers**: T129–T134 in parallel.

---

## Implementation Strategy

### MVP scope (User Story 1 only)

After Phases 1, 2, and 3 land (~68 tasks), the following slice is demoable:

- A `commercial.operator` can sign in, author a coupon, preview it against a sample profile, schedule it, and see the audit trail.
- The Preview tool calls `IPriceCalculator.Calculate(ctx)` in Preview mode and renders a layer-by-layer explanation with a delta ribbon.
- Hard-delete on the coupon route is forbidden.
- The schema is forward-compatible with US2 / US3 / US4 (lifecycle columns and `vendor_id` are present).

### Incremental delivery

| Increment | Tasks | Demo gain |
|---|---|---|
| MVP (US1) | T001-T068 | Coupons + Preview live |
| +US2 | T069-T080 | Promotions live |
| +US3 | T081-T092 | Business-pricing live |
| +US4 | T093-T102 | Campaigns + banner-link picker live |
| +US5 | T103-T114 | Approval gate live; thresholds tunable |
| +US6 | T115-T120 | Dev seeder ready for QA / training |
| +US7 (validation) | T121-T124 | Deactivation flow validated end-to-end |
| Polish | T125-T153 | Lookups, subscribers, workers, integrity-scan job (SC-004), uniqueness-check perf (FR-007), OpenAPI, audit, DoD |

Each increment is mergable on its own and produces a usable system. Spec 015 (admin UI) can begin consuming the contracts as soon as Phase 2 lands and ship UI per increment.

---

## Risk callouts

- **Engine immutability** (Plan §Constraints, Constitution P10): no task in this list modifies code under `Modules/Pricing/Internal/Calculate/`. Reviewers must reject any diff that does — that's a 007-a amendment, not 007-b.
- **`ManyServiceProvidersCreatedWarning` regression** (project-memory R14): T003 adds a CI grep guard. If the suppression is dropped during refactoring, the guard fails the PR.
- **AR editorial debt** (T145, R17): blocks launch but not the merge. Track in `AR_EDITORIAL_REVIEW.md`.
- **Cross-module subscriber dependencies** (T129–T131): require spec 005 / 021 to have published the corresponding events on `main`. If those PRs are still in flight, the subscribers ship "wired but quiet" — fakes in tests prove they work.

---

**Total tasks**: 153 (T001–T153).

**Per user story**:
- Setup: 5 (T001-T005)
- Foundational: 44 (T006-T049)
- US1: 19 (T050-T068)
- US2: 12 (T069-T080)
- US3: 12 (T081-T092)
- US4: 10 (T093-T102)
- US5: 12 (T103-T114)
- US6: 6 (T115-T120)
- US7: 4 (T121-T124)
- Polish: 29 (T125-T153) — includes T148/T149 integrity-scan job (SC-004) and T150 uniqueness-check perf test (FR-007), added by `/speckit-analyze` remediation

**Format validation**: every task above starts with `- [ ]`, has a sequential `T###` ID, carries `[P]` when parallelizable, carries `[USn]` only inside user-story phases, and includes an exact file path or directory.
