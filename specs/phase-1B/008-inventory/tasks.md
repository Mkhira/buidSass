# Tasks: Inventory (008)

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data**: [data-model.md](./data-model.md) | **Contracts**: [contracts/](./contracts/)

Task count: **86** across **12 phases**. Tests included (TDD-leaning). MVP = **T001–T055** (US1 + US2 + US3 delivering availability, basket validation, and reservation lifecycle).

---

## Phase 1 — Setup

- [ ] T001 Create `services/backend_api/Features/Inventory/` with subfolders Availability, Reservations, Movements, Batches, Warehouses, Thresholds, Snapshots, Workers, Events, Persistence, Observability, Shared
- [ ] T002 Add `InventoryDbContext` + DI registration in `services/backend_api/Features/Inventory/Persistence/InventoryDbContext.cs` and wire in Program.cs
- [ ] T003 [P] Add NodaTime + register `IClock` in DI (UTC) in `services/backend_api/Program.cs`
- [ ] T004 [P] Create test projects: `Tests/Inventory.Unit`, `Tests/Inventory.Properties`, `Tests/Inventory.Integration`, `Tests/Inventory.Contract` with FsCheck + Testcontainers + FluentAssertions refs
- [ ] T005 [P] Copy `specs/phase-1B/008-inventory/contracts/inventory.openapi.yaml` into backend Swagger source at `services/backend_api/OpenApi/inventory.openapi.yaml` and register in dev Swashbuckle pipeline

## Phase 2 — Foundational (blocking)

- [ ] T006 Write migration `V008_001__create_inventory_schema.sql` at `services/backend_api/Features/Inventory/Persistence/Migrations/` covering all 8 tables + constraints + indexes per data-model.md
- [ ] T007 Write migration `V008_002__seed_reason_codes_and_default_warehouses.sql` seeding 7 reason codes + `whs_ksa_01`, `whs_eg_01`
- [ ] T008 Write migration `V008_003__snapshot_sequence.sql` creating per-(variant, warehouse) sequence for `stock_movements.sequence_number`
- [ ] T009 EF entities: `Warehouse`, `VariantStockSnapshot`, `StockMovement`, `Reservation`, `ReservationLine`, `Batch`, `AdjustmentReasonCode`, `LowStockNotification`, `TransferHeader` in `Features/Inventory/Persistence/Entities/`
- [ ] T010 EF configurations with row-version (`xmin`) and `ats_version` CAS config in `Persistence/Configurations/`
- [ ] T011 [P] DTOs from contract in `Features/Inventory/Shared/Dtos/` — AvailabilityItem, BasketValidate{Request,Response}, Reservation*, CommitResponse, StockMovementDTO, AdjustmentRequest, TransferRequest, ReturnRestockRequest, BatchDTO, BatchInput, Warehouse, ErrorEnvelope
- [ ] T012 [P] Error code catalog `Features/Inventory/Shared/InventoryErrorCodes.cs` with bilingual ar/en messages (insufficient_stock, concurrent_write_conflict, invalid_state, already_extended, cross_market_transfer_unsupported, invalid_reason_code, evidence_required, quantity_cap_exceeded, restricted_not_eligible)
- [ ] T013 [P] MediatR `INotification` classes for all 9 events in `Features/Inventory/Events/` per contracts/events.md
- [ ] T014 Snapshot CAS helper `Features/Inventory/Snapshots/SnapshotCasWriter.cs` with single-retry semantics (R2)
- [ ] T015 Audit emitter `Features/Inventory/Observability/InventoryAuditWriter.cs` as MediatR handler writing to `audit.events`
- [ ] T016 Structured logger enricher `Features/Inventory/Observability/InventoryLogEnricher.cs` adding R13 fields
- [ ] T017 OpenTelemetry metrics registration per events.md §3.2 in `Features/Inventory/Observability/InventoryMetrics.cs`

## Phase 3 — User Story 1: Availability visibility (P1)

**Goal**: Storefront and admin can query per-(variant, warehouse) availability; state classified in_stock/low_stock/out_of_stock/preorder.

**Independent test**: GET /inventory/availability returns correct states; admin view includes onHand + threshold; public caller gets only ats/state.

