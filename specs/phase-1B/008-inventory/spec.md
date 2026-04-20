# Feature Specification: Inventory (008)

**Phase**: 1B · **Stage**: 3.5 (Inventory) · **Created**: 2026-04-20
**Depends on**: 005 (catalog variants) · **Consumed by**: 006 (search availability facet), 007-a (price/snapshot interaction), 009 (cart validation), 010 (checkout reservations + commits), 011 (orders + refund/return restocks), 012 (tax invoice lot numbers), 025 (low-stock notifications)

> Launch-grade inventory for a dental commerce platform: append-only stock ledger, available-to-sell computation, TTL soft-holds, hard commits on payment, low-stock events, batch/lot/expiry with FEFO picking guidance. Multi-warehouse-ready at the data layer even though launch operates with one fulfilment location per market (Principle 11).

---

## 1. Goal

Provide a single, auditable inventory source of truth for variant stock across warehouses and markets, enabling:
1. Accurate real-time Available-to-Sell (ATS) for every variant per market.
2. Reliable reservation lifecycle — soft-hold at checkout start (TTL-bounded), hard-commit on payment authorisation, release on cancel/timeout/refund.
3. Batch/lot and expiry tracking with FEFO (First-Expiry-First-Out) picking guidance for restricted medical goods.
4. Low-stock threshold signals consumable by notifications (spec 025) and search facet state (spec 006).
5. Admin visibility into movements, reservations, and adjustments, fully audited.

Satisfies Principles **11** (inventory depth), **21** (operational readiness), **24** (state models), **25** (audit).

---

## 2. User Roles

| Role | Interaction |
|---|---|
| Customer (indirect) | Sees availability state on search/detail; triggers reservation via cart/checkout |
| Admin — `inventory.read` | Views stock levels, ledger, reservations, dead-letter on failed events |
| Admin — `inventory.adjust` | Posts movements (receive, write-off, transfer, cycle-count correction) with reason code |
| Admin — `inventory.reserve.manage` | Cancels or extends a reservation; forces release |
| Admin — `inventory.batch.manage` | Authors batch/lot records, expiry dates, FEFO overrides |
| Warehouse operator (admin console, future) | Same as `inventory.adjust`, scoped to one warehouse — RBAC scope column already carried for future enforcement |
| Finance — `inventory.audit` | Exports ledger + valuation deltas for a given window |

---

## 3. Business Rules

### 3.1 Append-Only Stock Ledger

- Every quantity change is a `stock_movement` row. Rows are never updated or deleted — corrections post a **reversing** movement.
- Movement types: `receive`, `customer_allocation`, `customer_release`, `customer_commit`, `write_off`, `cycle_count_adjust`, `transfer_out`, `transfer_in`, `return_restock`, `reservation_expire`.
- Every movement references: `variant_id`, `warehouse_id`, `batch_id?`, `quantity` (signed int), `reason_code`, `reference` (order/basket/transfer id), `actor_id`, `correlation_id`, `recorded_at`.

### 3.2 Availability States

For each `(variant_id, market_code)`:

- `on_hand` = Σ movements over warehouses serving the market.
- `reserved` = active reservations (soft-hold + hard-commit pending) for that variant.
- `ats` (available-to-sell) = `on_hand − reserved`.
- `availability_state` derived (for search facet and product detail):
  - `out` when `ats ≤ 0`
  - `low` when `0 < ats ≤ low_stock_threshold(variant)`
  - `in_stock` when `ats > low_stock_threshold`
  - `preorder` when `on_hand ≤ 0` **and** `variant.accepts_preorder = true` (flag on catalog variant)

### 3.3 Reservation Lifecycle

State machine: `soft_held → committed → fulfilled | released | expired`.

- **Create soft-hold**: called by checkout-start (spec 010). Atomically deducts from ATS and writes `customer_allocation` + `reservation` row with `state=soft_held`, `ttl_seconds=900` (15 min default; configurable per market), `expires_at`.
- **Extend soft-hold**: allowed once per reservation up to `2 × ttl_seconds` cumulative when caller proves active checkout intent (e.g., payment-form submit). Audited.
- **Commit**: on payment authorisation (spec 010), `soft_held → committed`, writes `customer_commit` movement reducing `on_hand` and clearing `reserved`.
- **Fulfil**: when warehouse dispatches (spec 011 order fulfilled state), `committed → fulfilled`; recorded for audit; no ledger move (already committed).
- **Release** (customer or admin cancels during soft_held or committed-but-unfulfilled): writes `customer_release` movement restoring ATS.
- **Expire**: background scanner moves `soft_held → expired` when `expires_at < now()`; writes `reservation_expire` movement restoring ATS. Runs every 30 s.
- Reservation rows are **never deleted** — lifecycle captured in state transitions with timestamps and actor.

