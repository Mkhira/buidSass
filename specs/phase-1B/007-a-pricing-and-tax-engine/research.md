# Research — Pricing & Tax Engine v1 (Spec 007-a)

**Date**: 2026-04-22

## R1 — Centralized engine
**Decision**: single `IPriceCalculator` module, called by catalog (price-hint), cart (spec 009), checkout (spec 010), quotation (spec 011 quotations), invoice (spec 012), returns (spec 013).
**Rationale**: Principle 10 — pricing centralized and explainable. Avoids drift between storefront price and invoice total.
**Alternative**: in-line math per caller — rejected.

## R2 — Layer order
**Decision**: list → B2B tier → promotion → coupon → tax.
**Rationale**: Tier contracts supersede list but are themselves subject to scheduled promotions + coupons. Tax is always last so discounts apply to net.
**Alternative**: tax-inclusive math throughout — rejected because it obscures net/discount lines on the invoice.

## R3 — Rounding
**Decision**: Banker's (half-even) rounding at each layer boundary; self-assert totals.
**Rationale**: Prevents single-unit drift in large carts. Matches finance team expectation.
**Alternative**: Half-up — causes systematic upward bias; rejected.

## R4 — Storage of explanation
**Decision**: Store the `PriceExplanation` as a normalized JSON document plus an `explanation_hash` (SHA-256 of canonical JSON).
**Rationale**: Immutable audit per Principle 25; hash lets refund code verify no drift.
**Alternative**: Recompute at refund time — rejected (list price may have changed).

## R5 — Determinism
**Decision**: All time-sensitive inputs (promotion schedules) receive `ctx.nowUtc` explicitly; no `DateTime.UtcNow` inside the engine.
**Rationale**: Enables reproducible tests + stable hashes for quotes.

## R6 — Currency
**Decision**: Single currency per market (EGP/SAR); stored as integer minor units throughout.
**Rationale**: Phase 1 simplification; avoids decimal libraries.

## R7 — Coupon per-customer-limit
**Decision**: `coupon_redemptions` table with `(coupon_id, account_id)` unique; optimistic concurrency token on the coupon row for the overall limit.
**Rationale**: DB is the source of truth; prevents race conditions under concurrent redeems.

## R8 — Bundles
**Decision**: Bundles are SKUs authored in spec 005 with their own list price. Pricing engine doesn't compose components at runtime.
**Rationale**: Simpler math, simpler inventory decrement in spec 008.
**Alternative**: Virtual composite — rejected for Phase 1.

## R9 — BOGO implementation
**Decision**: Promotion rule `bogo` with `qualifying_sku`, `reward_sku`, `qualify_qty`, `reward_qty`, `reward_percent` (default 100).
**Rationale**: Fits "Buy 2 get 1 free" and "Buy 3 get 1 at 50 %" variants uniformly.

## R10 — Tax
**Decision**: Single `vat` kind at launch. Table `pricing.tax_rates` keyed by `(market, kind, effective_from)`.
**Rationale**: KSA 15 %, EG 14 %. Effective-from window supports rate changes with no data rewrite.

## R11 — Quotation caching
**Decision**: When `ctx.quotationId` is set and the quote is `active`, return the stored `PriceResult` without re-pricing.
**Rationale**: Quote price is a contract; list-price drift must not re-price the quote.

## R12 — Performance budget
**Decision**: p95 ≤ 40 ms for a 20-line cart (FR-018, SC-001).
**Rationale**: Storefront cart page budgets ~300 ms; 40 ms leaves headroom for IO.