- [ ] T018 [P] [US1] Availability query handler `Features/Inventory/Availability/GetAvailabilityHandler.cs` reading `variant_stock_snapshot` joined with catalog variant for threshold + preorder flag
- [ ] T019 [P] [US1] Endpoint `GET /inventory/availability` in `Features/Inventory/Availability/AvailabilityEndpoint.cs` (minimal API), max 50 variantIds, returns AvailabilityItem[]
- [ ] T020 [P] [US1] Admin stock endpoint `GET /admin/inventory/stock` with filters + cursor pagination in `Features/Inventory/Availability/AdminStockEndpoint.cs`
- [ ] T021 [P] [US1] Admin dashboard `GET /admin/inventory/dashboards/low-stock` in `Features/Inventory/Availability/LowStockDashboardEndpoint.cs`
- [ ] T022 [P] [US1] FluentValidation validators for query inputs in `Features/Inventory/Availability/Validators/`
- [ ] T023 [US1] Unit tests — state classification edge cases in `Tests/Inventory.Unit/Availability/AvailabilityStateTests.cs`
- [ ] T024 [US1] Integration test — public vs admin field visibility (`onHand` omitted for public) in `Tests/Inventory.Integration/Availability/VisibilityTests.cs`
- [ ] T025 [US1] k6 script `perf/inventory_availability.js` enforcing SC-002 p95 ≤ 120 ms

## Phase 4 — User Story 2: Basket validation (P1)

**Goal**: Checkout precondition `POST /inventory/basket/validate` returns per-line feasibility without mutating state.

**Independent test**: Given stock=3, requesting 5 returns `ok=false, available=3, state=low_stock`.

- [ ] T026 [US2] Handler `Features/Inventory/Availability/BasketValidateHandler.cs` batching snapshot reads
- [ ] T027 [US2] Endpoint `POST /inventory/basket/validate` in `Features/Inventory/Availability/BasketValidateEndpoint.cs`
- [ ] T028 [US2] Unit tests — partial success, preorder line passes when within floor, restricted variant flagged in `Tests/Inventory.Unit/Availability/BasketValidateTests.cs`
- [ ] T029 [US2] Contract test — OpenAPI response schema round-trip in `Tests/Inventory.Contract/BasketValidateContractTests.cs`

## Phase 5 — User Story 3: Reservation lifecycle (P1)

**Goal**: soft_held → committed → fulfilled | released | expired with CAS-safe decrement and TTL scanner.

**Independent test**: 100 concurrent reservations for 10 stock yield exactly 10 success + 90 `insufficient_stock`; no over-allocation.

- [ ] T030 [US3] State machine `Features/Inventory/Reservations/ReservationStateMachine.cs` with transitions per spec §6.2
- [ ] T031 [US3] `CreateReservationHandler` in `Features/Inventory/Reservations/CreateReservationHandler.cs` — CAS writes snapshot(reserved+=q) + inserts reservation rows in one transaction
- [ ] T032 [P] [US3] `ExtendReservationHandler` in `Features/Inventory/Reservations/ExtendReservationHandler.cs` enforcing `extended_count ≤ 1`
- [ ] T033 [US3] `CommitReservationHandler` in `Features/Inventory/Reservations/CommitReservationHandler.cs` — runs FEFO picker, writes reservation_out movements, snapshot on_hand-=q reserved-=q, batches on_hand-=q
- [ ] T034 [US3] `FulfilReservationHandler` in `Features/Inventory/Reservations/FulfilReservationHandler.cs` (post-dispatch terminal)
- [ ] T035 [US3] `ReleaseReservationHandler` in `Features/Inventory/Reservations/ReleaseReservationHandler.cs` — snapshot reserved-=q; publishes `reservation_released`
- [ ] T036 [US3] FEFO picker `Features/Inventory/Batches/FefoBatchPicker.cs` per R6 with batch_shortfall fallback + warning
- [ ] T037 [US3] `ReservationExpiryScanner : BackgroundService` at 30 s cadence, batches of 50, SKIP LOCKED (R4) in `Features/Inventory/Workers/ReservationExpiryScanner.cs`
- [ ] T038 [P] [US3] Endpoints: POST /inventory/reservations, GET/…/{id}, POST/…/extend, /commit, /fulfil, /release in `Features/Inventory/Reservations/ReservationEndpoints.cs`
- [ ] T039 [P] [US3] Preorder guard in CAS writer: rejects when `on_hand - reserved - delta < -preorder_floor`; tests in `Tests/Inventory.Unit/Reservations/PreorderFloorTests.cs`
- [ ] T040 [US3] FsCheck property — no over-allocation under 100k concurrent attempts (SC-001) in `Tests/Inventory.Properties/NoOverallocationProperty.cs`
- [ ] T041 [US3] FsCheck property — ledger replay determinism (SC-004, SC-010) in `Tests/Inventory.Properties/LedgerReplayDeterminismProperty.cs`
- [ ] T042 [US3] Integration test — 100 concurrent reservations under Testcontainers Postgres in `Tests/Inventory.Integration/Reservations/ConcurrencyTests.cs`
- [ ] T043 [US3] Integration test — TTL expiry scanner p99 lag ≤ 60 s under load (SC-005) in `Tests/Inventory.Integration/Reservations/ExpiryLagTests.cs`
- [ ] T044 [US3] Integration test — commit picks FEFO batches; fallback flags warning in `Tests/Inventory.Integration/Reservations/FefoCommitTests.cs`
- [ ] T045 [US3] k6 `perf/inventory_reserve.js` enforcing SC-003 p95 ≤ 250 ms @ 20 lines
- [ ] T046 [US3] Contract tests for all reservation endpoints in `Tests/Inventory.Contract/ReservationContractTests.cs`

