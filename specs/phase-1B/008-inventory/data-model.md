# Phase 1 Data Model: Inventory (008)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md)

All tables live in schema `inventory`. Monetary concerns absent (valuation out of scope). All tables have `created_at`, `updated_at`, `created_by`, `updated_by`, `xmin_row_version` except where noted (ledger is append-only — no updated_* columns).

---

## 1. `inventory.warehouses`

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `code` | `text` UNIQUE NOT NULL | e.g., `whs_ksa_01` |
| `name_en` / `name_ar` | `text` NOT NULL | |
| `market_code` | `text` NOT NULL CHECK in (`eg`,`ksa`) | |
| `is_default_for_market` | `bool` NOT NULL DEFAULT false | |
| `active` | `bool` NOT NULL DEFAULT true | |
| `address_line1` / `city` / `country` / `postal_code` | `text` | |
| `time_zone` | `text` NOT NULL | IANA (`Asia/Riyadh`, `Africa/Cairo`) |

**Constraints**: Partial unique index on `(market_code) WHERE is_default_for_market=true` — at most one default per market.

**Seed**: `whs_ksa_01` (market=ksa, tz=Asia/Riyadh, default), `whs_eg_01` (market=eg, tz=Africa/Cairo, default).

---

## 2. `inventory.variant_stock_snapshot`

Denormalised current state; updated transactionally with every movement/reservation write.

| Column | Type | Notes |
|---|---|---|
| `variant_id` | `ulid` NOT NULL | |
| `warehouse_id` | `ulid` NOT NULL FK → warehouses | |
| `on_hand` | `bigint` NOT NULL DEFAULT 0 | Can be negative for preorder variants (bounded by floor) |
| `reserved` | `bigint` NOT NULL DEFAULT 0 CHECK ≥ 0 | |
| `ats_version` | `bigint` NOT NULL DEFAULT 0 | Monotonic CAS counter (R2) |
| `last_movement_at` | `timestamptz` | |

**Primary key**: `(variant_id, warehouse_id)`.
**Indexes**: `(warehouse_id, variant_id)` covering; partial index `(variant_id) WHERE on_hand − reserved ≤ 0` for out-of-stock dashboards.

---

## 3. `inventory.stock_movements` (append-only ledger)

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `variant_id` | `ulid` NOT NULL | |
| `warehouse_id` | `ulid` NOT NULL FK | |
| `batch_id` | `ulid` NULL FK → batches | |
| `quantity_delta` | `bigint` NOT NULL CHECK ≠ 0 | Signed |
| `movement_type` | `text` NOT NULL CHECK in (…see §3.1 of spec…) | |
| `reason_code` | `text` FK → adjustment_reason_codes(code) | Required for admin adjustments; nullable for system movements |
| `reference_type` | `text` NULL | `basket` / `order` / `transfer` / `grn` |
| `reference_id` | `ulid` NULL | |
| `correlation_id` | `ulid` NOT NULL | |
| `actor_id` | `ulid` NOT NULL | FK → identity subject |
| `recorded_at` | `timestamptz` NOT NULL DEFAULT `now()` | DB clock |
| `sequence_number` | `bigint` NOT NULL | Monotonic per (variant_id, warehouse_id); computed via sequence |
| `notes` | `text` NULL | |

**Indexes**: `(variant_id, warehouse_id, sequence_number)`, `(recorded_at)`, `(reference_type, reference_id)`, `(movement_type, recorded_at)`.

**No** `updated_at`, `updated_by` — append-only.

---

## 4. `inventory.reservations`

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `basket_id` | `ulid` NOT NULL | |
| `order_id` | `ulid` NULL | Populated post-commit |
| `market_code` | `text` NOT NULL | |
| `state` | `text` NOT NULL CHECK in (`soft_held`,`committed`,`fulfilled`,`released`,`expired`) | |
| `ttl_seconds` | `int` NOT NULL CHECK between 60 and 3600 | |
| `expires_at` | `timestamptz` NOT NULL | |
| `extended_count` | `int` NOT NULL DEFAULT 0 CHECK ≤ 1 | |
| `customer_id` | `ulid` NULL | |
| `correlation_id` | `ulid` NOT NULL | |
| `committed_at` / `fulfilled_at` / `released_at` / `expired_at` | `timestamptz` NULL | |

**Indexes**: `(state, expires_at)` for scanner; partial unique index `(basket_id) WHERE state='soft_held'`.

## 4a. `inventory.reservation_lines`

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `reservation_id` | `ulid` NOT NULL FK | |
| `variant_id` | `ulid` NOT NULL | |
| `warehouse_id` | `ulid` NOT NULL | |
| `quantity` | `int` NOT NULL CHECK > 0 | |
| `picked_batches` | `jsonb` NULL | `[{batchId, lotNumber, quantity}]`; populated at commit |

