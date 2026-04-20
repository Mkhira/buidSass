# Phase 0 Research: Inventory (008)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Each entry: **Decision**, **Rationale**, **Alternatives Considered**.

---

## R1 — Ledger + snapshot hybrid

- **Decision**: Dual model: `stock_movements` append-only ledger is the source of truth; `variant_stock_snapshot` is a per-(variant, warehouse) denormalised row with `on_hand`, `reserved`, and a monotonic `ats_version bigint`. Both updated inside the same DB transaction on every reservation and movement.
- **Rationale**: Snapshot gives O(1) availability reads (SC-002 120 ms) and O(1) CAS writes (SC-001 no over-allocation). Ledger guarantees replay correctness (SC-004, SC-010).
- **Alternatives**: Pure event-sourced reads — rejected; catalog-scale replays during read path break latency SCs. Snapshot-only — rejected; no audit/replay.

## R2 — Concurrency strategy (CAS on ats_version)

- **Decision**: Reservation writer executes `UPDATE variant_stock_snapshot SET on_hand=?, reserved=?, ats_version=ats_version+1 WHERE variant_id=? AND warehouse_id=? AND ats_version=?`. On 0-row update, retry once after re-reading; second failure returns `inventory.insufficient_stock` or `inventory.concurrent_write_conflict` depending on which predicate fails.
- **Rationale**: Lock-free, scales linearly, avoids row-level locks and deadlocks. Single retry keeps tail latency bounded.
- **Alternatives**: `SELECT FOR UPDATE` — rejected; under 30-QPS hot-SKU contention, tail latency degrades. Serializable isolation — rejected; too broad, high abort rate.

## R3 — Time-zone-aware batch expiry

- **Decision**: Sweeper runs at 03:00 UTC daily. For each batch, it computes local midnight in the warehouse's market tz (`Asia/Riyadh` for KSA, `Africa/Cairo` for EG) via NodaTime; batch is "expired today" iff `expiry_at < warehouse_local_midnight_now`. Sweeper is idempotent (checks batch `state=active` before writing).
- **Rationale**: Physical and regulatory reality is local; UTC-only evaluation causes batches to expire at 03:00 local (KSA) or 05:00 local (EG) which is wrong for regulated goods.
- **Alternatives**: Per-warehouse cron — rejected; 2 markets don't justify the schedule complexity and regulatory audits expect deterministic single-sweep semantics.

## R4 — Reservation expiry scanner cadence

- **Decision**: `BackgroundService` ticks every 30 s; selects up to 500 soft-held reservations where `expires_at < now() - 2s` (2-second grace for clock jitter), expires in batches of 50 per transaction. Uses `SKIP LOCKED` to allow future horizontal scaling.
- **Rationale**: SC-005 p99 ≤ 60 s from expiry to terminal state; 30-s cadence gives ≤ 32-s p99 with headroom. Batch size keeps per-tx work bounded.

## R5 — Event publishing

- **Decision**: Domain events published via MediatR `INotification` to in-process handlers (search-bridge, notification-bridge, audit-writer). No outbox pattern at Phase 1B; single-DB-transaction consistency plus best-effort in-process dispatch is sufficient. Catalog (005) already established the pattern.
- **Rationale**: Matches spec 005/006 conventions, avoids infrastructure creep. Transactional outbox deferred to Phase 1.5 when cross-process durability is required.
- **Alternatives**: Debezium CDC — overkill for launch; adds operational complexity.

## R6 — FEFO picker

- **Decision**: At commit, picker queries batches for `(variant, warehouse)` with `on_hand > 0` and `state=active`, ordered by `expiry_at ASC NULLS LAST, received_at ASC`. Allocates greedily across batches until requested qty is satisfied. If total batch on_hand < requested, falls back to unbatched decrement of `variant_stock_snapshot` (and flags a `batch_shortfall` warning for admin). Picking result stored in `reservation_line.picked_batches` as JSON.
- **Rationale**: Deterministic, auditable, and aligns with dental regulatory requirement to dispatch earliest-expiry first.
- **Alternatives**: FIFO (by received_at) — rejected; ignores regulatory expiry mandate. LIFO — explicitly non-compliant.