## Phase 6 — User Story 4: Batches, lots, expiry (P1)

**Goal**: Admin can receive batches; sweeper expires them at warehouse-local midnight; FEFO honoured end-to-end.

- [ ] T047 [P] [US4] `CreateBatchHandler` + endpoint `POST /admin/inventory/batches` writing `initial_receipt` movement in `Features/Inventory/Batches/CreateBatchHandler.cs`
- [ ] T048 [P] [US4] `UpdateBatchHandler` + endpoint `PATCH /admin/inventory/batches/{id}` in `Features/Inventory/Batches/UpdateBatchHandler.cs`
- [ ] T049 [US4] `BatchExpirySweeper : BackgroundService` daily 03:00 UTC with NodaTime warehouse-tz evaluation (R3) in `Features/Inventory/Workers/BatchExpirySweeper.cs`
- [ ] T050 [P] [US4] Dashboard endpoint `GET /admin/inventory/dashboards/expiring` in `Features/Inventory/Batches/ExpiringDashboardEndpoint.cs`
- [ ] T051 [US4] Integration test — expiry sweep fires at local midnight for KSA vs EG in `Tests/Inventory.Integration/Batches/ExpirySweepTimezoneTests.cs`
- [ ] T052 [US4] Unit test — FEFO ordering (earliest expiry first, NULLs last, tiebreak by received_at) in `Tests/Inventory.Unit/Batches/FefoOrderingTests.cs`

## Phase 7 — User Story 5: Admin adjustments (P1)

**Goal**: Admin posts signed adjustments with seeded reason codes + evidence when required; full audit.

- [ ] T053 [US5] `PostAdjustmentHandler` + endpoint `POST /admin/inventory/adjustments` with quantity cap 100k and reason-code validation in `Features/Inventory/Movements/PostAdjustmentHandler.cs`
- [ ] T054 [US5] Policy `inventory.adjust` wired to admin role in `Features/Inventory/Shared/InventoryAuthorizationPolicies.cs`
- [ ] T055 [US5] Integration test — adjustment writes movement + snapshot + audit row + emits `adjustment_posted` + `snapshot_changed` in `Tests/Inventory.Integration/Movements/AdjustmentTests.cs`

## Phase 8 — User Story 6: Low-stock notifications (P2)

**Goal**: Debounced low-stock event per (variant, warehouse, day_bucket).

- [ ] T056 [US6] Low-stock detector in snapshot CAS writer: detects threshold crossings in `Features/Inventory/Snapshots/LowStockCrossingDetector.cs`
- [ ] T057 [US6] Dedup insert into `low_stock_notifications` and publish `low_stock` only on fresh row in `Features/Inventory/Events/LowStockEmitter.cs`
- [ ] T058 [US6] `stock_recovered` emission when ATS climbs back above threshold in `Features/Inventory/Events/StockRecoveredEmitter.cs`
- [ ] T059 [US6] Integration test — two same-day crossings emit exactly one `low_stock` event (SC-006) in `Tests/Inventory.Integration/Events/LowStockDedupTests.cs`