### 3.4 Batch / Lot / Expiry

- A `batch` row captures: `id`, `variant_id`, `warehouse_id`, `lot_number` (text, unique per variant+warehouse), `manufacturer_batch_code?`, `manufactured_at?`, `expiry_at?`, `received_qty`, `received_at`, `supplier_id?`.
- Movements referencing a batch are tracked per batch: on-hand per `(variant, warehouse, batch)` computed from movements.
- **FEFO picking guidance**: when reservation is committed and the variant has batches, system picks the batch(es) with the earliest `expiry_at` having available qty. Picking guidance is surfaced as part of the commit response (consumed by 011 and/or admin UI); engine does **not** force physical pick but records the guidance used.
- Expired batches (`expiry_at < today`) are **excluded from ATS** automatically and marked `state=expired` (background job runs daily at 03:00 UTC).
- Admin may override FEFO per commit with a reason code (audited).

### 3.5 Low-Stock Thresholds & Events

- Per-variant `low_stock_threshold` (int ≥ 0) authored in catalog admin or via inventory API. Default 5 at launch for physical goods; 0 disables the event.
- Crossing the threshold downward (ats transitioning `> threshold` → `≤ threshold`) emits `inventory.low_stock` domain event once per transition, not repeatedly. Debouncing key `(variant_id, warehouse_id, day_bucket)`.
- Crossing back upward emits `inventory.stock_recovered`.

### 3.6 Concurrency & Race Safety

- ATS reads are eventually consistent; reservation writes are strongly consistent.
- Reservation creation uses a **conditional update** (compare-and-swap on a per-(variant, warehouse) row with a monotonic `ats_version`) so two concurrent checkouts cannot double-book the last unit. Failed CAS → retry once → return `inventory.insufficient_stock` with the current `ats`.
- Multi-unit requests served atomically: either the entire requested quantity is reserved or none.
- Transfers between warehouses post paired `transfer_out` (negative) + `transfer_in` (positive) movements in a single DB transaction.

### 3.7 Warehouse Model (multi-warehouse-ready, single-warehouse at launch)

- `warehouses` table: `id`, `code`, `name_en`, `name_ar`, `market_code`, `address_*`, `active`, `is_default_for_market`.
- Launch seeds one warehouse per market (`whs_ksa_01`, `whs_eg_01`), both `is_default_for_market=true`.
- Every movement carries `warehouse_id`; ATS aggregated across warehouses serving the market.
- Routing to a specific warehouse is the responsibility of **shipping + fulfilment (spec 013 / future)**; inventory consumes a warehouse choice, does not make one. Launch default: each market's default warehouse.

### 3.8 Adjustments & Cycle Counts

- Admin posts adjustment with: `type` (`receive`, `write_off`, `cycle_count_adjust`), `variant`, `warehouse`, `batch?`, `quantity_delta`, `reason_code`, `evidence_ref?` (e.g., goods receipt note id).
- Negative cycle-count corrections trigger an `inventory.adjustment_posted` audit row plus — if the correction would have driven ATS below any active reservation — a `reservation_impaired` warning in the response. No automatic reservation cancellation; admin handles.

### 3.9 Audit & Observability

- Every admin write (adjustment, reservation force-action, batch edit, threshold update) emits an `audit_events` row with before/after and actor.
- Reservation lifecycle transitions emit audit rows too (create, extend, commit, release, expire).
- Ledger movements themselves are the source of truth; admin inspection is a read over the ledger plus an index table for fast "current state" queries.
- Domain events published for: `inventory.low_stock`, `inventory.stock_recovered`, `inventory.reservation_committed`, `inventory.reservation_released`, `inventory.reservation_expired`, `inventory.batch_expired`, `inventory.adjustment_posted`.

### 3.10 Integration Points

