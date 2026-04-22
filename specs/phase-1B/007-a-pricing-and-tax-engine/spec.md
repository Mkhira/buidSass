# Feature Specification: Pricing & Tax Engine (v1)

**Feature Number**: `007-a-pricing-and-tax-engine`
**Phase Assignment**: Phase 1B · Milestone 3 · Lane A (backend)
**Created**: 2026-04-22
**Input**: `docs/implementation-plan.md` §007-a; constitution Principles 5, 6, 9, 10, 18, 22, 23, 24, 25, 27, 28, 29.

---

## Clarifications

### Session 2026-04-22

- Q1: Where does pricing live? → **A: A dedicated `Pricing` module with a single `IPriceCalculator.Calculate(ctx)` entry point.** All surfaces (catalog price-hint hydration, cart pricing, checkout final pricing, quotes, invoices) call the same engine — no parallel implementations.
- Q2: Tax handling at launch? → **B: VAT-inclusive display with line-level breakdown stored.** EG 14 % VAT, KSA 15 % VAT. Display "includes VAT" line; invoice (spec 012) reads the stored breakdown.
- Q3: Rule precedence when coupon + B2B tier + promotion overlap? → **C: Layered pipeline with explicit ordering:** (1) list price → (2) B2B tier override → (3) scheduled promotion (BOGO / bundle / % off) → (4) coupon → (5) VAT. Each layer records its contribution in a `PriceExplanation` for auditability (Principle 10).
- Q4: Bundle / BOGO semantics? → **A: Bundles are explicit SKUs**. BOGO is a promotion rule referencing qualifying SKUs + reward SKU. No "virtual" composite products at the storefront.
- Q5: Rounding policy? → **B: Half-even (banker's) rounding to minor units (fils/piaster) at each layer boundary.** Documented because coupon math over line items needs it to avoid off-by-1.

---

## User Scenarios & Testing

### User Story 1 — Consumer sees a transparent price (P1)
A shopper opens a product in KSA-AR. They see the price `SAR 115.00 (incl. VAT)` and a breakdown on tap: `Net 100.00 + VAT 15 %`.

**Acceptance Scenarios**:
1. *Given* a product with list price 100.00 SAR, *when* the storefront hydrates the PDP, *then* the display shows `115.00 incl. VAT` and an expandable `net + tax` panel.
2. *Given* an EG market, *when* a product is priced at 100.00 EGP, *then* display shows `114.00 incl. VAT`.
3. *Given* a restricted product, *when* price is shown, *then* display is unchanged (Principle 8 — price always visible).

---

### User Story 2 — B2B tier pricing (P1)
A clinic account qualifying for "Tier 2" sees a 10 % tier discount applied automatically on every qualifying SKU.

**Acceptance Scenarios**:
1. *Given* tier 2 account + product with tier-2 override 90.00, *when* the cart is priced, *then* the net line is 90.00 (layer 2 applied), VAT recalculated on top.
2. *Given* tier 2 account + product with no tier override, *then* list price is used with no discount.
3. *Given* the same cart priced twice (idempotency), *then* the `PriceExplanation` is byte-identical.

---

### User Story 3 — Coupon applied at cart (P1)
A customer types coupon `WELCOME10` (10 % off order ≤ cap SAR 50). Engine applies it after promotions, before VAT, caps at 50, records explanation.

**Acceptance Scenarios**:
1. *Given* a valid coupon and qualifying cart, *when* engine runs, *then* the coupon layer records `type=percent`, `value=10`, `cap=5000` (minor units), `appliedAmount` computed per line pro-rata.
2. *Given* an expired coupon, *then* engine returns `pricing.coupon.expired`, cart subtotal unchanged.
3. *Given* a coupon that excludes restricted products, *when* cart has a restricted line, *then* only the non-restricted lines receive the discount.

---

### User Story 4 — Scheduled promotion + coupon stack (P2)
A "Buy 2 get 1 free" promo is live. Customer buys 3 gloves. Engine marks one line as free (0 net) via promotion layer, then a 10 % coupon applies to remaining lines.

**Acceptance Scenarios**:
1. *Given* a BOGO promotion active, *when* 3 qualifying units are in cart, *then* one line's `appliedAmount` = full net (free), explanation `promotion.bogo.free_item`.
2. *Given* the coupon layer runs after, *then* it discounts only the paid lines.

---

### User Story 5 — Quotation snapshot (P2)
A B2B buyer asks for a quotation. Engine produces a `PriceExplanation` stored on the quote. When the quote is accepted 5 days later, the stored explanation — not re-priced — turns into the order.

**Acceptance Scenarios**:
1. *Given* a quote issued at 09:00, *when* accepted 5 days later with no list price change, *then* the order uses the exact stored explanation.
2. *Given* the list price has changed in the interim, *then* the stored explanation still wins; the price change log is surfaced to the admin for awareness but does not re-price the quote.

---

### User Story 6 — Admin inspects why a customer paid what they paid (P2)
Support pulls an order → "Price explanation" tab. They see each layer, each rule id, each line contribution, and a running subtotal.

**Acceptance Scenarios**:
1. *Given* an order, *when* support opens the explanation, *then* 5 layer rows render (list, tier, promotion, coupon, VAT) each with its rule id + amount.
2. *Given* a refund is later approved, *when* the refund tab opens, *then* the explanation is replayed showing which layers still apply.

---

### Edge Cases
1. Zero-price line (free sample) → engine emits the line with `net=0`, VAT=0, no promotion/coupon.
2. Currency mismatch (EG product in KSA cart) → `400 pricing.currency_mismatch`; impossible via market scoping but guarded anyway.
3. Missing tax rate for a market → `500 pricing.tax_rate_missing`; operator alert.
4. Rounding drift > 1 minor unit across a 20-line cart → engine self-asserts totals match sum of line contributions; test.
5. Coupon cap reached with fractional remainder → rounds half-even to minor units; excess is not redistributed.
6. Two coupons attempted at once → `409 pricing.coupon.already_applied`.
7. Promotion schedule boundary (promo ends at 17:00:00 UTC) → engine uses `nowUtc` from the caller; freeze deterministic in tests.
8. Price explanation persistence: must be immutable once written for a quote/order (Principle 25 audit).
9. Idempotency: same inputs → same explanation bytes. Required for refund computations months later.
10. Negative total (theoretical) → clamped at 0 with explanation row `pricing.floor.clamped_to_zero`.

---

## Requirements (FR-)
- **FR-001**: System MUST expose `IPriceCalculator.Calculate(PricingContext): PriceResult` as the single entry point.
- **FR-002**: `PricingContext` MUST include: `marketCode`, `locale`, `accountContext (tier, verificationState)`, `lines[{productId, qty, listPriceMinor, restricted}]`, `couponCode?`, `quotationId?`, `nowUtc`.
- **FR-003**: `PriceResult` MUST include: `lines[{productId, qty, netMinor, taxMinor, grossMinor, explanation[]}]`, `totals{ subtotalMinor, discountMinor, taxMinor, grandTotalMinor }`, `explanationHash` (sha256 of canonical JSON).
- **FR-004**: Engine MUST run layers in fixed order: (1) list → (2) B2B tier → (3) promotion → (4) coupon → (5) tax.
- **FR-005**: Each layer MUST emit one explanation row per affected line with `layer`, `ruleId`, `ruleKind`, `appliedMinor`, `reasonCode?`.
- **FR-006**: Engine MUST use half-even rounding to minor units at each layer boundary; MUST self-assert totals match sum of line contributions (fail-fast on mismatch).
- **FR-007**: Tax rates MUST live in a `pricing.tax_rates` table keyed by `(market_code, tax_kind, effective_from)`; EG 14 %, KSA 15 % seeded.
- **FR-008**: Promotions MUST live in a `pricing.promotions` table: types `percent_off`, `amount_off`, `bogo`, `bundle`; with `applies_to[]` SKU list; `schedule{from, to}`; `priority`.
- **FR-009**: Coupons MUST live in a `pricing.coupons` table: code, type, value, cap, per-customer-limit, overall-limit, `excludes_restricted` flag, `valid_from`, `valid_to`.
- **FR-010**: B2B tiers MUST live in `pricing.b2b_tiers` and `pricing.product_tier_prices`; the account's current tier is read from spec 004 account context.
- **FR-011**: Engine MUST be deterministic: same inputs → same `explanationHash`.
- **FR-012**: Engine MUST support a `Preview` mode (no side effects) and an `Issue` mode (records the explanation to `pricing.price_explanations`, immutable).
- **FR-013**: Admin endpoints MUST support CRUD for promotions, coupons, tax rates (audit-logged per Principle 25).
- **FR-014**: Customer endpoint MUST allow re-pricing a cart `POST /v1/customer/pricing/price-cart` with full transparency.
- **FR-015**: Coupon codes MUST be case-insensitive match, canonicalized to uppercase on insert.
- **FR-016**: Restricted products MUST still surface a price (Principle 8); engine does not hide them.
- **FR-017**: Bundle prices MUST be set at the bundle SKU level (not computed from components); components carry their own price-hint for catalog display.
- **FR-018**: Engine MUST expose a metric `pricing_calculate_duration_ms` p95 ≤ 40 ms for a 20-line cart.
- **FR-019**: Every admin price/promotion/coupon mutation MUST write an audit row (Principle 25).
- **FR-020**: Engine MUST be callable from within a DB transaction (catalog restriction check + pricing is atomic under checkout in spec 010).
- **FR-021**: All monetary values MUST be stored and transported as integer minor units (no floating point).
- **FR-022**: Engine MUST expose `ComputeHash(explanation)` for external verification (refund/invoice pipelines).
- **FR-023**: Coupon per-customer-limit MUST be enforced via spec 004 account id; counter table with optimistic locking.
- **FR-024**: Engine MUST accept an optional `quotationId`; when present and status = `active`, the stored explanation is returned verbatim (not recalculated).

### Key Entities
- **PricingContext** / **PriceResult** / **ExplanationRow** — in-memory shapes.
- **TaxRate**, **Promotion**, **Coupon**, **B2BTier**, **ProductTierPrice**, **PriceExplanation** (persisted), **CouponRedemption** — DB tables.

---

## Success Criteria (SC-)
- **SC-001**: `IPriceCalculator.Calculate` p95 ≤ 40 ms for a 20-line cart (FR-018).
- **SC-002**: Determinism: 10000 random carts re-priced twice → 0 explanation-hash mismatches.
- **SC-003**: Rounding drift across 20-line cart ≤ 0 minor units (totals equal sum of line contributions).
- **SC-004**: Coupon per-customer-limit enforced at 100 % under a 100-concurrent-redeem stress test.
- **SC-005**: Tax rate lookup hits an in-proc 5 min TTL cache with ≥ 99 % hit rate after warm-up.
- **SC-006**: Every admin mutation leaves an audit row with actor + before/after JSON.
- **SC-007**: Quote acceptance after 5 days re-uses stored explanation byte-identically (SC-002 corollary).
- **SC-008**: BOGO promotion on a 3-unit cart produces exactly one free line regardless of line order.

---

## Dependencies
- Spec 004 — account context (tier, verificationState).
- Spec 005 — product list price + restricted flag + market scoping.
- Spec 003 — audit log, MessageFormat.NET.
- Storage/DB per ADR-004.

## Assumptions
- One tax kind (`vat`) at launch; future kinds (e.g., KSA excise) slot into the same table.
- Currency per market is fixed (EG→EGP, KSA→SAR).
- Bundles are launched as dedicated SKUs authored in spec 005.
- Multi-currency not required in Phase 1.

## Out of Scope
- Per-warehouse pricing (Phase 1.5).
- Per-vendor commission math (Phase 2; multi-vendor-ready only structurally).
- Personalized dynamic pricing (Phase 2).
- Tax at line-item kind variation within one cart (single kind per market at launch).