**Indexes**: `(reservation_id)`, `(variant_id, warehouse_id)`.

---

## 5. `inventory.batches`

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `variant_id` | `ulid` NOT NULL | |
| `warehouse_id` | `ulid` NOT NULL | |
| `lot_number` | `text` NOT NULL | |
| `lot_number_normalized` | `text` NOT NULL | case-fold + NFC |
| `manufacturer_batch_code` | `text` NULL | |
| `manufactured_at` | `date` NULL | |
| `expiry_at` | `date` NULL | |
| `received_qty` | `bigint` NOT NULL CHECK ≥ 0 | |
| `received_at` | `timestamptz` NOT NULL | |
| `supplier_id` | `ulid` NULL | |
| `state` | `text` NOT NULL CHECK in (`active`,`exhausted`,`expired`) DEFAULT `active` | |

**Constraints**: Partial unique `(variant_id, warehouse_id, lot_number_normalized) WHERE state <> 'expired'`. CHECK `expiry_at > manufactured_at` when both set.
**Indexes**: `(variant_id, warehouse_id, state, expiry_at)` for FEFO picker.

---

## 6. `inventory.adjustment_reason_codes`

| Column | Type | Notes |
|---|---|---|
| `code` | `text` PK | |
| `label_en` / `label_ar` | `text` NOT NULL | |
| `requires_evidence` | `bool` NOT NULL DEFAULT false | |

**Seed (§R11)**: `damaged`, `expired`, `theft`, `cycle_count`, `supplier_return`, `transfer`, `initial_receipt`.

---

## 7. `inventory.low_stock_notifications`

Dedup table for low-stock event emission (R7).

| Column | Type | Notes |
|---|---|---|
| `variant_id` | `ulid` NOT NULL | |
| `warehouse_id` | `ulid` NOT NULL | |
| `day_bucket` | `date` NOT NULL | |
| `emitted_at` | `timestamptz` NOT NULL | |

**Primary key**: `(variant_id, warehouse_id, day_bucket)`.

---

## 8. `inventory.transfer_headers` (intra-market transfers)

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `market_code` | `text` NOT NULL | |
| `source_warehouse_id` / `dest_warehouse_id` | `ulid` NOT NULL | CHECK source ≠ dest, same market |
| `state` | `text` CHECK in (`in_transit`,`completed`) | Launch: completed at creation |
| `reason_code` | `text` NOT NULL | |
| `reference` | `text` NULL | |

Transfer movements (`transfer_out`, `transfer_in`) both reference this `id` via `reference_id`.

---

## 9. DTOs (`packages/shared_contracts/inventory/`)

- `AvailabilityItem` — `{ variantId, marketCode, onHand?, ats, availabilityState, lowStockThreshold? (admin-only) }` — `onHand` only returned to admin callers.
- `AvailabilityBatchRequest` — `{ marketCode, variantIds[] }` (max 50).
- `BasketValidateRequest` — `{ marketCode, lines[{variantId, quantity}] }`.
- `BasketValidateResponse` — `{ lines[{variantId, requested, available, state, ok}] }`.
- `ReservationCreateRequest` — `{ basketId, marketCode, ttlSeconds?, customerId?, lines[{variantId, warehouseId?, quantity}] }`.
- `Reservation` — `{ id, basketId, state, ttlSeconds, expiresAt, extendedCount, lines[], committedAt?, fulfilledAt?, releasedAt?, expiredAt?, correlationId }`.
- `ReservationLine` — `{ variantId, warehouseId, quantity, pickedBatches? }`.
- `CommitResponse` — `{ reservationId, pickingGuidance[{variantId, picks[{batchId, lotNumber, quantity}]}], warnings[] }`.
- `StockMovementDTO` — ledger row shape for admin views.
- `AdjustmentRequest` — `{ warehouseId, variantId, batchId?, quantityDelta, reasonCode, evidenceRef?, notes? }`.
- `TransferRequest` — `{ marketCode, sourceWarehouseId, destWarehouseId, lines[{variantId, batchId?, quantity}], reasonCode, reference? }`.
- `ReturnRestockRequest` — `{ orderId, marketCode, lines[{variantId, warehouseId, batchId?, quantity, reason}] }`.
- `BatchDTO` / `BatchInput` — per §5.
- `ErrorEnvelope` — standard shape with `inventory.*` error codes.

---

## 10. Migrations

- `V008_001__create_inventory_schema.sql` — all 8 tables, constraints, indexes.
- `V008_002__seed_reason_codes_and_default_warehouses.sql` — 7 reason codes + 2 warehouses.
- `V008_003__snapshot_sequence.sql` — per-(variant, warehouse) sequence for `sequence_number`.
