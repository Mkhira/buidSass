# Quickstart — Cart v1 (Spec 009)

## Prerequisites
- Branch `phase-1B-specs`.
- Specs 003, 004, 005, 007-a, 008 merged; spec 006 available for availability consumption.

## 30-minute walk-through
1. **Primitives.** `CartTokenProvider` (HMAC), `CartMerger`, `EligibilityEvaluator`.
2. **Persistence.** 5 tables, migration `Cart_Initial`.
3. **Customer slices.** Get, AddLine, UpdateLine, RemoveLine, Merge, ApplyCoupon, SaveForLater, B2BMetadata, SwitchMarket, Restore.
4. **Admin slices.** Inspect + abandoned list.
5. **Workers.** Abandonment emitter, guest cleanup, archive reaper.
6. **Hooks.** Spec 004 login → merge; spec 005 `catalog.product.archived` → flag lines; spec 008 `product.availability.changed` → stockChanged surfacing.
7. **Tests.** 100 merge scenarios, reservation lifecycle, coupon apply/remove, market switch + restore, abandonment dedup.
8. **AR editorial.**

## DoD
- [ ] 22 FRs → ≥ 1 contract test each.
- [ ] 8 SCs → measurable check.
- [ ] Merge correctness SC-003 green.
- [ ] Cart read p95 ≤ 120 ms under warm-cache load.
- [ ] Abandonment dedup SC-004 green.
- [ ] Fingerprint + constitution check.
