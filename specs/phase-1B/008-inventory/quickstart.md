# Quickstart — Inventory v1 (Spec 008)

## Prerequisites
- Branch `phase-1B-specs`.
- Specs 003, 004, 005 merged; spec 006 (search) available for event consumption.

## 30-minute walk-through
1. **Primitives.** `AtsCalculator`, `FefoPicker`, `BucketMapper`.
2. **Persistence.** 6 tables; migration `Inventory_Initial`; seed `eg-main` + `ksa-main` warehouses.
3. **Reservation ops.** `CreateReservation` with `SELECT FOR UPDATE`; `ReleaseReservation`; `ConvertReservation`.
4. **Movement ops.** Receipt, adjustment, transfer (paired), writeoff.
5. **Workers.** `ReservationReleaseWorker` (30 s), `ExpiryWriteoffWorker` (daily), `AvailabilityEventEmitter` (on stock update).
6. **Events.** Wire `product.availability.changed` publisher; spec 006 indexer subscribes.
7. **Admin surface.** Stock grid, batch CRUD, movements ledger, adjustment + transfer + writeoff, dashboard alerts.
8. **Customer availability.** Bucket-only batch endpoint.
9. **Tests.** Concurrency SC-002, FEFO SC-005, TTL SC-003, bucket propagation SC-008.
10. **AR editorial on reason codes.**

## DoD
- [ ] 23 FRs → ≥ 1 contract test each.
- [ ] 9 SCs → measurable check.
- [ ] 100-concurrent-reservation test green (SC-002).
- [ ] FEFO fuzz test green (SC-005).
- [ ] Bucket change propagates to search in ≤ 10 s (SC-008).
- [ ] AR editorial pass on `inventory.ar.icu`.
- [ ] Fingerprint + constitution check.
