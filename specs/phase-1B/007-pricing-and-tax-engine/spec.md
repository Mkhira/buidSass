# Feature Specification: Pricing & Tax Engine (007-a)

**Phase**: 1B · **Stage**: 3.4 (Pricing & Tax) · **Created**: 2026-04-20
**Depends on**: 005 (catalog) · **Consumed by**: 008 (inventory), 009 (cart), 010 (checkout), 011 (orders), 012 (tax-invoices), 006 (search — priceToken resolution)
**Defers to**: 007-b (promotions UX + coupon authoring + campaigns, Phase 1D)

> Engine-only surface: resolves prices, applies promotion primitives, computes VAT, and emits an auditable breakdown. All authoring UIs (coupon console, campaign scheduler, business-pricing editor) are explicitly out of scope and land in 007-b.

---

## 1. Goal

Deliver a single, deterministic pricing and tax engine that every commerce surface (search, product detail, cart, checkout, quotes, orders, invoices) calls to obtain a fully-explained price. The engine MUST:

1. Resolve the price of a variant for a given customer context through a strict, auditable pipeline.
2. Apply promotion primitives (percentage, fixed, BOGO, bundle, tier) with explicit stacking and exclusion rules.
3. Validate coupons against eligibility, usage caps, and schedule windows.
4. Compute VAT per market (KSA 15%, EG configurable) with inclusive/exclusive handling per market policy.
5. Return a line-level and basket-level breakdown that downstream consumers can display, audit, and reconcile.
6. Be callable by `priceToken` (from search, spec 006) or by explicit basket payload (cart/checkout).

This satisfies constitution Principles **10** (centralised pricing), **18** (tax/invoice), **3** (B2B first-class), and **25** (audit on price changes).

---

## 2. User Roles

| Role | Engine interaction |
|---|---|
| Unauthenticated customer | Resolves list price + market VAT display; no business/tier pricing; no restricted-product purchase totals |
| Authenticated retail customer | As above, plus customer-specific active promos and applied coupons |
| Authenticated B2B buyer | Triggers business/tier pricing lookup; resolves PO-context totals; can see net + tax breakdown required for tax-invoice workflow |
| Admin (`pricing.read`) | Reads any customer-context breakdown for support/audit |
| Admin (`pricing.write.business`, `pricing.write.promo`, `pricing.write.coupon`) | Maintains pricing data via admin APIs (engine-layer CRUD only; editor UX is 007-b) |
| Finance (`pricing.audit`) | Exports price-change audit log + reconciliation reports |

---

## 3. Business Rules

### 3.1 Price Resolution Pipeline

Resolution **always** proceeds in this order; skipping a stage is non-compliant. Each stage writes a breakdown entry.

