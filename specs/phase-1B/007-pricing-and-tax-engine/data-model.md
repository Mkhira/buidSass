# Phase 1 Data Model: Pricing & Tax Engine (007-a)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md)

All monetary amounts stored as `bigint` **minor units** (halalas for SAR, piastres for EGP). Schema `pricing`. All tables have `created_at`, `updated_at`, `created_by`, `updated_by`, `xmin_row_version` (system); soft-deleted via `deleted_at`. History tables suffixed `_history` written by trigger for SC-008 replay.

---

## 1. `pricing.promotion_rules`

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `type` | `text` CHECK in (`percentage`,`fixed`,`bogo`,`bundle`) | |
| `priority` | `int` NOT NULL CHECK between 0 and 999 | |
| `stackable` | `bool` NOT NULL | Default `true` for percentage/fixed, `false` for bogo/bundle (enforced in service, not DB) |
| `exclusion_flag` | `bool` NOT NULL DEFAULT false | If true, blocks subsequent promos + coupon on affected lines |
| `active_from` | `timestamptz` | |
| `active_to` | `timestamptz` | CHECK `active_from < active_to` |
| `markets` | `text[]` NOT NULL | Subset of {`eg`,`ksa`} |
| `value_basis_points` | `int` NULL | For percentage; null otherwise |
| `value_minor_units` | `bigint` NULL | For fixed; null otherwise |
| `rule_payload` | `jsonb` | For bogo/bundle parameters (N, M, component_variants, min_qtys) |
| `eligibility_predicate` | `jsonb` NOT NULL | Schema v1 (R4) |
| `usage_cap_global` | `bigint` NULL | |
| `usage_cap_per_customer` | `int` NULL | |
| `state` | `text` NOT NULL CHECK in (`draft`,`scheduled`,`active`,`paused`,`expired`) | |
| `title_en` / `title_ar` | `text` NOT NULL | For admin + trace display |

**Indexes**: `(state, active_from, active_to)`, GIN on `markets`, GIN on `eligibility_predicate`.

---

## 2. `pricing.coupons`

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `code_normalized` | `text` NOT NULL | Uppercased, NFC-normalized |
| `market_code` | `text` NOT NULL | |
| `promotion_rule_id` | `ulid` NOT NULL FK → `promotion_rules(id)` | |
| `usage_cap_total` | `bigint` NULL | |
| `usage_cap_per_customer` | `int` NULL | |
| `min_basket_amount_minor` | `bigint` NULL | |
| `eligible_customer_segments` | `text[]` NOT NULL DEFAULT '{}' | Empty = all |
| `single_use_per_customer` | `bool` NOT NULL DEFAULT false | |
| `active_from` / `active_to` | `timestamptz` | |
| `state` | `text` CHECK in (`draft`,`scheduled`,`active`,`paused`,`expired`,`exhausted`) | |

**Indexes**: `UNIQUE (code_normalized, market_code) WHERE deleted_at IS NULL`, `(state, active_from, active_to)`.

---

## 3. `pricing.coupon_redemptions`

Append-only; commit-time only (R7).

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `coupon_id` | `ulid` FK | |
| `customer_id` | `ulid` NOT NULL | |
| `basket_id` | `ulid` NOT NULL | |
| `redeemed_at` | `timestamptz` NOT NULL | |
| `reversed_at` | `timestamptz` NULL | Set by refund flow (spec 011) |
| `discount_amount_minor` | `bigint` NOT NULL | Recorded for reconciliation |

**Indexes**: `UNIQUE (coupon_id, basket_id)`, `(customer_id, coupon_id)` for per-customer cap queries.

---

## 4. `pricing.business_pricing_entries`

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `company_id` | `ulid` NOT NULL | |
| `variant_id` | `ulid` NULL | |
| `category_id` | `ulid` NULL | |
| `price_minor_units` | `bigint` NOT NULL CHECK ≥ 0 | |
| `currency_code` | `text` NOT NULL | Must match variant market currency |
| `active_from` / `active_to` | `timestamptz` | |

