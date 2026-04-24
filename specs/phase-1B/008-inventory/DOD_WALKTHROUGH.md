# DoD Walkthrough — Inventory v1

**Spec**: `008-inventory` · **Phase**: 1B · **Milestone**: 3 · **Lane**: A

## 1. Constitution alignment

| Gate | Evidence |
|---|---|
| P11 inventory depth | `stocks + batches + reservations + movements + reorder debounce + workers` under `Modules/Inventory/`. |
| P23 modular monolith | Inventory implemented as isolated module slice with explicit endpoints + persistence context. |
| P24 explicit state handling | Reservation and batch status transitions implemented (`active/released/converted`, `active/expired/depleted`). |
| P25 audit | Admin and internal stock mutations publish `IAuditEventPublisher` events. |
| P27 UX/error quality | ProblemDetails + `reasonCode` extensions across customer/admin/internal error paths. |
| P28 implementation clarity | Deterministic FEFO tie-break (`expiry_date`, then `batch_id`) and transaction boundaries explicit in code. |

## 2. Delivery evidence by phase

| Phase | Evidence |
|---|---|
| Setup | Module tree + Program wiring + test project scaffold completed (`T001–T003`). |
| Foundational | Primitives + 6 entities + namespace-filtered `InventoryDbContext` + `Inventory_Initial` migration + bootstrap seeder (`T004–T010`). |
| Reservation MVP | Internal create/release/convert endpoints + release worker + concurrency and TTL tests (`T011–T018`). |
| Batch/expiry | Admin batches endpoint + expiry writeoff worker + FEFO/expiry tests (`T019–T023`). |
| Reorder | Reorder debounce integration test + emitter implementation (`T024–T025`). |
| Admin movements | Adjust/transfer/writeoff contracts + implementation (`T026–T028`). |
| Availability sync | Availability event integration test + in-transaction emitter hooks (`T029–T030`). |
| Customer surface | Public availability endpoint with bucket-only payload (`T031–T032`). |
| Return seam | Internal return contract + implementation (`T033–T034`). |
| Polish | Metrics, ICU bundles, OpenAPI artifact, AR editorial file, DoD file (`T035–T038`). |

## 3. Hard-rule compliance checklist

- [x] Inventory migration files are UTF-8 without BOM.
- [x] `InventoryDbContext.OnModelCreating` uses namespace-filtered `ApplyConfigurationsFromAssembly`.
- [x] Inventory table names use lowercase snake_case.
- [x] Singleton hosted workers resolve scoped dependencies via `IServiceScopeFactory.CreateAsyncScope()`.
- [x] Reservation create path uses transactional row locks (`FOR UPDATE`) and enforces contention correctness.
- [x] Movement + stock updates occur in the same transaction for mutating operations.
- [x] Every `Tests/Inventory.Tests/*` class carries `[Collection("inventory-fixture")]`.
- [x] `InventoryTestFactory` migrates `App -> Identity -> Catalog -> Pricing -> Inventory` in order.
- [x] `InventoryTestFactory.ResetDatabaseAsync` truncates inventory + identity + `public.audit_log_entries` + `public.seed_applied` (and dependent module tables).
- [x] Admin/internal endpoints include `.RequireAuthorization(AdminJwt)` and `.RequirePermission("inventory.*")`.
- [x] Admin/internal mutations publish audit events.
- [x] Primitives avoid `DateTime.UtcNow`; worker time is isolated to worker contexts.

## 4. Operational and observability notes

- Reservation conflict counter: `inventory_reservation_conflicts_total`.
- Reservation duration histogram: `inventory_reservation_duration_ms`.
- ATS gauge (optional): `inventory_ats_gauge`.
- Availability transition logs: `inventory.availability_changed` + `product.availability.changed`.
- Reorder transition logs: `inventory.reorder_threshold_crossed`.

## 5. OpenAPI + docs artifacts

- OpenAPI artifact: `services/backend_api/openapi.inventory.json`.
- AR editorial tracker: `specs/phase-1B/008-inventory/AR_EDITORIAL_REVIEW.md`.
- This DoD file: `specs/phase-1B/008-inventory/DOD_WALKTHROUGH.md`.

## 6. Fingerprint

`789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62`

Computed via `bash scripts/compute-fingerprint.sh`; unchanged from prior ratified baseline.

## 7. Post-implementation review remediation

Two review passes ran after Codex finished. 11 real bugs fixed in-session.

