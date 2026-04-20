# Phase 0 Research: Pricing & Tax Engine (007-a)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Each entry: **Decision**, **Rationale**, **Alternatives Considered**.

---

## R1 — Money representation

- **Decision**: A `Money` struct wrapping `(long MinorUnits, CurrencyCode Currency)`. All arithmetic on `MinorUnits` (long). No implicit decimal conversion. Division returns a `DistributedAllocation<Money>` type that forces callers to choose a rounding policy per call. `Rate` is `(int BasisPoints)` — percentages stored as integer basis points (e.g., 1500 = 15%).
- **Rationale**: Eliminates float drift (SC-004). Forcing distribution-type for division prevents residual-loss bugs. Basis points avoid decimal arithmetic entirely for percentages.
- **Alternatives**: `decimal` — rejected; still permits implicit rounding and ORM mapping surprises. `BigInteger` — overkill.

## R2 — Rounding policy

- **Decision**: Banker's rounding (`MidpointRounding.ToEven`) at every stored minor-unit boundary. Line totals stored as whole minor units; basket total = `sum(line_totals)` (no re-rounding). Percentage discounts compute `line_minor * basis_points / 10000` with banker's rounding of the remainder.
- **Rationale**: Eliminates the 1-cent drift that plagues finance systems. Sum-of-lines semantics matches invoice conventions.
- **Alternatives**: Half-up — rejected; introduces systematic upward bias.

## R3 — Pipeline shape

- **Decision**: 9 ordered stages implemented as `IPricingStage` with `PricingContext Apply(PricingContext ctx)`. Context is immutable; each stage returns a new context with appended breakdown trace. Stages registered in a fixed composition root order — no runtime reordering.
- **Rationale**: Determinism (US1 byte-identical results); testability (each stage tested in isolation). Immutability makes the trace the authoritative replay record.
- **Alternatives**: Mutable pipeline state — rejected; complicates replay and property tests.

## R4 — Promotion eligibility predicate schema

- **Decision**: JSON predicate with a fixed schema: `{ all?: [...], any?: [...], not?: {...}, match: { kind: "segment"|"variant"|"category"|"brand"|"market"|"min_basket_amount", value: ... } }`. Evaluator is a pure function in `Features/Pricing/Promotions/PredicateEvaluator.cs`. Schema version `v1` baked in — schema bumps require migration.
- **Rationale**: Reviewable in PRs (no custom DSL); evaluator is <200 LOC and fully testable; avoids DB JSONB query complexity (predicates live in promo row, evaluated in-process).
- **Alternatives**: SQL-based predicate — rejected; couples catalog JSON shape to engine. Expression-tree DSL — rejected; over-powered for launch rule set.

## R5 — Promotion loading + caching

- **Decision**: On startup and on promo-write events (MediatR notifications), load all `state=active` promotions into an in-process `IMemoryCache` keyed by market. Cache invalidation on every write. At peak (~500 active promos × 2 markets), full payload ≈ 1 MB — fits trivially in memory.
- **Rationale**: Pipeline needs promo list on every resolution; DB hit per resolution would break SC-002. In-process cache with write-through invalidation is simpler than Redis for Phase 1B single-region.
- **Alternatives**: Redis — deferred; single-region, single-writer admin flows don't need distributed cache coherence yet.

## R6 — Tax rule resolution

- **Decision**: Tax rules cached per market; resolver returns effective rule for `(market, tax_class, at_instant)`. Active window evaluated via `NodaTime.Instant`. Default tax rule per market is mandatory — engine refuses to resolve if no active rule exists for the market (fail-fast).
- **Rationale**: Determinism; fail-fast prevents silent 0%-tax computation when rules misconfigured.
- **Alternatives**: Fallback to 0% — rejected; silently hides misconfiguration.

## R7 — Coupon validation vs redemption