- **Cart (spec 009)** calls `GET /inventory/availability?variant=…&market=…` for real-time ATS badge and calls `POST /inventory/validate` for basket-level pre-checkout check.
- **Checkout (spec 010)** calls `POST /inventory/reservations` (soft-hold) then `POST /inventory/reservations/{id}/commit` on payment auth, or `POST /inventory/reservations/{id}/release` on cancel/failure.
- **Orders (spec 011)** calls `POST /inventory/reservations/{id}/fulfil` when warehouse dispatches; calls `POST /inventory/movements/return-restock` on approved returns.
- **Search (spec 006)** consumes `inventory.*` domain events to refresh `availability_state` facet.
- **Notifications (spec 025)** consumes `inventory.low_stock` + `inventory.batch_expired`.

---

## 4. Primary User Flows

### Flow A — Checkout reservation lifecycle (happy path)

1. Customer clicks "Proceed to checkout". Checkout calls `POST /inventory/reservations` with `{ basket_id, lines, market_code, ttl_seconds? }`.
2. Inventory computes ATS for each line, attempts CAS write; returns 201 with `{ reservation_id, expires_at, lines[] }`.
3. Customer completes payment. Checkout calls `POST /inventory/reservations/{id}/commit`.
4. Inventory transitions reservation to `committed`, posts `customer_commit` movements (picking batches FEFO if applicable), returns commit summary including `picking_guidance[]`.
5. Warehouse dispatches. Order service calls `POST /inventory/reservations/{id}/fulfil` — terminal state.

### Flow B — Insufficient stock

1. Checkout calls reservation endpoint.
2. Inventory detects ats < requested. Returns 409 `inventory.insufficient_stock` with `{ variantId, requested, available }` in details.
3. Checkout surfaces error; customer adjusts cart.

### Flow C — Reservation expiry

1. Customer abandons checkout after soft-hold.
2. Background scanner detects `expires_at < now()` → transitions to `expired`, posts `reservation_expire` movement.
3. `inventory.reservation_expired` event published. Abandoned-cart email (spec 025) may fire.
4. ATS recovers; search facet refreshes within event-propagation SLA.

### Flow D — Return restock

1. Customer returns item (spec 011 return-approved state).
2. Order service calls `POST /inventory/movements/return-restock` with `{ order_id, variant, warehouse, batch?, quantity, reason }`.
3. Inventory posts `return_restock` movement, increments ATS, optionally resurrects a batch if lot number matches.

### Flow E — Admin adjustment

1. Admin posts `POST /admin/inventory/adjustments` with type=`write_off`, quantity=-3, reason=`damaged_in_transit`, evidence=`grn_1234`.
2. Inventory posts `write_off` movement; audit row written.
3. If adjustment impairs an active reservation, response carries warning; admin decides whether to release.

### Flow F — Batch expiry sweep

1. Daily 03:00 UTC job scans batches where `expiry_at < today`.
2. For each expired batch with `on_hand > 0`, posts `write_off` movement with reason `expired` and emits `inventory.batch_expired`.
3. Audit row written.

---

## 5. UI States (consumed by spec 014 customer app and admin UI)

- **Availability badge** — `in_stock` / `low_stock` (with count if admin-configured to show) / `out` / `preorder`. Never shows "unknown" — missing data surfaces `out`.
- **Checkout loading** — waits for reservation call; shows skeleton with explicit "reserving your items…" copy bilingual.
- **Checkout reservation failure** — shows bilingual error with the specific short stock, calls to action to reduce quantity.
- **Cart staleness** — if cart-level validate returns a line that is now `out`, UI flags the affected line with a remove/adjust action.
- **Admin — reservation inspection** — table view of active reservations with filter by state, variant, warehouse; actions `release` / `extend`.
- **Admin — ledger view** — chronological movement stream with filters; export to CSV.
- **Admin — low-stock dashboard** — variants currently at or below threshold, ordered by ats ASC.
- **Admin — batch expiry dashboard** — batches expiring in next 30/60/90 days.

---

## 6. Data Model (logical)

### 6.1 Key Entities

