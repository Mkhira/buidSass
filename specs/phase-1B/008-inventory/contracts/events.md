# Inventory Events & Observability Contract (008)

**Date**: 2026-04-20 | **Spec**: [spec.md](../spec.md) | **Transport**: MediatR `INotification` in-process (R5); no outbox at Phase 1B.

All events carry: `eventId` (ULID), `eventName`, `occurredAt` (UTC), `correlationId`, `actorId`, `marketCode`, plus payload below.

---

## 1. Domain events published

### 1.1 `inventory.low_stock`
Emitted when a movement/reservation transitions a `(variant, warehouse)` from `ats > threshold` to `ats ≤ threshold`. Debounced via `low_stock_notifications` (R7) — at most one per day bucket per `(variant, warehouse)`.

```json
{
  "variantId": "01HX...",
  "warehouseId": "01HX...",
  "ats": 4,
  "threshold": 5,
  "marketCode": "ksa"
}
```

**Consumers**: `notifications` (025) → merchandiser email/push; `admin` dashboard refresh.

### 1.2 `inventory.stock_recovered`
Emitted when `ats` crosses back above threshold. Resets the dedup key for future `low_stock` emissions.

```json
{ "variantId": "...", "warehouseId": "...", "ats": 12, "threshold": 5, "marketCode": "ksa" }
```

### 1.3 `inventory.reservation_committed`
At order placement.

```json
{ "reservationId": "...", "orderId": "...", "basketId": "...", "lines": [{ "variantId": "...", "warehouseId": "...", "quantity": 2, "pickedBatches": [{"batchId":"...","lotNumber":"LOT-A","quantity":2}] }] }
```

**Consumers**: `orders` (011), `search` (006) (ATS re-index), `analytics`.

### 1.4 `inventory.reservation_released`
Customer or admin-initiated release.

```json
{ "reservationId": "...", "basketId": "...", "reasonCode": "customer_abandoned", "releasedBy": "system|admin:<id>" }
```

### 1.5 `inventory.reservation_expired`
Scanner-driven expiry.

```json
{ "reservationId": "...", "basketId": "...", "expiredAt": "2026-04-20T17:30:00Z" }
```

### 1.6 `inventory.batch_expired`
Sweeper (03:00 UTC, warehouse-local evaluation).

```json
{ "batchId": "...", "variantId": "...", "warehouseId": "...", "lotNumber": "LOT-A", "expiryAt": "2026-04-20", "remainingQty": 8 }
```

**Consumers**: `notifications` (025) → ops team; `admin` dashboard.

### 1.7 `inventory.adjustment_posted`
Admin wrote a manual adjustment (damaged/theft/cycle_count/…).

```json
{ "movementId": "...", "variantId": "...", "warehouseId": "...", "quantityDelta": -3, "reasonCode": "damaged", "notes": "crushed package", "evidenceRef": "s3://…" }
```

### 1.8 `inventory.transfer_posted`
Intra-market transfer committed.

```json
{ "transferId": "...", "sourceWarehouseId": "...", "destWarehouseId": "...", "lineCount": 4 }
```

### 1.9 `inventory.snapshot_changed` (internal)
Fired on every snapshot update. Handlers: `search` (006) ATS re-index, availability cache bump.

```json
{ "variantId": "...", "warehouseId": "...", "onHand": 42, "reserved": 3, "ats": 39, "atsVersion": 1240 }
```

---

## 2. Audit actions (Principle 25)

Written to `audit.events` by a dedicated `AuditWriter` handler on the same MediatR pipeline. Every admin write + every reservation state transition is audited.

| Action | Trigger | Actor | Diff fields |
|---|---|---|---|
| `inventory.adjustment.posted` | POST /admin/inventory/adjustments | admin | variantId, warehouseId, delta, reasonCode, evidenceRef |
| `inventory.transfer.posted` | POST /admin/inventory/transfers | admin | source, dest, lines |
| `inventory.batch.received` | POST /admin/inventory/batches | admin | batchId, lotNumber, qty, expiry |
| `inventory.batch.updated` | PATCH /admin/inventory/batches/{id} | admin | prev/next values |
| `inventory.threshold.updated` | PUT /admin/inventory/thresholds | admin | variantId, old, new |
| `inventory.reservation.force_released` | POST /admin/…/force-release | admin | reservationId, reasonCode, notes |
| `inventory.reservation.state_changed` | state machine transition | system/admin | from, to |

Retention: 7 years (matches finance/audit policy).

---

## 3. Observability signals

### 3.1 Logs (structured, one line per write; aggregated for reads — R13)

Fields on every reservation/movement write: `inv.market`, `inv.warehouse`, `inv.variant_hash` (SHA-256 of variantId), `inv.operation`, `inv.quantity_delta`, `inv.ats_before`, `inv.ats_after`, `inv.correlation_id`, `inv.latency_ms`, `inv.retry_count`.

### 3.2 Metrics (OpenTelemetry)

| Metric | Type | Labels |
|---|---|---|
| `inventory_availability_latency_ms` | histogram | marketCode, batch_size_bucket |
| `inventory_reservation_create_latency_ms` | histogram | marketCode, line_count_bucket |
| `inventory_reservation_conflict_total` | counter | marketCode, conflict_type |
| `inventory_snapshot_cas_retries_total` | counter | marketCode |
| `inventory_reservation_expiry_lag_ms` | histogram | marketCode |
| `inventory_low_stock_events_total` | counter | marketCode |
| `inventory_batch_expired_total` | counter | marketCode |
| `inventory_oversell_attempt_total` | counter | marketCode |
| `inventory_ledger_write_latency_ms` | histogram | marketCode, movement_type |

### 3.3 Traces

Spans: `inventory.availability.batch_lookup`, `inventory.reservation.create`, `inventory.reservation.commit`, `inventory.movement.write`, `inventory.batch_picker.fefo`, `inventory.expiry_scanner.tick`, `inventory.batch_sweeper.tick`. All tagged with `correlationId`.

### 3.4 Alerts

- `inventory_snapshot_cas_retries_total` sustained > 50/min → contention alert.
- `inventory_reservation_expiry_lag_ms p99 > 60000` → SC-005 breach.
- `inventory_oversell_attempt_total > 0` in any 5-minute window → SC-001 breach (page oncall).
- `inventory_availability_latency_ms p95 > 120` sustained 10 min → SC-002 breach.