## R7 — Low-stock debouncing

- **Decision**: Debounce key `(variant_id, warehouse_id, date_trunc('day', now()))`. A dedicated `inventory.low_stock_notifications` table records emission per key. Event emitter checks for existing row before publishing; existing row = skip.
- **Rationale**: SC-006 requires exactly one event per crossing per day bucket. Table-based dedup is durable and restart-safe.
- **Alternatives**: In-memory set — rejected; lost on restart.

## R8 — Preorder cap

- **Decision**: Preorder-enabled variants allow negative `on_hand` down to a configurable floor (default −1000) per (variant, warehouse). CAS writer rejects reservations that would exceed the floor. Flag lives on catalog variant (`accepts_preorder`, `preorder_floor`); inventory reads it at reservation time.
- **Rationale**: Principle 11 doesn't mandate preorder but merchandising teams asked for it; bounding by floor prevents runaway exposure.

## R9 — Low-stock threshold storage

- **Decision**: Store `low_stock_threshold` on catalog variant (spec 005) rather than a separate inventory table. Inventory reads it at event-emission time. Admin endpoint in inventory module proxies the write back to catalog for consistency.
- **Rationale**: Threshold is a merchandising property; catalog is its natural home. Avoids cross-schema joins on every reservation.
- **Alternatives**: Duplicate column in inventory schema — rejected; dual source of truth.

## R10 — Transfer semantics

- **Decision**: Intra-market transfer writes paired `transfer_out` (negative) + `transfer_in` (positive) movements in the same transaction, linked via a shared `transfer_id` on both rows. `transfer_out` reduces source snapshot ATS immediately. Cross-market transfers return `inventory.cross_market_transfer_unsupported` at launch.
- **Rationale**: Atomicity via single tx prevents partial transfers. Admin UX for transfers deferred to Phase 1.5; endpoint exists for programmatic use + seed migrations.

## R11 — Adjustment reason codes

- **Decision**: Seeded table `adjustment_reason_codes` with 7 codes at launch: `damaged`, `expired`, `theft`, `cycle_count`, `supplier_return`, `transfer`, `initial_receipt`. Each carries `label_en/ar` and `requires_evidence` bool. New codes via migration only (no admin CRUD at launch).
- **Rationale**: Enforces vocabulary consistency for finance audits; migration-only authoring matches catalog taxonomy-key policy (spec 005 clarification).

## R12 — Admin ledger export

- **Decision**: `GET /admin/inventory/ledger?format=csv` streams CSV via `application/csv` content type with chunked transfer; filters by date range, variant, warehouse. Server-side limit: 1 M rows per export; beyond that, caller must paginate by date.
- **Rationale**: Finance uses Excel/CSV; streaming avoids memory blow-ups on large exports.

## R13 — Observability schema

- **Decision**: Structured log fields on every reservation/movement write: `inv.market`, `inv.warehouse`, `inv.variant_hash` (SHA-256 of variant_id — not PII but reduces log payload), `inv.operation`, `inv.quantity_delta`, `inv.ats_before`, `inv.ats_after`, `inv.correlation_id`, `inv.latency_ms`. Availability reads emit aggregated counters, not per-variant logs.
- **Rationale**: Per-write log at expected 30 QPS is tractable; per-read log at expected 500 QPS is not.

## R14 — Warehouse routing at launch

- **Decision**: Inventory handlers accept `warehouse_id` in the reservation payload; when omitted, inventory falls back to `is_default_for_market=true` for the requested market. Multi-warehouse routing logic belongs to shipping (spec 013); inventory never chooses the warehouse itself.
- **Rationale**: Separation of concerns; inventory is a "book of record", not a fulfilment optimiser.

## R15 — Migration + seed ordering

- **Decision**: Migration `V008_001` creates schema and tables. Migration `V008_002` seeds the 7 adjustment reason codes and the two default warehouses (`whs_ksa_01`, `whs_eg_01`). `V008_003` creates indexes (partial unique on active soft-held reservations, covering indexes on snapshot lookup).
- **Rationale**: Split lets seeds be re-applied in dev without recreating schema.

---

## Outstanding Items

None. All Technical Context unknowns resolved. Phase 1 design artifacts may proceed.
