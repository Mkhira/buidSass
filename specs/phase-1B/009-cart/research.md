# Research — Cart v1 (Spec 009)

**Date**: 2026-04-22

## R1 — Token for anonymous carts
**Decision**: HMAC-signed opaque 256-bit token, stored in HttpOnly Secure SameSite=Lax cookie + also readable as header `X-Cart-Token`.
**Rationale**: Zero-PII identifier; safe across devices; easy to rotate. Signed so server can reject tampered tokens without DB lookup.
**Alternative**: Plain UUID — safe but lookup overhead higher; we still persist the id, token is the surface form.

## R2 — Merge algorithm
**Decision**: Deterministic line-sum with `max_per_order` cap; B2B metadata from auth cart wins; coupon preserved if eligible for account.
**Rationale**: Predictable outcome + observable via `CartMerger` unit tests.

## R3 — Pricing on read
**Decision**: Preview mode call per read (no stored totals).
**Rationale**: Principle 10 — single source of truth. Cart shouldn't cache totals because promotions/coupons can expire mid-session.
**Alternative**: Stored totals with TTL — rejected; creates drift bugs.

## R4 — Reservation lifecycle
**Decision**: Every line holds a reservation id; updates extend or replace.
**Rationale**: Keeps inventory honest and makes "stock changed" UI path explicit.
**Alternative**: Reserve at checkout only — rejected because shoppers frequently hit "out of stock" surprise at checkout.

## R5 — Abandonment detection
**Decision**: 60 min idle + ≥ 1 line + known email → emit once per 24 h per cart.
**Rationale**: Standard e-commerce norm; 24 h dedup avoids notification spam.

## R6 — Archiving on market switch
**Decision**: Soft-archive for 7 days; user may restore.
**Rationale**: UX safety net for accidental market switches; short enough to avoid stale data.

## R7 — Cart size cap
**Decision**: 100 distinct lines.
**Rationale**: Defensive bound; well above typical B2B order line counts (typically 20-40).

## R8 — Saved-for-later semantics
**Decision**: Separate container, does NOT reserve inventory. Move back = attempt fresh reservation.
**Rationale**: Matches marketplace norms; prevents infinite stock-hoarding via saved-for-later.

## R9 — Optimistic concurrency
**Decision**: `row_version` on cart + cart_line. 409 on conflict; UI retries.
**Rationale**: Two-tab scenarios are common.

## R10 — Coupon storage
**Decision**: Single coupon per cart at Phase 1. Stored on cart, applied in Preview pricing via spec 007-a.
**Rationale**: Stacking coupons introduces a rule explosion pricing engine already rejects (FR-015 above).

## R11 — B2B metadata eligibility
**Decision**: Only accounts flagged `is_b2b=true` (spec 004) can set B2B fields; other attempts return 403.
**Rationale**: Avoids confusing retail customers with B2B jargon.

## R12 — Admin access auditing
**Decision**: Admin read of cart writes an audit row (`cart.admin_viewed`) to trace support-driven access.
**Rationale**: Principle 25 baseline for shopper privacy.
