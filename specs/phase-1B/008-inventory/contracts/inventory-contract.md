# HTTP Contract — Inventory v1 (Spec 008)

**Base**: `/v1/`. Errors: RFC 7807 + `reasonCode`.

## Customer (read)
### GET /v1/customer/inventory/availability?productIds=id1,id2&market=ksa
Batch endpoint. Response: `{ "items": [{ productId, bucket: "in_stock"|"backorder"|"out_of_stock" }] }`.

## Internal (service-to-service, spec 004 S2S token)
### POST /v1/internal/inventory/reservations
Request: `{ cartId, marketCode, items: [{ productId, qty }] }`.
Response: `{ reservationId, items: [{ productId, qty, pickedBatchId, expiresAt }] }`.
Errors: `409 inventory.insufficient` with `{ shortfallByProduct }`.

### PATCH /v1/internal/inventory/reservations/{id}
Adjust live reservation. Request: `{ items?: [{ productId, qty }], extendTtlSeconds? }`. Called by spec 009 on cart line add/update and by spec 010 on session extension / pricing-drift accept. Idempotent on `(reservationId, sha256(payload))`.
Response: updated reservation DTO (same shape as create). Errors: `409 inventory.insufficient`, `409 inventory.reservation.expired`, `409 inventory.reservation.already_converted`.

### DELETE /v1/internal/inventory/reservations/{id}
Explicit release (cart abandon, checkout timeout).

### POST /v1/internal/inventory/reservations/{id}/convert
Called by spec 011 order-confirm. Atomic.

### POST /v1/internal/inventory/movements/return
Request: `{ orderId, items: [{ productId, qty }], reasonCode }`.
Spec 013 entry point.

## Admin
### Stocks
- `GET /v1/admin/inventory/stocks?productId=&warehouse=&bucket=&page=&pageSize=` — permission `inventory.read`.
- `PATCH /v1/admin/inventory/stocks/{productId}/{warehouseId}` — set `safety_stock` / `reorder_threshold`. Permission `inventory.write`.

### Batches
- `GET /v1/admin/inventory/batches?productId=&warehouse=&status=` — `inventory.read`.
- `POST /v1/admin/inventory/batches` — create/receipt. Body: `{ productId, warehouseId, lotNo, expiryDate, qty, notes? }`. Writes `kind=receipt`. Permission `inventory.write`.
- `PATCH /v1/admin/inventory/batches/{id}` — correct metadata only (qty changes go through movements).

### Movements
- `GET /v1/admin/inventory/movements?productId=&warehouse=&kind=&from=&to=&page=&pageSize=` — `inventory.read`.
- `POST /v1/admin/inventory/movements/adjust` — `{ productId, warehouseId, batchId?, delta, reason }`. Permission `inventory.write`. Blocks negative on_hand.
- `POST /v1/admin/inventory/movements/transfer` — `{ productId, fromWarehouseId, toWarehouseId, qty, batchId? }`. Writes paired `transfer_out` + `transfer_in` atomically.
- `POST /v1/admin/inventory/movements/writeoff` — `{ productId, warehouseId, batchId, qty, reason }`.

### Dashboards
- `GET /v1/admin/inventory/alerts?kind=low_stock|expiring_soon` — lists current alerts.

## Reason codes
`inventory.insufficient`, `inventory.warehouse_market_mismatch`, `inventory.batch_qty_negative`, `inventory.negative_on_hand_blocked`, `inventory.reservation.not_found`, `inventory.reservation.expired`, `inventory.reservation.already_converted`, `inventory.batch.duplicate_lot`.

## Events (published)
- `inventory.reservation.created|released|converted`
- `inventory.movement.posted`
- `inventory.reorder_threshold_crossed`
- `inventory.batch_expired`
- `product.availability.changed`
