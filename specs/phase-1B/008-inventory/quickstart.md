# Quickstart: Inventory (008)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Get the inventory module running locally, run the smoke walkthrough, and validate the Definition of Done.

---

## 1. Prerequisites

- .NET 9 SDK, Docker, `psql`, `k6`, `dotnet-ef`
- `docker compose up -d postgres` (seeded Postgres in `infra/local/`)
- Catalog (005) migrations applied (variants + `low_stock_threshold`, `accepts_preorder`, `preorder_floor` columns)
- Identity (004) running (bearer tokens for admin endpoints)

## 2. Bring-up

```bash
cd services/backend_api
dotnet ef database update --context InventoryDbContext        # V008_001..003
dotnet run --project Features/Inventory                         # or main host
```

Expected seeds:
- `inventory.warehouses`: `whs_ksa_01` (default KSA, Asia/Riyadh), `whs_eg_01` (default EG, Africa/Cairo)
- `inventory.adjustment_reason_codes`: 7 rows (damaged, expired, theft, cycle_count, supplier_return, transfer, initial_receipt)
- Per-(variant, warehouse) snapshot sequence for `sequence_number`

Hosted workers auto-start: `ReservationExpiryScanner` (30 s tick), `BatchExpirySweeper` (03:00 UTC).

## 3. Smoke walkthrough

### A. Availability — public read
```bash
curl "http://localhost:5000/inventory/availability?marketCode=ksa&variantIds=vrnt_a,vrnt_b"
```
Expect `AvailabilityItem[]` with `availabilityState` ∈ {in_stock, low_stock, out_of_stock, preorder}. `onHand` absent (public caller).

### B. Basket validate → reserve → commit → fulfil
```bash
curl -X POST .../inventory/basket/validate -d '{"marketCode":"ksa","lines":[{"variantId":"vrnt_a","quantity":2}]}'
curl -X POST .../inventory/reservations    -d '{"basketId":"bkt_1","marketCode":"ksa","lines":[{"variantId":"vrnt_a","quantity":2}]}'
# → 201 with state=soft_held, ttlSeconds=900, expiresAt=...
curl -X POST .../inventory/reservations/{id}/commit -d '{"orderId":"ord_1"}'
# → CommitResponse with FEFO pickingGuidance per line
curl -X POST .../inventory/reservations/{id}/fulfil
# → state=fulfilled; ledger now has shipment_out movements
```

### C. Admin adjustment
```bash
curl -X POST .../admin/inventory/adjustments -H 'Authorization: Bearer $ADMIN' \
  -d '{"warehouseId":"whs_ksa_01","variantId":"vrnt_a","quantityDelta":-3,"reasonCode":"damaged","notes":"crushed box"}'
```
Expect `StockMovementDTO`; `audit.events` gains `inventory.adjustment.posted`; `inventory.snapshot_changed` fires to search.

### D. Batch receipt + FEFO
```bash
curl -X POST .../admin/inventory/batches -H 'Authorization: Bearer $ADMIN' \
  -d '{"variantId":"vrnt_a","warehouseId":"whs_ksa_01","lotNumber":"LOT-A","expiryAt":"2027-01-31","receivedQty":50}'
```
Expect batch `state=active`; next commit picks this batch before later-expiry ones.

### E. Reservation expiry
Create a reservation with `ttlSeconds=60`; wait ~90 s; GET → `state=expired`. Ledger has no movement (soft-held never decremented on_hand).

### F. Low-stock event
Drop ATS below threshold via adjustment → observe `inventory.low_stock` event + `low_stock_notifications` row for today. Repeat same-day drop → no duplicate emission.

## 4. Tests

```bash
dotnet test Tests/Inventory.Unit
dotnet test Tests/Inventory.Properties      # FsCheck: replay determinism, CAS, FEFO
dotnet test Tests/Inventory.Integration     # Testcontainers Postgres; 100-concurrent reservations
dotnet test Tests/Inventory.Contract        # OpenAPI round-trip
k6 run perf/inventory_availability.js        # SC-002: p95 ≤ 120 ms
k6 run perf/inventory_reserve.js             # SC-003: p95 ≤ 250 ms at 20 lines
```

## 5. Definition of Done

- [ ] All 12 acceptance scenarios (US1–US8) pass
- [ ] All 10 success criteria (SC-001..SC-010) verified
- [ ] FsCheck properties: ledger replay determinism ≥ 10k trials; no over-allocation under 100k concurrent attempts
- [ ] k6: availability p95 ≤ 120 ms; reservation create p95 ≤ 250 ms @ 20 lines
- [ ] Reservation scanner p99 ≤ 60 s lag under load
- [ ] OpenAPI + events.md round-trip in contract tests
- [ ] Admin endpoints require `inventory.reserve.manage` / `inventory.adjust` policies
- [ ] Audit rows for every admin write + reservation transition
- [ ] Bilingual (ar/en) error messages for all `inventory.*` codes
- [ ] Low-stock dedup keyed by `(variant, warehouse, day_bucket)` — one event per key per day
- [ ] Batch expiry sweep evaluated at warehouse-local midnight (Asia/Riyadh, Africa/Cairo)
- [ ] Cross-market transfer returns `inventory.cross_market_transfer_unsupported`
- [ ] CSV ledger export streams chunked transfer; limit 1 M rows per call
- [ ] `correlationId` propagated end-to-end (HTTP → handler → DB → events → logs)
- [ ] Constitution gate check passes (Principles 11, 21, 24, 25, 27, 28, 29)

## 6. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `inventory.concurrent_write_conflict` frequent | Hot-SKU contention | Check `inventory_snapshot_cas_retries_total`; confirm single retry logic, consider warming cache |
| Reservations never expire | Scanner not running | `journalctl` for `ReservationExpiryScanner`; verify `IHostedService` registered |
| Batches expire at wrong local time | Sweeper using UTC | Confirm warehouse `time_zone` populated and NodaTime tz data loaded |
| `low_stock` fires twice same day | Dedup row race | Check unique `(variant_id, warehouse_id, day_bucket)` PK; ensure insert before publish |
| CSV export OOM | Buffering enabled | Ensure `application/csv` stream writer uses chunked transfer; drop result set size to ≤ 1 M |