- **Warehouse** — `id`, `code`, `name_en`, `name_ar`, `market_code`, `active`, `is_default_for_market`, address fields.
- **StockMovement** — append-only ledger (see §3.1).
- **VariantStockSnapshot** — denormalised current state per `(variant_id, warehouse_id)`: `on_hand`, `reserved`, `ats_version` (monotonic int for CAS). Updated in the same transaction as each movement / reservation write.
- **Reservation** — `id`, `basket_id`, `order_id?`, `market_code`, `state` (`soft_held`/`committed`/`fulfilled`/`released`/`expired`), `ttl_seconds`, `expires_at`, `extended_count`, `customer_id?`, `correlation_id`.
- **ReservationLine** — `reservation_id`, `variant_id`, `warehouse_id`, `quantity`, `picked_batches[]` (populated at commit).
- **Batch** — `id`, `variant_id`, `warehouse_id`, `lot_number`, `manufacturer_batch_code?`, `manufactured_at?`, `expiry_at?`, `received_qty`, `state` (`active`/`exhausted`/`expired`), `supplier_id?`.
- **BatchMovement** — optional rollup denormalisation: per-batch `on_hand`. (Logical; physical design in plan.)
- **LowStockThreshold** — `variant_id`, `threshold_qty`, `updated_at`, `updated_by`. (Stored on catalog variant if simpler; see plan decision.)
- **AdjustmentReasonCode** — `code`, `label_en`, `label_ar`, `requires_evidence` (bool), seeded list (damaged, expired, theft, cycle_count, supplier_return, transfer, initial_receipt).

### 6.2 State Machines

**Reservation**: `soft_held → committed → fulfilled` | `soft_held → released | expired` | `committed → released` (pre-fulfil cancel). No transition from `fulfilled`.

**Batch**: `active → exhausted` (on_hand hits 0) ↔ `active` (restock with same lot) | `active → expired` (sweep job).

### 6.3 Invariants

- `on_hand_per_batch ≥ 0` at all times (enforced by CAS).
- `sum(batch.on_hand for variant+warehouse) = VariantStockSnapshot.on_hand` when batches tracked for that variant.
- `reserved ≤ on_hand` always.
- No two reservations may share the same `(basket_id, state=soft_held)` — partial unique index.
- `reservation_line.quantity > 0`.
- `movement.quantity ≠ 0`.

---

## 7. Validation Rules

- `quantity` positive integer except in explicit movement deltas; absolute value capped at 100 000 per single movement.
- `ttl_seconds` in range [60, 3600]; default per-market configurable.
- Batch `lot_number` case-insensitive unique per `(variant_id, warehouse_id)` among non-expired rows.
- `expiry_at > manufactured_at` when both present.
- `adjustment.reason_code` must exist in seeded list.
- Extension of reservation allowed at most once per reservation.
- `low_stock_threshold` in range [0, 10 000].

---

## 8. API / Service Requirements

Customer / internal callers:
- `GET /inventory/availability` — single or batch availability + state, market-scoped.
- `POST /inventory/validate` — basket-level validation, returns per-line ok/short/out.
- `POST /inventory/reservations` — create soft-hold.
- `POST /inventory/reservations/{id}/extend` — extend TTL (once).
- `POST /inventory/reservations/{id}/commit` — commit (payment auth).
- `POST /inventory/reservations/{id}/fulfil` — warehouse dispatched.
- `POST /inventory/reservations/{id}/release` — cancel soft-held or committed-unfulfilled.
- `POST /inventory/movements/return-restock` — invoked by orders on approved return.

Admin:
- `GET /admin/inventory/stock` — current snapshot, filters by variant/warehouse/market/state.
- `GET /admin/inventory/ledger` — ledger stream with filters + CSV export.
- `POST /admin/inventory/adjustments` — post adjustment.
- `POST /admin/inventory/transfers` — paired transfer_out/in.
- `GET|POST|PUT /admin/inventory/batches[/{id}]` — batch CRUD.
- `GET|PUT /admin/inventory/thresholds/{variantId}` — low-stock threshold read/update.
- `GET /admin/inventory/reservations` — active reservation inspection.
- `POST /admin/inventory/reservations/{id}/force-release` — admin override release.
- `GET /admin/inventory/low-stock` — dashboard data.
- `GET /admin/inventory/batch-expiry` — dashboard data (next 30/60/90 days).
- `GET /admin/inventory/warehouses` — list (read-only at launch — seeded warehouses only).

Internal events (published): `inventory.low_stock`, `inventory.stock_recovered`, `inventory.reservation_committed`, `inventory.reservation_released`, `inventory.reservation_expired`, `inventory.batch_expired`, `inventory.adjustment_posted`.

