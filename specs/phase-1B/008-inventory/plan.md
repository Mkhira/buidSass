# Implementation Plan — Inventory v1 (Spec 008)

**Branch**: `phase-1B-specs` · **Date**: 2026-04-22

## Technical Context
- **Runtime**: .NET 9 / C# 12.
- **DB**: PostgreSQL 16; schema `inventory`.
- **Module**: `services/backend_api/Modules/Inventory/`.
- **Deps (NuGet)**: EF Core (shared), FluentValidation, background-service primitives.

## Constitution Check
| Principle | Gate | Note |
|---|---|---|
| 6 — Multi-vendor-ready | PASS | `owner_id/vendor_id` on warehouses + stocks. |
| 8 — Restricted visibility | PASS | Reservations accepted; restriction gating lives at checkout (spec 010). |
| 11 — Inventory depth | PASS | Stocks + batches + reservations + movements + ATS + thresholds. |
| 17 — Post-purchase | PASS | `kind=return` integrated with spec 013. |
| 21 — Operational readiness | PASS | Admin surface + audit + movements ledger. |
| 22/23 — Stack + architecture | PASS | .NET + Postgres; modular monolith slice. |
| 24 — State machines | PASS | Reservation lifecycle + Batch lifecycle enumerated in data-model. |
| 25 — Audit | PASS | Every admin movement audited. |
| 27 — UX | PASS | Public buckets (in_stock/backorder/out_of_stock) not raw numbers. |

**Gate**: PASS.

## Phase A — Primitives
- `Primitives/AtsCalculator.cs` — pure function: `ATS = on_hand − reserved − safety_stock`.
- `Primitives/Fefo/FefoPicker.cs` — picks batches nearest-expiry-first.
- `Primitives/BucketMapper.cs` — ATS → `in_stock|backorder|out_of_stock`.
- `Primitives/Reservation/ReservationLease.cs` — record type.

## Phase B — Persistence
- 6 entities + EF configs (data-model.md).
- Migration `Inventory_Initial`.
- Seed two warehouses `eg-main`, `ksa-main` via `InventoryBootstrapSeeder`.

## Phase C — Core ops
- `Reservations/Create/` — `SELECT FOR UPDATE` row-lock; inserts reservation + decrements `reserved` counter.
- `Reservations/Release/` — worker-driven and manual.
- `Reservations/Convert/` — called by order-confirm (spec 011): turns reservation → deduction.
- `Movements/Adjust/`, `Movements/Receipt/`, `Movements/Transfer/`, `Movements/Writeoff/`.

## Phase D — Workers
- `ReservationReleaseWorker` — 30 s tick; releases expired reservations.
- `ExpiryWriteoffWorker` — daily 01:00 UTC tick; writes off expired batches.
- `AvailabilityEventEmitter` — observes stock-row changes; emits `product.availability.changed` when bucket transitions (consumed by spec 006 indexer).

## Phase E — Admin surface
- CRUD + queries (batches list, movements ledger, stock grid).

## Phase F — Customer surface
- `Customer/GetAvailability/` → returns bucket only.

## Phase G — Events
- `inventory.reorder_threshold_crossed` (debounced 1 h).
- `inventory.batch_expired` (daily).
- `product.availability.changed` (immediate).

## Phase H — Testing
- Unit: ATS calc, FEFO picker, bucket mapper.
- Integration (Testcontainers Postgres): concurrency SC-002, TTL SC-003, FEFO SC-005, bucket propagation SC-008.
- Contract tests for FRs.

## Phase I — Polish
- Reason-code messages `inventory.{ar,en}.icu`.
- OpenAPI + fingerprint + DoD.

## Complexity Tracking
| Item | Why | Mitigation |
|---|---|---|
| Row-level locking on stock row | Only reliable way to guarantee SC-002. | One short transaction per reservation; no cross-row locks. |
| Batch + reservation + movement split | Operational realism (Principle 11). | Separate tables with append-only movements; keep queries indexed on `(product_id, warehouse_id)`. |

**Post-design re-check**: PASS.