### Critical
- **C-01** `Admin/Movements/Transfer` acquired `FOR UPDATE` locks in request direction, so concurrent `Transfer(A→B)` and `Transfer(B→A)` could deadlock. Now locks the lower `warehouseId` first, deterministically.
- **C-02** `ReorderAlertEmitter` logged/emitted the event on every crossing, not just when the `ON CONFLICT DO NOTHING` actually claimed the hour's debounce slot → SC-006 violated. Now checks the affected-rows result and early-returns when the insert was a no-op.

### High
- **H-01** Transfer didn't decrement the source batch's `QtyOnHand` when `BatchId` was provided → stock_levels and `sum(batches.qty_on_hand)` drifted. Now locks the source batch (scoped to product + source warehouse), validates sufficient qty, decrements + flips to `depleted` when zeroed.
- **H-02** Reservation Create pinned a single batch even when `batch.QtyOnHand < item.Qty`; Convert later `Math.Max(0, ...)`-clamped the batch decrement, silently losing units. Create now rejects when the FEFO-picked batch has less than the requested qty. Cross-batch reservations remain a spec-011 enhancement.
- **H-03** `Internal/Movements/Return` silently created a new `InventoryBatch` with the caller-supplied id when the referenced batch didn't exist. Now returns `404 inventory.batch.not_found` if `BatchId` doesn't match an existing batch for `(product, warehouse)`.
- **H-04** Return's `movementIds` were read back via `SourceKind="return" AND SourceId=OrderId` after commit, which returned movements from prior partial-return calls against the same order. Now tracks movement references added in-call and reads `movement.Id` after `SaveChangesAsync`.
- **H-05** `ExpiryWriteoffWorker` left residual qty on expired batches when `stock.OnHand < batch.QtyOnHand` (pre-existing drift) → `status=expired, qty_on_hand > 0` inconsistent. Now always zeros the batch when marking expired; the stock delta is capped at `stock.OnHand` so we don't drive on-hand negative.
- **H-06** Admin Batch Create didn't verify the warehouse exists → orphan batches. Now `db.Warehouses.AnyAsync(...)` check → `404 inventory.warehouse.not_found`.
- **H-07** `Admin/Movements/Writeoff` (and `Adjust` when `BatchId` provided) locked the batch by id only → admin could write off a batch that didn't belong to the posted `(product, warehouse)`. Now the `FOR UPDATE` query also filters by `ProductId` + `WarehouseId` and returns 404 on mismatch.

### Medium
- **M-01** Convert + Create endpoints relied solely on `request.AccountId`; if the body omitted it, audit publishing was skipped (Principle 25 gap for trusted internal callers). Both endpoints now fall back to the JWT `sub` claim via `AdminInventoryResponseFactory.ResolveActorAccountId(context)` before calling the handler.
- **M-03** Batch `PATCH` accepted any string for `Status` (e.g., `"foo"`), breaking downstream queries filtering by enum. Now whitelists `active | depleted | expired` and returns `400 inventory.batch.invalid_status`.

## 8. Deferred (non-blocking)

- **FR-022 bundle fan-out.** Reservation Create processes the requested SKU as a single line. Spec 007-a resolves bundles as their own priced SKUs and leaves `pricing.bundle_memberships` as an analytics-only table. Inventory at launch decrements the bundle SKU itself. Component-level decrement waits on spec 011 cart/checkout decisions.
- **`Admin/Stocks/` + `Admin/Alerts/` read endpoints.** Module tree from T001 includes these dirs (currently empty), but tasks.md has no item implementing them. FR-009 lists "batch list/query" which `Admin/Batches/{List,Get}` covers; stocks/alerts read endpoints are a spec-011 follow-up.
- **Event bus wiring.** Availability + reorder events go through `ILogger` with TODOs. Spec 011/012 wires a real bus.
- **Post-commit emission.** Emitters currently fire before `tx.CommitAsync`; rollback would leak a phantom log line. Reorder debounce is DB-backed so the per-window dedupe itself is transactional. Availability is log-only today. Moving emission post-commit waits on the event bus.
- **Test factory interceptor re-registration** omits Pricing's `ImmutablePriceExplanationInterceptor` when re-registering `PricingDbContext`. Inventory tests don't write `price_explanations`; cross-suite suites are unaffected.

## 9. Test status

| Suite | Result |
|---|---|
| `Inventory.Tests` | 23/23 pass |
| `Catalog.Tests` (regression) | 42/42 pass |
| `Search.Tests` (regression) | 60/60 pass |
| `Pricing.Tests` (regression) | 45/45 pass |

Total: 170 tests pass. Build: 0 errors, 2 pre-existing SixLabors CVE warnings.