---

## 9. Edge Cases

- **Zero-quantity reservation line** — rejected as `inventory.validation_failed`.
- **Preorder variant** — reservation still succeeds with `on_hand ≤ 0` when `accepts_preorder=true`; negative snapshot allowed with a hard cap (configurable, default −1000) to bound exposure.
- **Simultaneous commit + release** — commit wins if it reaches the state machine first (atomic check); the losing call returns `inventory.reservation_state_conflict`.
- **Clock skew on expiry** — sweeper uses DB server `now()` exclusively to avoid drift.
- **Batch mid-reservation** — if FEFO batch picking yields multiple batches for a single line, response carries all picks with quantities summing to the line quantity.
- **Return of non-tracked batch** — restock allowed without batch reference; warning recorded.
- **Transfer across markets** — blocked at launch (each warehouse is market-scoped); attempted cross-market transfer returns `inventory.cross_market_transfer_unsupported`.
- **Negative adjustment below reserved** — allowed with reservation-impaired warning (§3.8).
- **Duplicate reservation create** — partial-unique index on `(basket_id, state=soft_held)` rejects second create; caller receives existing reservation id.
- **Ledger replay** — for any `(variant, warehouse)` snapshot, replaying the ledger in recorded order MUST reproduce current `on_hand`. Property test verifies.
- **Out-of-order event consumption** (by search) — events carry `sequence_number` per variant so consumers can detect reordering.
- **Time zone of expiry-sweep** — batches expire at 00:00 in the warehouse's local time zone (market-scoped: Asia/Riyadh, Africa/Cairo) to match physical reality; sweeper scheduled in UTC but evaluates tz-aware.

---

## 10. Acceptance Criteria

### US1 — Reliable ATS under concurrency (P1)
- Given 100 concurrent reservation requests for the last 50 units of a variant, exactly 50 succeed and 50 return `inventory.insufficient_stock`. No over-allocation. Property-based + load test.

### US2 — Reservation lifecycle (P1)
- Soft-hold → commit → fulfil produces correctly ordered ledger entries and ATS matches on-hand post-commit. Release after commit restores ATS.
- TTL-expired soft-hold returns inventory; `inventory.reservation_expired` event fires within 60 s of `expires_at`.

### US3 — Batch / FEFO picking (P1)
- Given three batches for the same variant with staggered expiries, committing a reservation picks the earliest-expiry batches first; response carries exact batch breakdown; ledger reflects per-batch decrements.
- Expired batches are excluded from ATS by the daily sweep job within one day of expiry.

### US4 — Low-stock events (P1)
- Variant with threshold=5 at ats=6, reservation of qty=2 emits exactly one `inventory.low_stock` event (debounced, no duplicates for same day bucket).
- Restock that takes ats back above threshold emits `inventory.stock_recovered`.

### US5 — Admin adjustments + audit (P2)
- Admin posts a `write_off` adjustment; ledger gains the entry, snapshot reflects new on_hand, `audit_events` row captures actor + before/after, evidence reference retained.

### US6 — Ledger replay correctness (P1)
- Replaying the ledger for any `(variant, warehouse)` pair reproduces the current snapshot. Enforced by a property-based test across 10 000 random movement sequences.

### US7 — Reservation inspection + force-release (P2)
- Admin lists active reservations filtered by variant; force-release transitions state to `released`, posts `customer_release` movement, audit row written.

### US8 — Search facet propagation (P1)
- Availability-state transition publishes a domain event consumed by search (spec 006) within p95 ≤ 2 s so the facet reflects new state.

---

## 11. Success Criteria

- **SC-001**: Zero over-allocation in 100 000 concurrent CAS reservation attempts across 10 hot SKUs.
- **SC-002**: `GET /inventory/availability` (batched up to 50 variants) p95 ≤ 120 ms.
- **SC-003**: `POST /inventory/reservations` p95 ≤ 250 ms for a 20-line basket.
- **SC-004**: Ledger-replay correctness — 100% match between replay and snapshot for a 10 000-movement fuzz corpus.
- **SC-005**: Reservation expiry scanner detects expiry within ≤ 60 s of `expires_at` at the 99th percentile.
- **SC-006**: Low-stock event emitted exactly once per threshold-crossing within a (variant, warehouse, day) bucket — 0 duplicates in a 7-day simulation run.
- **SC-007**: Daily batch-expiry sweep processes 10 k batch rows within 60 s.
- **SC-008**: 100% of admin adjustments, reservation force-actions, and threshold updates write an `audit_events` row.
- **SC-009**: Inventory event → search facet refresh end-to-end p95 ≤ 2 s (consistent with SC-001 of spec 006).
- **SC-010**: ATS accuracy — for any point in time, `ats = on_hand − reserved` holds across 1 M-movement replay.

