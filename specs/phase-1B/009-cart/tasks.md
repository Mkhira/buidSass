---
description: "Dependency-ordered tasks for spec 009 — cart"
---

# Tasks: Cart v1

## Phase 1: Setup
- [ ] T001 Module tree `services/backend_api/Modules/Cart/{Primitives,Customer/{GetCart,AddLine,UpdateLine,RemoveLine,Merge,ApplyCoupon,RemoveCoupon,MoveToSaved,RestoreFromSaved,SetB2BMetadata,SwitchMarket,Restore},Admin/{GetCart,ListAbandoned},Workers,Entities,Persistence/{Configurations,Migrations},Messages}` + tests
- [ ] T002 Register `AddCartModule`, wire `Program.cs`
- [ ] T003 [P] Shared FluentValidation dep

## Phase 2: Foundational
- [ ] T004 [P] `CartTokenProvider` in `Primitives/CartTokenProvider.cs`
- [ ] T005 [P] `CartMerger` in `Primitives/CartMerger.cs`
- [ ] T006 [P] `EligibilityEvaluator` in `Primitives/EligibilityEvaluator.cs`
- [ ] T007 5 entities + configs + migration `Cart_Initial`
- [ ] T008 Unit tests for token provider + merger (100 scenarios) + eligibility evaluator
- [ ] T009 `CartTestFactory` + Testcontainers infra

## Phase 3: US1/US2 — Anonymous + merge (P1) 🎯 MVP
- [ ] T010 [P] [US1] Contract test `AddLine_AnonymousCart_CreatesAndSetsCookie` at `tests/Cart.Tests/Contract/Customer/AddLineContractTests.cs`
- [ ] T011 [P] [US1] Integration test `AddLine_ReservesInventory_AndTtl` at `tests/Cart.Tests/Integration/ReservationLifecycleTests.cs`
- [ ] T012 [P] [US2] Contract test `Merge_SumsQty_CapsAtMax` at `tests/Cart.Tests/Contract/Customer/MergeContractTests.cs`
- [ ] T013 [P] [US2] Integration test `Merge_100Scenarios_NoDrift` (SC-003)
- [ ] T014 [US1] Implement `GetCart` + `AddLine` + `UpdateLine` + `RemoveLine`
- [ ] T015 [US2] Implement `Merge` + hook into spec 004 login
- [ ] T016 [US1] Populate `Messages/cart.{ar,en}.icu`

## Phase 4: US3 — Restricted interaction (P1)
- [ ] T017 [P] [US3] Contract test `AddRestricted_AddsWithFlag_EligibilityBlocks` at `tests/Cart.Tests/Contract/Customer/RestrictionContractTests.cs`
- [ ] T018 [US3] Wire `EligibilityEvaluator` into every cart read

## Phase 5: US4 — Qty updates (P1)
- [ ] T019 [P] [US4] Contract test `UpdateQty_ExtendsReservation` in `tests/Cart.Tests/Contract/Customer/UpdateLineContractTests.cs`
- [ ] T020 [P] [US4] Contract test `UpdateQtyZero_RemovesLine_ReleasesReservation` in same file

## Phase 6: US5 — B2B fields (P2)
- [ ] T021 [P] [US5] Contract test `SetB2B_NonB2BAccount_Returns403` at `tests/Cart.Tests/Contract/Customer/B2BMetadataContractTests.cs`
- [ ] T022 [US5] Implement `SetB2BMetadata`

## Phase 7: US6 — Abandonment (P2)
- [ ] T023 [P] [US6] Integration test `Abandonment_EmitsOnce_24hDedup` (SC-004) at `tests/Cart.Tests/Integration/AbandonmentTests.cs`
- [ ] T024 [US6] `AbandonedCartWorker` implementation

## Phase 8: US7 — Save for later (P2)
- [ ] T025 [P] [US7] Contract test `MoveToSaved_ReleasesReservation` at `tests/Cart.Tests/Contract/Customer/SaveForLaterContractTests.cs`
- [ ] T026 [US7] Implement saved-items endpoints

## Phase 9: Market switch + archive
- [ ] T027 [P] Contract test `SwitchMarket_ArchivesOldCart` at `tests/Cart.Tests/Contract/Customer/SwitchMarketContractTests.cs`
- [ ] T028 [P] Integration test `Restore_Within7Days_Succeeds` + `Restore_After7Days_Fails`
- [ ] T029 Implement SwitchMarket + Restore slices + `ArchivedCartReaperWorker`
- [ ] T030 `GuestCartCleanupWorker` (30-day purge, SC-005)

## Phase 10: Coupon on cart
- [ ] T031 [P] Contract test `ApplyCoupon_Invalid_ReturnsReasonCode` at `tests/Cart.Tests/Contract/Customer/CouponContractTests.cs`
- [ ] T032 Implement `ApplyCoupon` + `RemoveCoupon`

## Phase 11: Admin surface
- [ ] T033 [P] Contract test `AdminGetCart_WritesAuditRow` at `tests/Cart.Tests/Contract/Admin/GetCartContractTests.cs`
- [ ] T034 Implement `Admin/GetCart` + `Admin/ListAbandoned`

## Phase 12: Polish
- [ ] T035 [P] AR editorial pass on `cart.ar.icu`
- [ ] T036 [P] OpenAPI regen + contract diff green
- [ ] T037 Fingerprint + DoD

**Totals**: 37 tasks across 12 phases. MVP = Phases 1 + 2 + 3 + 4 + 5.
