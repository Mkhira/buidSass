# Quickstart — Pricing & Tax Engine v1 (Spec 007-a)

## Prerequisites
- Branch `phase-1B-specs`.
- Specs 003, 004, 005 merged.

## 30-minute walk-through
1. **Primitives.** `IPriceCalculator`, `PricingContext`, `PriceResult`, `ExplanationRow`. Layer interfaces.
2. **Layers.** List → B2BTier → Promotion → Coupon → Tax. Each layer emits ExplanationRow per line.
3. **Rounding.** `BankersRounding.Round(minor, bps) => minor`. Applied at each layer boundary.
4. **Persistence.** 9 tables; migration `Pricing_Initial`. Seed EG 14 %, KSA 15 % VAT.
5. **Caches.** `TaxRateCache` + `PromotionCache` with 5-min TTL and in-proc invalidation.
6. **Customer slice.** `PriceCart` — validates + calls engine `Preview`.
7. **Admin slices.** CRUD for tax-rates/promotions/coupons/tiers/tier-prices; audit on every write.
8. **Internal endpoint.** `calculate` with `mode=issue` persists `price_explanations` immutably.
9. **Determinism tests.** Property-based; 10k carts × 2 calls → 0 hash mismatches (SC-002).
10. **Perf tests.** 20-line cart p95 ≤ 40 ms (SC-001).

## DoD
- [ ] 24 FRs → ≥ 1 contract test each.
- [ ] 8 SCs → measurable check.
- [ ] Explanation hash stable across runs.
- [ ] Tax-rate cache hit ≥ 99 % after warm-up.
- [ ] Coupon per-customer-limit holds under concurrent stress (SC-004).
- [ ] AR editorial pass on reason codes.
- [ ] Fingerprint + constitution check.
