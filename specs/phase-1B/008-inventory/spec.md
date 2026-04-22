# Feature Specification: Inventory (v1)

**Feature Number**: `008-inventory`
**Phase Assignment**: Phase 1B · Milestone 3 · Lane A (backend)
**Created**: 2026-04-22
**Input**: constitution Principles 6, 8, 11, 17, 21, 22, 23, 24, 25, 27, 28, 29.

---

## Clarifications

### Session 2026-04-22

- Q1: How many warehouses at launch? → **B: One logical warehouse per market (eg-main, ksa-main)**. Schema supports N warehouses; runtime config lists the 2 launch locations.
- Q2: Reservation semantics? → **A: Time-bound soft reservations with TTL 15 min, auto-released.** Reservations are written to a `inventory_reservations` table; a background worker releases expired rows.
- Q3: Batch/lot/expiry? → **A: First-class `inventory_batches` table** with `(product_id, warehouse_id, lot_no, expiry_date, qty_on_hand)`; FEFO (first-expiry-first-out) picking at reservation time.
- Q4: Available-to-Sell (ATS) formula? → **A: ATS = on_hand − reserved − safety_stock** per (product, warehouse). Safety stock is admin-configurable per product per warehouse; default 0.
- Q5: Low-stock alerting? → **B: Event-driven.** When ATS crosses the `reorder_threshold` downward, an event `inventory.reorder_threshold_crossed` is emitted; notification campaign spec consumes it. Daily summary digest also emitted.

---

## User Scenarios & Testing

### User Story 1 — Reserve at cart add (P1)
Customer adds 3 gloves to cart. System reserves 3 units (FEFO from nearest-expiry batch) for 15 minutes.

**Acceptance Scenarios**:
1. *Given* ATS = 10 for product X in ksa-main, *when* cart adds 3, *then* a reservation row is written and ATS = 7.
2. *Given* the cart abandons, *when* 15 min elapse, *then* the reservation is released by the worker and ATS returns to 10.
3. *Given* ATS = 2 and cart requests 3, *then* reservation returns partial (2 confirmed + 1 `inventory.insufficient`) OR rejects entirely — behavior configurable per spec 009 cart policy. Default: reject all-or-nothing.

---

### User Story 2 — Deduct on order confirm (P1)
Checkout succeeds. Reservation converts to deduction; on_hand decreases atomically.

**Acceptance Scenarios**:
1. *Given* an active reservation, *when* checkout confirms the order, *then* the reservation is converted (delta: on_hand -= qty, reserved -= qty, ledger row written).
2. *Given* the reservation has already expired between checkout submit and confirm, *then* re-attempt reservation at confirm time; if it still holds, succeed; else `409 inventory.insufficient`.
3. *Given* a confirmed order, *then* `inventory_movements` carries a row `kind=sale`, `source=order:{id}`.

---

### User Story 3 — Batch/lot/expiry governance (P1)
Admin receives a shipment: 200 units, lot `L-2026-042`, expiry `2028-06-30`. Admin records the receipt; system credits the batch.

**Acceptance Scenarios**:
1. *Given* a new batch received, *when* admin posts the receipt, *then* a batch row is upserted and `inventory_movements` row `kind=receipt` is written.
2. *Given* multiple batches with different expiries, *when* a reservation is made, *then* FEFO picks the nearest-expiry batch first.
3. *Given* a batch reaches its expiry date, *then* `inventory.batch_expired` event is emitted daily; batch marked `status=expired`; its qty is subtracted from on_hand via a `kind=writeoff` movement.

---

### User Story 4 — Low-stock alert (P2)
When ATS drops below a product's `reorder_threshold`, system emits the event.

**Acceptance Scenarios**:
1. *Given* threshold = 10, ATS = 11, *when* a reservation brings ATS to 8, *then* `inventory.reorder_threshold_crossed` is emitted once (idempotent — debounced 1 h).
2. *Given* the ATS climbs back above threshold, *then* the debounce timer is reset.

---

### User Story 5 — Admin adjustment + audit (P2)
A physical count finds 2 missing units. Admin records a `kind=adjustment` movement; audit captures actor + reason.

**Acceptance Scenarios**:
1. *Given* admin posts `{ kind: "adjustment", delta: -2, reason: "physical-count-miss" }`, *then* movement row + audit row are written atomically.
2. *Given* an adjustment attempts to bring on_hand below zero, *then* `409 inventory.negative_on_hand_blocked`.

---

### User Story 6 — Restriction interlock (P1)
Restricted product: eligibility is determined by spec 005 restriction evaluator; inventory reservations are still writable (Principle 8 — product visibility never hidden), but checkout enforcement (spec 010) gates the actual order creation.

**Acceptance Scenarios**:
1. *Given* a restricted product, *when* a cart reserves, *then* reservation succeeds; the cart surface shows the restriction badge (spec 009 concern).
2. *Given* an unverified customer tries to confirm checkout, *then* spec 010 rejects and the reservation is released by the checkout flow.

---

### Edge Cases
1. Concurrent reservations for the same product (10 simultaneous carts, ATS = 5) → first 5 succeed, remaining 5 get `409 inventory.insufficient`. Enforced via `SELECT … FOR UPDATE` on the stock row.
2. Reservation TTL boundary: cart held exactly at the 15-minute mark → worker releases; checkout retries and either re-reserves or fails.
3. Warehouse not configured for the product's market → `400 inventory.warehouse_market_mismatch`.
4. Bundle SKU (spec 005) → reservation decrements each component if `bundle_memberships` declares them; otherwise decrements only the bundle SKU itself.
5. Negative-quantity edit on batch → rejected with `400 inventory.batch_qty_negative`.
6. Returns (spec 013) reverse a reservation/movement → `kind=return` with positive delta.
7. Expiry overlap: same lot appears twice → unique constraint `(product_id, warehouse_id, lot_no)` blocks duplicate.
8. Reservation for a product with 0 safety_stock + ATS = 0 → `409 inventory.insufficient` (SC-hit).
9. Reservation spans markets (EG cart + KSA stock) → impossible via market-scoped SKU but guarded anyway.
10. Event storm: 500 simultaneous `reorder_threshold_crossed` events → debounced/deduplicated per product per 1 h.