## Phase 9 — User Story 7: Ledger export (P2)

**Goal**: Admin exports filtered ledger as JSON or streaming CSV.

- [ ] T060 [US7] Query handler with cursor pagination + filters in `Features/Inventory/Movements/QueryLedgerHandler.cs`
- [ ] T061 [US7] CSV streaming endpoint `GET /admin/inventory/ledger?format=csv` with chunked transfer + 1 M row cap (R12) in `Features/Inventory/Movements/LedgerEndpoint.cs`
- [ ] T062 [US7] Integration test — CSV stream includes headers, correct row count, stops at cap in `Tests/Inventory.Integration/Movements/LedgerCsvExportTests.cs`

## Phase 10 — User Story 8: Transfers + returns (P2)

**Goal**: Intra-market transfers post paired movements; returns restock writes `return_in`.

- [ ] T063 [US8] `PostTransferHandler` + endpoint `POST /admin/inventory/transfers` with same-market check + paired transfer_out/transfer_in + `transfer_headers` row in `Features/Inventory/Movements/PostTransferHandler.cs`
- [ ] T064 [US8] Return-restock handler + endpoint `POST /inventory/returns/restock` in `Features/Inventory/Movements/ReturnRestockHandler.cs`
- [ ] T065 [US8] Cross-market transfer returns `inventory.cross_market_transfer_unsupported` in validator
- [ ] T066 [US8] Integration test — transfer atomicity (either both movements write or neither) in `Tests/Inventory.Integration/Movements/TransferAtomicityTests.cs`
- [ ] T067 [US8] Integration test — cross-market transfer rejected in `Tests/Inventory.Integration/Movements/CrossMarketRejectionTests.cs`

## Phase 11 — Admin force-release, warehouses, thresholds

- [ ] T068 [P] Force-release endpoint + `inventory.reserve.manage` policy in `Features/Inventory/Reservations/ForceReleaseEndpoint.cs`
- [ ] T069 [P] Warehouses list endpoint `GET /admin/inventory/warehouses` in `Features/Inventory/Warehouses/WarehousesEndpoint.cs`
- [ ] T070 [P] Thresholds PUT endpoint proxying to catalog variant in `Features/Inventory/Thresholds/ThresholdEndpoint.cs`
- [ ] T071 [P] Admin reservations list endpoint `GET /admin/inventory/reservations` in `Features/Inventory/Reservations/AdminReservationListEndpoint.cs`
- [ ] T072 Integration test — force-release requires `inventory.reserve.manage`; audit row written in `Tests/Inventory.Integration/Reservations/ForceReleaseAuthzTests.cs`

## Phase 12 — Polish & cross-cutting

- [ ] T073 [P] Bilingual ar/en strings for all error codes in `Features/Inventory/Shared/Locales/`
- [ ] T074 [P] OpenAPI round-trip contract test for every endpoint in `Tests/Inventory.Contract/FullRoundTripTests.cs`
- [ ] T075 [P] Correlation-ID middleware propagation verified in `Tests/Inventory.Integration/Observability/CorrelationIdTests.cs`
- [ ] T076 [P] OTel metrics verified (counters tick, histograms populated) in `Tests/Inventory.Integration/Observability/MetricsTests.cs`
- [ ] T077 [P] Alert rules in `infra/monitoring/inventory-alerts.yaml` (oversell, expiry lag, CAS retries, availability p95)
- [ ] T078 [P] Runbook `docs/runbooks/inventory.md` — scanner stuck, sweeper mis-timezone, CAS contention, CSV OOM
- [ ] T079 [P] FsCheck property — reservation state machine legality (no invalid transitions) in `Tests/Inventory.Properties/StateMachineLegalityProperty.cs`
- [ ] T080 FsCheck property — transfer conservation (sum across warehouses unchanged) in `Tests/Inventory.Properties/TransferConservationProperty.cs`
- [ ] T081 Integration test — restricted variant blocks add-to-cart + checkout path in `Tests/Inventory.Integration/Availability/RestrictedVariantTests.cs`
- [ ] T082 Integration test — preorder floor enforcement rejects over-cap reservations in `Tests/Inventory.Integration/Reservations/PreorderFloorBoundaryTests.cs`
- [ ] T083 Update `docs/dod.md` inventory row to `status=green` with verification evidence links
- [ ] T084 Update `docs/implementation-plan.md` spec 008 status + cross-links
- [ ] T085 CHANGELOG entry for inventory module in `services/backend_api/CHANGELOG.md`
- [ ] T086 Constitution fingerprint update (scripts/compute-fingerprint.sh) and PR description block

