---
description: "Dependency-ordered tasks for spec 008 — inventory"
---

# Tasks: Inventory v1

## Phase 1: Setup
- [X] T001 Module tree `services/backend_api/Modules/Inventory/{Primitives,Primitives/Fefo,Reservations/{Create,Release,Convert},Movements/{Adjust,Receipt,Transfer,Writeoff,Return},Customer/GetAvailability,Admin/{Stocks,Batches,Movements,Alerts},Internal,Workers,Entities,Persistence/{Configurations,Migrations},Messages}` + tests `tests/Inventory.Tests/{Unit,Integration,Contract}`
- [X] T002 Register `AddInventoryModule`, wire `Program.cs`
- [X] T003 [P] Add background-service + FluentValidation refs (shared)

## Phase 2: Foundational
- [X] T004 [P] `AtsCalculator` in `Primitives/AtsCalculator.cs`
- [X] T005 [P] `FefoPicker` in `Primitives/Fefo/FefoPicker.cs`
- [X] T006 [P] `BucketMapper` in `Primitives/BucketMapper.cs`
- [X] T007 6 entities + EF configs + migration `Inventory_Initial`
- [X] T008 `InventoryBootstrapSeeder` creates `eg-main` + `ksa-main` warehouses
- [X] T009 Unit tests for calculator/picker/mapper `tests/Inventory.Tests/Unit/*Tests.cs`
- [X] T010 `InventoryTestFactory` + Testcontainers Postgres

## Phase 3: US1/US2 — Reservation + conversion (P1) 🎯 MVP
- [X] T011 [P] [US1] Contract test `CreateReservation_DecrementsAts` at `tests/Inventory.Tests/Contract/Internal/ReservationsContractTests.cs`
- [X] T012 [P] [US1] Integration test `100Concurrent_Exactly5Succeed` (SC-002) at `tests/Inventory.Tests/Integration/ConcurrencyTests.cs`
- [X] T013 [P] [US1] Integration test `TtlExpiry_WorkerReleasesWithin1Min` (SC-003) at `tests/Inventory.Tests/Integration/TtlReleaseTests.cs`
- [X] T014 [P] [US2] Contract test `ConvertReservation_WritesSaleMovement` in same file
- [X] T015 [US1] Implement `Internal/Reservations/Create` with `SELECT FOR UPDATE`
- [X] T016 [US1] Implement `Internal/Reservations/Release` (manual + worker)
- [X] T017 [US2] Implement `Internal/Reservations/Convert` (posts `kind=sale` movement + batch_id)
- [X] T018 [US1] `ReservationReleaseWorker` in `Workers/ReservationReleaseWorker.cs`

## Phase 4: US3 — Batch/lot/expiry (P1)
- [X] T019 [P] [US3] Contract test `ReceiveBatch_WritesMovement` at `tests/Inventory.Tests/Contract/Admin/BatchesContractTests.cs`
- [X] T020 [P] [US3] Integration test `Fefo_PicksNearestExpiryFirst` (SC-005) at `tests/Inventory.Tests/Integration/FefoTests.cs`
- [X] T021 [P] [US3] Integration test `ExpiryWorker_WritesOffExpiredBatches` (SC-007) at `tests/Inventory.Tests/Integration/ExpiryWriteoffTests.cs`
- [X] T022 [US3] Implement `Admin/Batches/{Create,Patch,List,Get}` slices
- [X] T023 [US3] `ExpiryWriteoffWorker` in `Workers/ExpiryWriteoffWorker.cs`

## Phase 5: US4 — Reorder alerts (P2)
- [X] T024 [P] [US4] Integration test `ReorderCrossed_EmitsOnce` (SC-006) at `tests/Inventory.Tests/Integration/ReorderDebounceTests.cs`
- [X] T025 [US4] Emit `inventory.reorder_threshold_crossed` with 1 h debounce table

## Phase 6: US5 — Admin adjustments (P2)
- [X] T026 [P] [US5] Contract test `Adjust_NegativeThatWouldGoSubZero_Rejected` at `tests/Inventory.Tests/Contract/Admin/AdjustContractTests.cs`
- [X] T027 [P] [US5] Contract test `Transfer_WritesPairedMovements` in `tests/Inventory.Tests/Contract/Admin/TransferContractTests.cs`
- [X] T028 [US5] Implement `Admin/Movements/{Adjust,Transfer,Writeoff}` with audit

## Phase 7: US6 — Availability event + search sync (P1)
- [X] T029 [P] [US6] Integration test `BucketChange_EmitsAvailabilityEvent_Under10s` (SC-008)
- [X] T030 [US6] Implement `AvailabilityEventEmitter` hook inside stock-write transactions

## Phase 8: Customer surface
- [X] T031 [P] Contract test `GetAvailability_BatchBuckets_NoRawQty` at `tests/Inventory.Tests/Contract/Customer/GetAvailabilityContractTests.cs`
- [X] T032 Implement `Customer/GetAvailability/*.cs`

## Phase 9: Return integration (spec 013 seam)
- [X] T033 [P] Contract test `Return_RestocksBatch` at `tests/Inventory.Tests/Contract/Internal/ReturnsContractTests.cs`
- [X] T034 Implement `Internal/Movements/Return` endpoint

## Phase 10: Polish
- [X] T035 [P] Observability metrics (reservation conflicts counter; ATS gauge optional)
- [X] T036 [P] AR editorial pass on `inventory.ar.icu`
- [X] T037 [P] OpenAPI regen + contract diff green
- [X] T038 Fingerprint + DoD

**Totals**: 38 tasks across 10 phases. MVP = Phases 1 + 2 + 3 + 4 + 7.