---

## Requirements (FR-)
- **FR-001**: System MUST maintain per-(product, warehouse) stock rows with `on_hand`, `reserved`, `safety_stock`, `reorder_threshold`.
- **FR-002**: System MUST compute ATS = on_hand − reserved − safety_stock on read.
- **FR-003**: Reservations MUST be time-bound (TTL 15 min default; configurable via `Inventory:Reservations:TtlMinutes`).
- **FR-004**: `ReservationWorker` MUST release expired reservations every ≤ 1 min.
- **FR-005**: Deduction (sale) MUST be atomic with order-confirm; on failure, reservation is preserved.
- **FR-006**: Inventory MUST support N warehouses per market; launch config = `eg-main`, `ksa-main`.
- **FR-007**: Batch/lot/expiry MUST be tracked in `inventory_batches`; picking policy = FEFO.
- **FR-008**: Every on-hand change MUST write a row in `inventory_movements` with `kind ∈ {receipt, sale, return, adjustment, writeoff, transfer_in, transfer_out}`.
- **FR-009**: Admin endpoints MUST support: receipt, adjustment, transfer, expiry-writeoff, batch list/query.
- **FR-010**: Customer-facing endpoint MUST expose ATS as a simple "in_stock | backorder | out_of_stock" bucket per product per warehouse (avoids exposing absolute qty publicly).
- **FR-011**: `inventory.reorder_threshold_crossed` event MUST debounce per (product, warehouse) 1 h.
- **FR-012**: `inventory.batch_expired` event MUST fire daily on the first tick after expiry.
- **FR-013**: Restricted products MUST be reservable at cart — restriction gate lives at checkout (spec 010), not here.
- **FR-014**: Expiry writeoff MUST post a `kind=writeoff` movement; audit preserved.
- **FR-015**: Transfers MUST pair two movements (`transfer_out` + `transfer_in`) in a single transaction.
- **FR-016**: All reservation + deduction operations MUST use `SELECT … FOR UPDATE` on the `inventory_stocks` row for concurrency safety.
- **FR-017**: Concurrency SC: 100 simultaneous reservation attempts for ATS=5 yield exactly 5 successes; rest return `409 inventory.insufficient`.
- **FR-018**: System MUST emit a gauge `inventory_ats{product,warehouse}` (optionally) and a counter `inventory_reservation_conflicts_total`.
- **FR-019**: Admin audit MUST capture actor + reason on every manual movement (Principle 25).
- **FR-020**: Integration with search (spec 006): a `product.availability.changed` event MUST be emitted whenever the `in_stock/backorder/out_of_stock` bucket changes so search documents re-index the `availability` facet.
- **FR-021**: Integration with orders (spec 011): returned items trigger a `kind=return` movement that credits the original batch if identifiable, otherwise a default "restock" batch.
- **FR-022**: Bundle SKUs: if `bundle_memberships` is populated for a SKU, reservation/deduction fan out to components; otherwise operate on the bundle SKU itself.
- **FR-023**: Every admin endpoint requires spec 004 RBAC (`inventory.read`, `inventory.write`).

### Key Entities
- **Warehouse** — (code, market, display_name).
- **InventoryStock** — `(product_id, warehouse_id, on_hand, reserved, safety_stock, reorder_threshold)`.
- **InventoryBatch** — per-lot tracking.
- **InventoryReservation** — time-bound hold.
- **InventoryMovement** — append-only ledger.
- **StockBucketCache** — derived `in_stock|backorder|out_of_stock` per `(product, market)`.

---

## Success Criteria (SC-)
- **SC-001**: Reservation + ATS compute p95 ≤ 25 ms under normal load.
- **SC-002**: Concurrency: 100 simultaneous reservations for ATS=5 yield exactly 5 successes (FR-017).
- **SC-003**: Reservation TTL release worker runs ≤ 1 min after TTL expiry.
- **SC-004**: 0 negative `on_hand` rows across the test suite (self-check invariant).
- **SC-005**: FEFO picking correctness: 100 random batches × 50 reservations → 0 violations of nearest-expiry-first.
- **SC-006**: `inventory.reorder_threshold_crossed` event debounce holds at 1 h (no dup within window).
- **SC-007**: Expiry writeoff runs daily; SC verified by injecting a past-date batch and asserting next-day worker writes the movement.
- **SC-008**: Search `availability` facet updates within 10 s of bucket change (end-to-end with spec 006 indexer).
- **SC-009**: Every admin movement has an audit row with actor + reason.

---

## Dependencies
- Spec 005 — product ids + bundle memberships + market config.
- Spec 004 — admin RBAC.
- Spec 006 — consumes `product.availability.changed`.
- Spec 009/010 — reservation caller.
- Spec 013 — return integration.

## Assumptions
- 2 warehouses at launch (`eg-main`, `ksa-main`). Schema supports N.
- Bundles handled per FR-022.
- Safety stock default = 0; configurable per (product, warehouse).

## Out of Scope
- Multi-hop transfers across markets.
- Demand forecasting / reorder automation (Phase 2).
- Serial numbers (per-unit tracking) — batches suffice at launch.
- Cross-market stock substitution at cart time (Phase 1.5).