---

## Dependencies

- Phase 1 → Phase 2 → (Phase 3 ∥ Phase 4) → Phase 5 (needs 3+4) → Phase 6 → Phase 7 → Phase 8 ∥ Phase 9 ∥ Phase 10 → Phase 11 → Phase 12
- US2 depends on US1's snapshot reads; US3 depends on US2 validator reuse; US4 blocks US3 commit path for batch-backed variants; US5–US8 are parallel once US3 green.

## Parallel execution examples

- Phase 3 tasks T018–T022 run in parallel (distinct files).
- Phase 5 T032, T038, T039 run parallel once T030/T031 land.
- Phase 11 T068–T071 all parallel (four independent endpoint files).
- Polish phase T073–T079 all parallel.

## Implementation strategy — MVP

- **MVP = Phases 1–5** (T001–T055): delivers availability reads, basket validation, reservation lifecycle, batch receipt + FEFO + sweeper, admin adjustments. Unblocks orders (011) integration and storefront checkout.
- Phase 8 (low-stock) is operationally important but can follow MVP by 1 sprint.
- Phase 9 (CSV export) and Phase 10 (transfers/returns) can slip to Phase 1.5 if needed without blocking launch.

## Independent test criteria per story

| Story | Independent test |
|---|---|
| US1 | GET availability returns correct state classification across all four states; public/admin field masking verified |
| US2 | POST basket/validate returns partial ok=false with accurate `available` values without mutating snapshot |
| US3 | 100 concurrent reservations for 10 stock: exactly 10 succeed; 90 fail with `insufficient_stock`; zero over-allocation |
| US4 | Sweeper at 03:00 UTC expires KSA batches whose `expiry_at < Asia/Riyadh midnight`, but not EG batches not yet at `Africa/Cairo midnight` |
| US5 | Adjustment writes movement + snapshot + audit + event; invalid reason code returns 400 |
| US6 | Two same-day threshold crossings emit exactly one `low_stock` event |
| US7 | CSV export streams > 100k rows without OOM; caps at 1M; filters applied |
| US8 | Transfer atomically writes paired movements; rollback leaves ledger unchanged; cross-market rejected |

---

## Amendment A1 — Environments, Docker, Seeding

**Source**: [`docs/missing-env-docker-plan.md`](../../../docs/missing-env-docker-plan.md)

**Hard dependency**: PR A1 + PR 004 + PR 005 must merge before this PR. Note: `NodaTime` + `Testcontainers.PostgreSql` packages referenced in T003/T004 are provided by PR A1's `.csproj` additions — this spec no longer needs to add them.

### New tasks

- [ ] T087 [US3] Implement `services/backend_api/Features/Seeding/Seeders/_008_InventorySeeder.cs` (`Name="inventory-v1"`, `Version=1`, `DependsOn=["catalog-v1"]`). Warehouses + reason codes are pre-seeded by migration `V008_002`; seeder only adds snapshot rows + batches + sample movements. Distributions per dataset size (small=120/medium=400/large=1600 snapshot rows): 70% in_stock, 8% low_stock, 10% out_of_stock, 12% preorder. Batches (30/100/300): 60% fresh, 25% near-expiry ≤30d, 10% expiring ≤7d, 5% already expired (sweeper fixtures). 5 historical movements per seeded variant for ledger replay tests.
- [ ] T088 [US3] Integration test `Tests/Inventory.Integration/Seeding/InventorySeederTests.cs`: state distribution within ±2%; FEFO picker against seeded batches returns earliest-expiry first; sweeper dry-run correctly identifies the 5%-expired bucket.
- [ ] T089 Remove the `NodaTime` + `Testcontainers.PostgreSql` additions from T003/T004 (A1 provides both); update T004 to `Tests/Inventory.*` project references only.