**Constraints**: CHECK `(variant_id IS NOT NULL) <> (category_id IS NOT NULL)` (exactly one). Exclusion constraint prevents overlapping active windows for the same `(company_id, variant_id)` or `(company_id, category_id)`.

---

## 5. `pricing.tier_pricing_entries`

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `variant_id` | `ulid` NULL | |
| `category_id` | `ulid` NULL | |
| `min_quantity` | `int` NOT NULL CHECK ≥ 1 | |
| `price_minor_units` | `bigint` NOT NULL CHECK ≥ 0 | |
| `market_code` | `text` NOT NULL | |
| `active_from` / `active_to` | `timestamptz` | |

**Constraints**: same XOR as business pricing. Exclusion constraint on `(variant_id, market_code, min_quantity, active window)` — no two tiers with same threshold active at once.

---

## 6. `pricing.tax_rules`

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `market_code` | `text` NOT NULL CHECK in (`eg`,`ksa`) | |
| `tax_class` | `text` NOT NULL DEFAULT 'standard' | |
| `rate_basis_points` | `int` NOT NULL CHECK 0..10000 | |
| `display_mode` | `text` NOT NULL CHECK in (`inclusive`,`exclusive`) DEFAULT `exclusive` | |
| `active_from` / `active_to` | `timestamptz` | |

**Constraints**: exclusion on `(market_code, tax_class, active window)` — one active rule per class per market.

**Seed**:
- KSA / standard → 1500 bp (15%), exclusive, active since 2018-01-01.
- EG / standard → 1400 bp (14%), exclusive, active since 2017-07-01.
- Both markets / zero_rated → 0 bp, exclusive, always active.

---

## 7. `pricing.pricing_snapshots`

Written by spec 011 on order placement; engine provides payload.

| Column | Type | Notes |
|---|---|---|
| `id` | `ulid` PK | |
| `order_id` | `ulid` NOT NULL UNIQUE | FK to orders (spec 011) |
| `breakdown_json` | `jsonb` NOT NULL | Full BreakdownDTO |
| `engine_version` | `text` NOT NULL | Semver |
| `captured_at` | `timestamptz` NOT NULL | |

---

## 8. History Tables (trigger-backed)

For each of `promotion_rules`, `coupons`, `business_pricing_entries`, `tier_pricing_entries`, `tax_rules`: a `*_history` table storing the full row plus `valid_from`, `valid_to`, `operation` (`insert`/`update`/`delete`). Triggers insert on every write. Used by admin `resolve-debug` (R11) and finance reconciliation (SC-008).

---

## 9. DTOs (shared contracts, camelCase, at `packages/shared_contracts/pricing/`)

- `ResolveBasketRequest` — `{ basketId, customerId?, companyId?, marketCode, lines[{variantId, quantity}], couponCode?, at? }`
- `ResolveTokenRequest` — `{ priceToken, customerId?, marketCode }`
- `LinePricingBreakdown` — `{ variantId, quantity, basePriceMinor, compareAtMinor?, resolvedUnitPriceMinor, lineSubtotalMinor, discountsApplied[], lineNetMinor, taxMinor, lineTotalMinor, trace[] }`
- `BasketPricingBreakdown` — `{ breakdownCorrelationId, marketCode, currency, lines[LinePricingBreakdown], couponApplied?{code, discountMinor}, couponValidationErrors[], subtotalMinor, discountsTotalMinor, netTotalMinor, taxTotalMinor, totalMinor, warnings[] }`
- `BreakdownTraceEntry` — `{ stage, promotionId?, couponId?, priceBefore, priceAfter, note }`
- `ErrorEnvelope` — `{ code, messageEn, messageAr, correlationId, details? }`

---

## 10. Migration Files

- `V007_001__create_pricing_schema.sql` — schema, all 7 authored tables, redemptions, snapshots, history tables, triggers, seeds.
- `V007_002__seed_default_tax_rules.sql` — KSA 15% + EG 14% + zero-rated.