---

## 12. Clarifications

### Session 2026-04-20 (auto-resolved per user directive — recommended defaults)

- **Q: Default reservation TTL** → A: 900 seconds (15 min) per reservation, with one permitted extension up to 2× TTL. Per-market configurable. (Rationale: balances typical checkout completion time against inventory exposure; matches industry norm.)
- **Q: Preorder policy at launch** → A: Supported per-variant via `accepts_preorder` flag (catalog variant), capped at an absolute on-hand floor of −1000 per (variant, warehouse) to bound exposure. Default off for all variants. (Rationale: enables launch marketing without uncontrolled backorder; admins can opt in per SKU.)
- **Q: Multi-warehouse at launch** → A: Data model supports multi-warehouse; physical launch seeds one warehouse per market (`whs_ksa_01`, `whs_eg_01`). Cross-market transfers blocked; intra-market transfers enabled by data but not exposed in admin UI until Phase 1.5. (Rationale: matches Principle 11 "multi-warehouse-ready"; keeps UX scope tight.)
- **Q: Batch expiry granularity & sweep cadence** → A: Daily sweep at 03:00 UTC; batches evaluate expiry at warehouse-local 00:00 (KSA: Asia/Riyadh, EG: Africa/Cairo). (Rationale: matches physical/regulatory reality without sub-hour complexity.)
- **Q: Reservation force-release permissions** → A: RBAC policy `inventory.reserve.manage` distinct from `inventory.adjust`; audited. Admin force-release triggers same ledger + events as customer-initiated release. (Rationale: separates reservation-ops from stock-ops responsibilities.)

---

## 13. Dependencies

- **005 catalog** — variants, `accepts_preorder`, `low_stock_threshold` (may live here or on catalog variant — plan decides), tax_class, currency (for valuation reports, future).
- **004 identity** — actor subject, RBAC policies `inventory.*`.
- **003 shared audit** — `audit_events`.
- **Market config** — Asia/Riyadh, Africa/Cairo time zones; per-market TTL override optional.

Consumed by: 006 (search), 007-a (snapshot captures reservation at commit for audit cross-reference), 009 (cart), 010 (checkout), 011 (orders/returns), 012 (invoice lot refs), 013 (shipping — future warehouse routing), 025 (notifications).

---

## 14. Assumptions

- One physical warehouse per market at launch; data layer supports many.
- Valuation (cost-of-goods, FIFO/average) is **out of scope** for Phase 1B — ledger captures quantities only. Finance valuation ships with Phase 1.5.
- No serial-number tracking (per-unit identity) at launch — batch/lot only.
- No stock-outs across markets: each market manages its own stock; cross-market borrowing out of scope.
- Returns restocking requires admin approval (gated in spec 011); inventory endpoint trusts caller after approval.
- Reservation payload is flat (no nested bundles); if a basket contains bundle promo (spec 007-a), checkout expands to component variants before calling inventory.
- Clock source is the DB server; app servers never write `recorded_at`.
- No cold-chain temperature tracking for batches at launch (regulatory, future).

---

## 15. Out of Scope (explicit)

- Stock valuation / cost of goods — Phase 1.5+.
- Per-unit serial tracking — Phase 2+.
- Supplier-side PO workflow — Phase 1.5 (`procurement` module).
- Automatic reorder point / replenishment suggestions — Phase 1.5.
- Cross-market transfers — Phase 2.
- Physical warehouse management UX (bin locations, pick paths) — Phase 2.
- Temperature / cold-chain — Phase 2+.

---

## 16. Constitution Anchors

Principles exercised: **11** (inventory depth), **21** (operational readiness), **24** (state models for reservation, batch), **25** (audit on critical actions), **27** (UX states), **28** (AI-build standard), **29** (required spec output). ADR anchors: **ADR-003** (vertical slice + MediatR), **ADR-004** (EF Core), **ADR-010** (single-region residency).