1. **Base price** — `ProductVariant.base_price` in market currency.
2. **Compare-at (MSRP) capture** — if set, stored in the breakdown for UX (`strike-through display`); not a resolution input.
3. **Business pricing override** — if customer has a `company_id` and a matching `business_pricing` row exists for the variant or its category, replace the working price.
4. **Tier pricing override** — if basket quantity ≥ a tier threshold on the variant/category, replace the working price with the tier price (highest threshold ≤ qty wins).
5. **Active promotion** — evaluate promotions in `priority ASC` order. First promotion with `exclusion_flag=false` whose eligibility matches contributes a `promo_discount` breakdown entry. Subsequent promotions with `stackable=true` additionally contribute until the first `stackable=false` is applied. Promotions with `exclusion_flag=true` prevent any further promo or coupon application at that line.
6. **Coupon application** — at most one coupon per basket (Phase 1B). Validated against: `active_window`, `usage_cap_remaining`, `min_basket_amount`, `eligible_markets`, `eligible_customer_segments`, `eligible_product_predicate`, and any line-level `exclusion_flag` set in stage 5. Result is an additional discount line.
7. **Net pre-tax total** — sum of line nets after all discounts.
8. **Tax computation** — per-line VAT applied using the market's tax rule set (see 3.3). Result added as `tax` breakdown entries.
9. **Final total** — net + tax; currency-rounded per market policy (banker's rounding at subtotal; see 3.4).

### 3.2 Promotion Primitives

| Primitive | Effect on line/basket | Stacking default |
|---|---|---|
| `percentage` | Multiply line subtotal by `(1 − p)` where `p ∈ [0,1]` | `stackable=true` unless authored otherwise |
| `fixed` | Subtract fixed amount from line or basket (bounded at 0) | `stackable=true` |
| `bogo` | Buy N get M free of same variant or variant group | `stackable=false` by default |
| `bundle` | Price for a specified multi-variant set when all components present at min qty | `stackable=false` by default |
| `tier` | Quantity-break price replacement (already handled in stage 4) | Not a promo; authored in tier table |

Each promotion carries: `id`, `type`, `priority` (int, lower first), `stackable` (bool), `exclusion_flag` (bool), `active_from`, `active_to`, `markets[]`, `eligibility_predicate` (customer-segment + category + brand + variant), `usage_caps` (per-customer, global).

### 3.3 Tax (VAT) Rules

- **KSA**: 15% standard rate. `display_mode = exclusive` on customer-facing pages; invoice MUST show both net and tax lines.
- **EG**: Configurable per market configuration (default 14% at launch, overridable without code change). `display_mode = exclusive` on customer-facing pages.
- Zero-rated / exempt products carry a `tax_class` on the variant; engine respects the class when computing per-line VAT.
- Invoice-level rounding: per-line VAT rounded to 2 decimals (market minor unit); basket VAT = sum of line VATs (avoid recomputing from net to prevent off-by-one cent drift).
- **Price token resolution** (from 006 search): caller passes token → engine resolves the same pipeline with the caller's authenticated context → returns breakdown.

### 3.4 Currency & Rounding

- All monetary values stored as integer **minor units** (halalas for SAR, piastres for EGP) in the database and in internal computation.
- Display rounding: banker's rounding (round-half-to-even) at every stored boundary; display layer converts minor units to decimal.
- Percentage discounts computed on minor units; fractional residuals drop to rounding policy — never silently truncated.
- Every breakdown entry carries both `minor_units` and `decimal_display` fields; clients MUST NOT re-derive display from minor units with their own rounding.

### 3.5 Restricted Products

- Restricted products (per spec 005) are priced normally by the engine. The engine does **not** gate purchase eligibility; that is the cart/checkout responsibility. Price and breakdown remain visible at all times (Principle 8).

### 3.6 B2B & PO Context

- If caller context includes `company_id`, the engine MUST attempt business-pricing lookup **before** tier pricing. Business pricing and tier pricing do **not** stack — whichever matches first in the pipeline wins, and the other stage is skipped with a `skipped=business_pricing_applied` or `skipped=tier_pricing_applied` breakdown entry.
- `purchase_order_reference` is carried through the breakdown for audit but has no pricing effect.

### 3.7 Audit

- Every write to `promotions`, `coupons`, `business_pricing`, `tier_pricing`, `tax_rules` emits an `audit_events` row with actor, before/after snapshot, correlation_id, market_code.
- Every price resolution is **not** audited (high volume); instead, resolution logs stream to observability with a hashed customer id, basket hash, and resolved_total (FR-020). Sampled replays allow reconstruction of any historical resolution via the pricing snapshot.

### 3.8 Price Token (integration with 006)

- Engine exposes `POST /pricing/resolve-token` that accepts a search-issued `priceToken` and the caller's auth context, and returns a single-line breakdown for that variant.
- Tokens are short-lived (TTL 120 s), signed (HMAC with rotating key), and carry only: variant_id, market_code, issued_at. They do **not** embed price — price is always re-resolved.

---

## 4. Primary User Flow

### Flow A — Cart re-price (authenticated retail customer, KSA)

1. Cart service calls `POST /pricing/resolve-basket` with `{ basket_id, lines[], customer_id, market=ksa }`.
2. Engine loads customer segment + company linkage.
3. For each line: runs pipeline stages 1–7.
4. Engine applies basket-scope coupon if provided.
5. Engine computes VAT at stage 8.
6. Engine returns `BasketPricingBreakdown` with line-level and basket-level entries, including `total_minor_units`, `total_display`, `tax_minor_units`, a stacking trace per line, and a `breakdown_correlation_id`.

### Flow B — Search hit price-token resolve

1. Search hit carries `priceToken`.
2. Product detail calls `POST /pricing/resolve-token` with `{ priceToken, customer_context }`.
3. Engine verifies signature + TTL; re-resolves pipeline for that single variant.
4. Returns `LinePricingBreakdown`.

### Flow C — Admin business-pricing upsert

1. Admin calls `PUT /admin/pricing/business/{companyId}/{variantId}` with new price and active window.
2. Engine validates RBAC (`pricing.write.business`), persists, emits audit event.
3. Next cart resolution for that company picks up new price.

---

## 5. UI States (client expectations — owned by 014 / admin UI)

- **Loading** — while `resolve-basket` or `resolve-token` inflight, clients show a skeleton price; never cache stale price across sessions without re-resolving.
- **Success** — full breakdown rendered (line total, discounts shown with strike-through compare-at if set, VAT summary).
- **Partial eligibility** — if a coupon was submitted but failed validation, engine returns `appliedCoupon: null` with `couponValidationErrors[]` carrying bilingual reason codes.
- **Stale token** — if `priceToken` expired, engine returns `pricing.token_expired` error envelope; client refreshes from search.
- **Restricted** — engine returns price normally; UI overlays "verification required to purchase" (gated by 004, not by engine).
- **Error** — bilingual error envelope (FR-021) with correlation id.

---

## 6. Data Model (logical — detailed physical layout lives in plan)

### 6.1 Key Entities

- **PromotionRule** — `id`, `type`, `priority`, `stackable`, `exclusion_flag`, `active_from`, `active_to`, `markets[]`, `value_minor_units` (for percentage/fixed), `rule_payload` (JSON for BOGO/bundle parameters), `eligibility_predicate` (structured JSON), `usage_cap_global`, `usage_cap_per_customer`, `state` (`draft`, `scheduled`, `active`, `paused`, `expired`).
- **Coupon** — `code` (case-insensitive unique per market), `promotion_rule_id` (FK — a coupon is a gated promotion), `usage_cap_total`, `usage_cap_per_customer`, `min_basket_amount`, `eligible_customer_segments[]`, `single_use_per_customer` (bool), `active_from`, `active_to`, `state`.
- **BusinessPricingEntry** — `company_id`, `variant_id` (nullable) OR `category_id` (nullable — one of variant/category required), `price_minor_units`, `active_from`, `active_to`, `currency`.
- **TierPricingEntry** — `variant_id` OR `category_id`, `min_quantity`, `price_minor_units`, `market_code`, `active_from`, `active_to`.
- **TaxRule** — `market_code`, `tax_class`, `rate_basis_points` (e.g., 1500 = 15.00%), `display_mode` (`inclusive`|`exclusive`), `active_from`, `active_to`.
- **PricingBreakdown** (response-only, not persisted) — line and basket level entries with stage traces.
- **PriceTokenClaim** — `variant_id`, `market_code`, `issued_at`, `ttl_seconds`, `signature`.
- **CouponRedemption** — `coupon_id`, `customer_id`, `basket_id`, `redeemed_at`, `reversed_at?` — written at checkout-success (called by 010), used for usage-cap accounting.
- **PricingSnapshot** — `order_id`, `breakdown_json`, `captured_at` — written by 011 on order placement; engine provides the payload to capture.

### 6.2 State Machines

**Promotion**: `draft → scheduled → active → paused | expired`. Only `draft → scheduled`, `scheduled → active`, `active → paused`, `paused → active`, `active → expired`, `scheduled → expired` allowed. Pausing is instantaneous and halts new applications; in-flight baskets already repriced retain their discount until re-resolved.

**Coupon**: mirrors Promotion plus `exhausted` when `usage_cap_total` reached.

---

## 7. Validation Rules

- `percentage.value` in range (0, 1] with 4-decimal precision.
- `fixed.value_minor_units ≥ 1`.
- `active_from < active_to` when both set.
- `usage_cap_*` ≥ 1 when set.
- `tax_rule.rate_basis_points ≥ 0 and ≤ 10000` (0% – 100%).
- `priority` integer 0..999; duplicates allowed but resolution order ties broken by `id` ASC.
- A single variant MUST NOT have overlapping active BusinessPricing entries for the same company.
- A single variant MUST NOT have overlapping active TierPricing tiers with identical `min_quantity` for the same market.
- Coupon `code` NFC-normalized, case-folded to uppercase before uniqueness check (Arabic + Latin).
- Eligibility predicate JSON validates against a published schema at write time; malformed predicates are rejected.

---

## 8. API / Service Requirements

Customer surfaces (authenticated or anonymous):
- `POST /pricing/resolve-basket` — basket-scope resolution.
- `POST /pricing/resolve-token` — single-variant resolution via search-issued token.
- `POST /pricing/validate-coupon` — dry-run coupon check for UX feedback without committing redemption.

Admin surfaces (RBAC enforced):
- `GET|POST|PUT|DELETE /admin/pricing/promotions[/{id}]` — PromotionRule CRUD (authoring UI lives in 007-b; engine exposes the primitives).
- `GET|POST|PUT|DELETE /admin/pricing/coupons[/{id}]` — Coupon CRUD.
- `GET|PUT /admin/pricing/business/{companyId}/{variantId}` — BusinessPricing upsert/read.
- `GET|PUT /admin/pricing/tier/{variantId}` — TierPricing list/replace.
- `GET|PUT /admin/pricing/tax/{marketCode}` — TaxRule read/update (protected with `pricing.write.tax`).
- `GET /admin/pricing/resolve-debug` — admin-only replay tool: given a customer + basket + timestamp, replay the pipeline (read-only).

Internal events (consumed by 011 orders, 012 invoices):
- `pricing.snapshot.captured` — payload with `breakdown_json` for a placed order (emitted on request from 010/011 via a synchronous call, not the engine itself).

---

## 9. Edge Cases

- **Zero-price item** — allowed (e.g., free sample); skips promo/coupon stages but still runs tax at 0.
- **Negative net after discounts** — clamp to 0, emit `breakdown.warning: negative_net_clamped`.
- **All lines ineligible for a coupon** — coupon returns `couponValidationErrors: [{code: "no_eligible_lines"}]`, no discount applied, no redemption row written.
- **Clock skew on active windows** — engine uses UTC server clock; windows evaluated in UTC with a 2-second grace tolerance.
- **Promotion paused mid-session** — client's next re-resolve drops the discount; engine does not retain client-side cached pricing.
- **Tax rule change mid-session** — same; re-resolve picks up new rule.
- **Currency mismatch** — if variant currency ≠ market currency, engine returns `pricing.currency_mismatch` error (should be caught by catalog validation, but engine guards).
- **Rounding residual** — basket total recomputed as sum of line totals (not re-rounded at basket level) to prevent cents drift.
- **Token replay** — tokens are stateless; replay is possible within TTL. Acceptable because price re-resolves freshly each call.
- **B2B with no business pricing row** — pipeline continues normally; tier/promo/coupon still apply.
- **Multi-currency catalog** — out of scope for Phase 1B; each market is single-currency.
- **Bundle partial presence** — bundle primitive requires all components at ≥ min qty; otherwise contributes nothing.
- **Coupon + exclusion-flag promo on same line** — coupon skipped with `coupon_blocked_by_exclusion_flag` trace.

---

## 10. Acceptance Criteria

Organized by user story priority.

### US1 — Deterministic basket resolution (P1)

- **Given** a known catalog, customer segment, and basket; **when** `resolve-basket` is called twice with identical inputs; **then** results are byte-identical (including trace order, correlation id is the only allowed difference).
- Property-based tests with ≥ 5 000 random baskets across both markets pass without a rounding violation (FR-007).

### US2 — Promotion stacking + exclusion (P1)

- Given two stackable percentage promos (10% + 5%) on the same line → line discount is multiplicative (14.5%), each recorded in the trace in priority order.
- Given a promo with `exclusion_flag=true` applied → all subsequent promos + coupons on that line are skipped with explicit trace reasons.
- Acceptance: all 12 golden-file cases (KSA + EG) match expected breakdown byte-for-byte.

### US3 — Coupon redemption accounting (P1)

- Applying an active coupon within its window produces a discount; attempting to apply the same coupon when `usage_cap_per_customer` is reached returns `coupon_exhausted_for_customer` without touching the basket price.
- `CouponRedemption` row is written only when checkout (010) calls the commit endpoint — engine-layer `resolve-basket` performs validation, not commit.

### US4 — VAT computation parity (P1)

- 50-case golden fixture for KSA 15% and EG configurable yields per-line and basket VAT within ±0 minor units of expected values. (Zero drift tolerance.)
- Switching EG rate via tax-rule update produces new VAT totals within 1 s on the next resolution, without code deploy.

### US5 — Business + tier pricing for B2B (P1)

- B2B buyer in a company with a business-pricing row gets business price; tier stage skipped with explicit trace.
- B2B buyer without a business-pricing row but with qty crossing a tier threshold gets tier price.
- B2B buyer without either gets base price + any applicable promos.

### US6 — Price token resolution (P1)

- Valid token → breakdown returned within p95 ≤ 100 ms at single-variant scope.
- Expired token → `pricing.token_expired` error envelope.
- Forged signature → `pricing.token_invalid` error envelope.

### US7 — Audit on authored changes (P2)

- Every CRUD on promotions, coupons, business pricing, tier pricing, tax rules writes an `audit_events` row with before/after.
- Admin `resolve-debug` replays any historical breakdown using the `PricingSnapshot` captured at order time.

### US8 — Observability + rate safety (P2)

- Resolution logs stream with: `customer_hash`, `basket_hash`, `market`, `total_minor_units`, `latency_ms`, `stack_rules_applied_count`, `coupon_applied` (bool). No raw customer id, no raw coupon code.
- Rate limiter clamps per-IP `resolve-basket` to a configurable threshold; exceeded → 429 with envelope.

---

## 11. Success Criteria (measurable, tech-agnostic)

- **SC-001**: 100% of resolutions are fully explained — the breakdown line count equals the sum of applied stages, with no hidden math. Validated by schema test on every resolution response.
- **SC-002**: `resolve-basket` p95 ≤ 250 ms at a 50-line basket; p99 ≤ 500 ms.
- **SC-003**: `resolve-token` p95 ≤ 100 ms at single-variant scope.
- **SC-004**: 0 rounding drift across 10 000 property-based baskets in both markets (KSA + EG).
- **SC-005**: 100% of VAT golden fixtures match expected to the minor unit.
- **SC-006**: 100% of promotion and coupon edits produce an audit row with before/after.
- **SC-007**: Tax rule change applies within 1 s to subsequent resolutions without deploy.
- **SC-008**: Price-change audit export covers any 30-day window and reconciles to finance reports within ±0 minor units.

---

## 12. Clarifications

### Session 2026-04-20 (auto-resolved per user directive — recommended defaults)

- **Q: Stacking default** → A: Promotions are stackable by default with `stackable=true`; BOGO and bundle default to `stackable=false`; exclusion via `exclusion_flag` overrides and halts further discounting on that line. Authors override per-promo. (Rationale: maximises merchandising flexibility while keeping audit trails explicit; matches mainstream commerce engines.)
- **Q: Maximum coupons per basket in Phase 1B** → A: Exactly one coupon per basket at launch. Multi-coupon stacking deferred to Phase 1.5 (7-b follow-up). (Rationale: avoids combinatorial validation complexity and edge-case redemption accounting before UI review.)
- **Q: Currency storage unit** → A: Integer minor units (halalas / piastres) stored in DB and used in all intermediate computation; display layer converts. Banker's rounding at stored boundaries. (Rationale: eliminates float drift; industry standard for billing systems.)
- **Q: VAT display mode for customer-facing pages** → A: Exclusive (price displayed ex-VAT, tax line itemised at basket) for both KSA and EG at launch; tax-invoice PDF always shows both net and tax (regulatory requirement). Inclusive display is authored per-market via tax rule for future flexibility. (Rationale: aligns with KSA ZATCA practice and B2B expectations; exclusive is safer for mixed retail+B2B.)
- **Q: Price-token TTL and signature** → A: 120 seconds TTL, HMAC-SHA256 signature with a rotating key (7-day rotation), no embedded price. Clients refresh from search on expiry. (Rationale: short enough to prevent abuse, long enough for typical browse→detail flows; never exposes resolved price to client for forgery.)

---

## 13. Dependencies

- **005 catalog** — variants, variant base prices, tax_class, currency, brand/category metadata.
- **004 identity** — customer id, company linkage, RBAC policies `pricing.*`.
- **003 shared audit** — `audit_events` table.
- **006 search** — issues `priceToken`; engine verifies and resolves.

Consumed by (downstream will break without engine): 008 inventory (availability does not need price but uses same basket shape), 009 cart, 010 checkout, 011 orders, 012 tax invoices, 014 customer-app-shell.

---

## 14. Assumptions

- Single currency per market (SAR for KSA, EGP for EG). Multi-currency catalog deferred.
- One coupon per basket at launch; multi-coupon deferred to 1.5.
- Customer segments are a finite enum sourced from identity (`retail`, `b2b`, `student`, `professional_verified`). New segments require a migration.
- Promotion authoring UX, campaign scheduler, and banner linkage ship in 007-b; this spec provides only the engine-layer CRUD.
- Tax rules for KSA and EG cover standard VAT only; zero-rating and exemptions via `tax_class`. Future withholding or selective excise tax = spec 1.5+.
- Business pricing is per-company (not per-user); company hierarchy depth not modelled (flat company list).
- Price-change history preservation: `promotions`, `coupons`, `business_pricing`, `tier_pricing`, `tax_rules` tables are soft-delete + audit-backed, enabling full historical reconstruction for 007 years (finance retention baseline).
- Engine does **not** own abandoned-cart pricing reminders (that's 025 notifications).
- Engine does **not** resolve shipping cost; shipping is spec 013.

---

## 15. Out of Scope (explicit)

- Coupon authoring UI / promo console — 007-b.
- Campaign banners / scheduling UI — 007-b.
- Business-pricing bulk import UI — 007-b (engine exposes upsert, bulk flow is 007-b).
- Shipping cost resolution — 013.
- Invoice PDF rendering — 012.
- Abandoned-cart reminder pricing snapshots — 025.
- Multi-currency pricing — Phase 2.
- Loyalty / store credit — Phase 2.
- Tax withholding and excise — Phase 1.5+.

---

## 16. Constitution Anchors

Principles explicitly exercised by this spec: **3** (B2B first-class), **5** (market configuration), **8** (restricted-product visibility preserved), **10** (centralised pricing), **18** (tax/invoice), **24** (explicit state machines for promo/coupon), **25** (audit on critical actions), **27** (UX states), **28** (AI-build standard), **29** (required spec output). ADR anchors: **ADR-003** (vertical slice), **ADR-004** (EF Core), **ADR-010** (single-region residency).