- **Decision**: `resolve-basket` and `validate-coupon` both perform validation (window, cap, eligibility, min basket) but neither increments `CouponRedemption`. Only `POST /pricing/coupons/{id}/commit-redemption` (called by checkout spec 010 on payment auth) writes redemption rows. Commit is idempotent via `(coupon_id, basket_id)` unique constraint.
- **Rationale**: Prevents double-counting when cart resolves price multiple times. Matches how checkout flows actually commit state.
- **Alternatives**: Increment on first apply — rejected; inflates usage, punishes customers who revise basket.

## R8 — Price token format

- **Decision**: Compact base64url-encoded payload `{ v: 1, variant_id, market, iat, ttl }` with detached HMAC-SHA256 signature. Signing key rotated weekly via Azure Key Vault (dev uses a file-mounted key). Verifier accepts `current_key` OR `previous_key` to cover rotation windows.
- **Rationale**: JWT overkill for a 2-field claim; custom compact format keeps token ≤ 120 chars (search payload size budget). Dual-key window guarantees zero-downtime rotation.
- **Alternatives**: JWT — rejected; header overhead + library risk.

## R9 — Observability schema

- **Decision**: Resolution log fields: `pricing.market`, `pricing.customer_hash` (HMAC of customer_id), `pricing.basket_hash` (SHA-256 of `{variant_id, qty}` array), `pricing.line_count`, `pricing.total_minor_units`, `pricing.tax_minor_units`, `pricing.stack_rules_applied_count`, `pricing.coupon_applied`, `pricing.latency_ms`, `pricing.correlation_id`. No coupon codes, no variant SKUs, no raw customer id.
- **Rationale**: Sufficient signal for SRE + finance sampling; zero PII or promotional-secret leakage.

## R10 — Snapshot ownership

- **Decision**: Engine exposes a pure `BuildBreakdown(context) → BreakdownJson` method. Order-placement (spec 011) calls it and writes the `PricingSnapshot` row with `order_id`. Engine does **not** own snapshot storage — it owns the payload shape.
- **Rationale**: Keeps order lifecycle fully in 011. Engine stays stateless w.r.t. order history.

## R11 — Admin `resolve-debug`

- **Decision**: Admin endpoint accepts `{ customer_id, basket_payload, at_instant }` → replays the pipeline against authored data as of `at_instant`. Authored-data tables carry `effective_history` (system-versioned via trigger writes to `*_history` tables).
- **Rationale**: US7 + SC-008 require historical reconstruction. System-versioned tables (trigger-based) avoid PostgreSQL 14 temporal limitations while staying queryable.
- **Alternatives**: Event sourcing — rejected; scope creep for launch.

## R12 — Basket size cap

- **Decision**: `resolve-basket` rejects baskets > 200 lines with `pricing.basket_too_large`. p95 SC measured at 50 lines; 200 gives 4× headroom.
- **Rationale**: Prevents pathological input; dental B2B baskets observed in the field max out around 60 lines.

## R13 — Concurrency on authored data

- **Decision**: All authored-data tables use `xmin` row version with optimistic concurrency. Admin writes fail with 409 on conflict; client re-reads + retries.
- **Rationale**: Consistent with spec 005 (catalog) pattern; low contention expected.

## R14 — EF Core schema

- **Decision**: Dedicated `pricing` schema. Separate DbContext (`PricingDbContext`) to keep migrations isolated from catalog + identity.
- **Rationale**: Module isolation per ADR-003; avoids merge conflicts on a shared migration chain.

## R15 — Property-based test shape

- **Decision**: FsCheck generators produce `(basket, customer_segment, active_promos, market, coupon?)` tuples. Invariants checked: (a) `total = sum(line_totals)`, (b) `line_total ≥ 0`, (c) identical inputs → identical output (determinism), (d) `discount ≤ pre_discount_line_subtotal`, (e) VAT = `sum(line_vat)` with ≤ 0 drift.
- **Rationale**: These invariants are the spec's truth conditions and cover SC-001/SC-004.

---

## Outstanding Items

None. All Technical Context unknowns resolved. Proceed to Phase 1 design.
