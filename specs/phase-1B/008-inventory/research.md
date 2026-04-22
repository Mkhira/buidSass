# Research — Inventory v1 (Spec 008)

**Date**: 2026-04-22

## R1 — Concurrency
**Decision**: `SELECT … FOR UPDATE` on the `inventory_stocks` row (one per `(product_id, warehouse_id)`).
**Rationale**: Most proven approach under PostgreSQL; avoids optimistic-retry storms. Row is short — the transaction ends in < 5 ms typical.
**Alternative**: Optimistic `rowversion` — rejected because under heavy contention it produces cascading retries.

## R2 — Reservation representation
**Decision**: Separate `inventory_reservations` table with TTL. `inventory_stocks.reserved` is a running counter kept in sync inside the same transaction.
**Rationale**: Ledger+counter split keeps hot-path reads cheap; counter = O(1) ATS.
**Alternative**: Compute reserved from reservations on every read — rejected (scans under load).

## R3 — FEFO
**Decision**: Picker orders batches by `expiry_date ASC` then `received_at ASC`, skipping `status != active`.
**Rationale**: Matches industry expectation for medical/pharma.
**Alternative**: FIFO (received-date) — rejected because nearer-expiry stock must leave first.

## R4 — TTL
**Decision**: 15 min default, configurable.
**Rationale**: Balances abandoned-cart slack vs stock-hoarding. Matches marketplace norms.

## R5 — Bucket mapping
**Decision**: `in_stock` when ATS ≥ 1; `backorder` when ATS = 0 AND batch exists with future receipt window; `out_of_stock` otherwise.
**Rationale**: Avoids exposing absolute quantities publicly.

## R6 — Bundle handling
**Decision**: If `catalog.bundle_memberships` declares components for the SKU, fan out reservations/deductions to components using each component's `qty`.
**Rationale**: Keeps inventory accurate; admin can still see the bundle-SKU-level movement for analytics.
**Alternative**: Treat bundle as standalone — rejected because physical fulfilment needs component-level accuracy.

## R7 — Debounce on reorder event
**Decision**: Emit at most once per `(product, warehouse)` per 1 h; tracked in an in-proc `ConcurrentDictionary<_, DateTimeOffset>` with a persisted fallback in a `reorder_event_emissions` table.
**Rationale**: Avoids notification noise on flapping carts.

## R8 — Expiry writeoff
**Decision**: Daily 01:00 UTC worker; any batch with `expiry_date < today` and `status=active` is marked `expired` and a `kind=writeoff` movement posts the qty reduction.
**Rationale**: Predictable operational cadence.

## R9 — Integration with search
**Decision**: On every stock-row update, if the `(market, product)` bucket changes, emit `product.availability.changed` event; spec 006 indexer consumes it to re-index the `availability` facet.
**Rationale**: Storefront filters stay accurate within 10 s.

## R10 — Safety stock
**Decision**: Per `(product, warehouse)` column on `inventory_stocks`; default 0; admin-configurable.
**Rationale**: Protects headroom for in-person walk-ins / VIP orders; common operational lever.

## R11 — Returns integration
**Decision**: Spec 013 posts a `kind=return` movement referencing the original order/batch; if the batch is no longer active, a designated "restock" batch receives the qty.
**Rationale**: Keeps FEFO honest for returned inventory.

## R12 — Observability
**Decision**: Metric `inventory_reservation_conflicts_total` (counter tagged by product/warehouse) + gauge `inventory_ats_bucket` (per product/warehouse). Prometheus-compatible.
