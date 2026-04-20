# Events Contract: Pricing & Tax Engine (007-a)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md)

The engine emits **audit events** (for authored-data changes) and **observability signals** (for every resolution). It consumes catalog + identity events for cache invalidation. Customer-facing domain events are not emitted — the engine is stateless w.r.t. customer lifecycle.

---

## 1. Consumed Events

| Event (from) | Engine action |
|---|---|
| `catalog.variant.published` / `updated` / `archived` (spec 005) | Invalidate any cached promo that targets the variant by id; no direct pricing effect |
| `catalog.category.renamed` / `moved` (spec 005) | Re-evaluate cached predicates that reference the category |
| `identity.company.updated` (spec 004) | Invalidate cached business-pricing lookups for that company |
| `identity.customer.segment_changed` (spec 004) | Invalidate per-customer coupon-eligibility cache entry |

All consumed via MediatR `INotificationHandler<T>` in `Features/Pricing/Observability/CacheInvalidationSubscribers/`.

---

## 2. Published Audit Events (→ `audit_events`, spec 003)

All carry `actor_subject_id`, `market_code`, `correlation_id`, structured `details`.

| Action code | Emitted when | Details payload |
|---|---|---|
| `pricing.promotion.created` | Admin creates a promotion rule | `promotionId`, `type`, `state`, `markets`, `priority` |
| `pricing.promotion.updated` | Admin updates a promotion rule | `promotionId`, `before`, `after` |
| `pricing.promotion.state_changed` | State transition (draft→scheduled, etc.) | `promotionId`, `fromState`, `toState`, `reason` |
| `pricing.promotion.deleted` | Soft-delete | `promotionId` |
| `pricing.coupon.created` | Admin creates a coupon | `couponId`, `marketCode`, `promotionRuleId` |
| `pricing.coupon.updated` | Admin updates a coupon | `couponId`, `before`, `after` |
| `pricing.coupon.state_changed` | State transition | `couponId`, `fromState`, `toState` |
| `pricing.coupon.deleted` | Soft-delete | `couponId` |
| `pricing.coupon.redeemed` | Checkout commits a redemption | `couponId`, `customerId`, `basketId`, `discountMinor` |
| `pricing.coupon.redemption_reversed` | Refund reverses a redemption | `redemptionId`, `reason` |
| `pricing.business_pricing.upserted` | Admin upserts a business pricing entry | `entryId`, `companyId`, `variantId?`, `categoryId?`, `priceMinorBefore?`, `priceMinorAfter` |
| `pricing.business_pricing.deleted` | Soft-delete | `entryId` |
| `pricing.tier_pricing.replaced` | Admin replaces the tier list for a variant | `variantId`, `marketCode`, `entriesBefore`, `entriesAfter` |
| `pricing.tax_rule.updated` | Admin updates a tax rule | `ruleId`, `marketCode`, `taxClass`, `before`, `after` |
| `pricing.debug_replay.executed` | Admin runs `/admin/pricing/resolve-debug` | `customerId`, `basketHash`, `at`, `totalMinor` |

---

## 3. Observability Signals (not persisted as audit)

Emitted to Serilog + OpenTelemetry on every resolution. Per R9, no raw PII or coupon codes.

| Signal | Fields |
|---|---|
| `pricing.resolve_basket.executed` | `market`, `customer_hash`, `basket_hash`, `line_count`, `total_minor_units`, `tax_minor_units`, `discounts_total_minor`, `stack_rules_applied_count`, `coupon_applied` (bool), `latency_ms`, `correlation_id` |
| `pricing.resolve_token.executed` | `market`, `variant_id`, `customer_hash`, `resolved_unit_minor`, `latency_ms`, `correlation_id` |
| `pricing.token.rejected` | `reason` (`expired`/`invalid_signature`/`malformed`), `correlation_id` |
| `pricing.cache.invalidated` | `scope` (`promotion`/`business_pricing`/`tax_rule`), `market` |

---

## 4. FR / SC Traceability

| Source | Events |
|---|---|
| FR 3.7 (audit on authored writes) | All `pricing.*.created/updated/deleted/state_changed` |
| FR 3.8 (price token) | `pricing.resolve_token.executed`, `pricing.token.rejected` |
| US7 (admin audit + replay) | All audit events + `pricing.debug_replay.executed` |
| US8 (observability + rate safety) | `pricing.resolve_basket.executed`, `pricing.resolve_token.executed` |
| SC-006 (100% promo/coupon edits audited) | All `pricing.promotion.*` + `pricing.coupon.*` |
| SC-007 (tax rule change within 1s) | `pricing.tax_rule.updated` + `pricing.cache.invalidated` |
| SC-008 (30-day audit export) | Audit rows + trigger-backed `*_history` tables (data-model §8) |

---

## 5. Not Emitted (deferred)

- Promotion-authoring drafts / reviews — 007-b.
- Campaign linkage — 007-b.
- Business-pricing bulk-import completion — 007-b.
- Loyalty / store-credit events — Phase 2.
