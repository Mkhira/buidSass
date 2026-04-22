# Data Model — Pricing & Tax Engine v1 (Spec 007-a)

**Date**: 2026-04-22 · Schema: `pricing`.

## Tables (9)

### 1. `pricing.tax_rates`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `market_code` | citext NOT NULL | `eg` \| `ksa` |
| `kind` | citext NOT NULL | `vat` at launch |
| `rate_bps` | int NOT NULL | basis points; 1500 = 15 % |
| `effective_from` | timestamptz NOT NULL | |
| `effective_to` | timestamptz NULL | |
| `created_by_account_id` | uuid | |
| `created_at` | timestamptz | |

Unique `(market_code, kind, effective_from)`. Lookup: latest row with `effective_from ≤ now AND (effective_to IS NULL OR now < effective_to)`.

### 2. `pricing.promotions`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `kind` | citext NOT NULL | `percent_off` \| `amount_off` \| `bogo` \| `bundle_wrapper` |
| `name` | text NOT NULL | |
| `config_json` | jsonb NOT NULL | kind-specific shape |
| `applies_to_product_ids` | uuid[] NULL | empty = all |
| `applies_to_category_ids` | uuid[] NULL | |
| `market_codes` | citext[] NOT NULL | |
| `priority` | int NOT NULL | lower = earlier |
| `starts_at` / `ends_at` | timestamptz NULL | |
| `owner_id` / `vendor_id` | citext/uuid | P6 |
| `is_active` | bool NOT NULL DEFAULT true | |
| `created_at` / `updated_at` / `deleted_at` | timestamptz | |

### 3. `pricing.coupons`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `code` | citext UNIQUE NOT NULL | uppercased |
| `kind` | citext NOT NULL | `percent` \| `amount` |
| `value` | int NOT NULL | percent → bps, amount → minor units |
| `cap_minor` | bigint NULL | cap for percent coupons |
| `per_customer_limit` | int NULL | |
| `overall_limit` | int NULL | |
| `used_count` | int NOT NULL DEFAULT 0 | |
| `excludes_restricted` | bool NOT NULL DEFAULT false | |
| `market_codes` | citext[] NOT NULL | |
| `valid_from` / `valid_to` | timestamptz | |
| `row_version` | bytea NOT NULL | optimistic concurrency |
| `owner_id` / `vendor_id` | P6 | |

### 4. `pricing.coupon_redemptions`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `coupon_id` | uuid FK → coupons | |
| `account_id` | uuid FK → identity.accounts | |
| `order_id` | uuid NULL FK → orders.orders | populated on checkout |
| `redeemed_at` | timestamptz NOT NULL | |

Unique `(coupon_id, account_id, order_id)`.

### 5. `pricing.b2b_tiers`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `slug` | citext UNIQUE | `tier-1`, `tier-2`, … |
| `name` | text | |
| `default_discount_bps` | int | fallback if no product-specific tier price |
| `is_active` | bool | |

### 6. `pricing.account_b2b_tiers`
| column | type | notes |
|---|---|---|
| `account_id` | uuid PK FK → identity.accounts | |
| `tier_id` | uuid FK → b2b_tiers | |
| `assigned_at` | timestamptz | |
| `assigned_by_account_id` | uuid | audit |

### 7. `pricing.product_tier_prices`
| column | type | notes |
|---|---|---|
| `product_id` | uuid FK → catalog.products | |
| `tier_id` | uuid FK → b2b_tiers | |
| `market_code` | citext | |
| `net_minor` | bigint NOT NULL | |

PK `(product_id, tier_id, market_code)`.

### 8. `pricing.price_explanations`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `owner_kind` | citext NOT NULL | `quote` \| `order` \| `preview` |
| `owner_id` | uuid NOT NULL | |
| `account_id` | uuid NULL | |
| `market_code` | citext NOT NULL | |
| `explanation_json` | jsonb NOT NULL | canonical; immutable |
| `explanation_hash` | bytea NOT NULL | sha-256 |
| `grand_total_minor` | bigint NOT NULL | |
| `created_at` | timestamptz NOT NULL | |

Unique `(owner_kind, owner_id)` where `owner_kind ∈ (quote, order)`. Append-only.

### 9. `pricing.bundle_memberships`
(Optional at Phase 1; included for future bundle analytics. Bundles ship as SKUs in spec 005 but this lets admin tools inspect composition.)

| column | type | notes |
|---|---|---|
| `bundle_product_id` | uuid FK → catalog.products | |
| `component_product_id` | uuid FK → catalog.products | |
| `qty` | int NOT NULL | |

PK `(bundle_product_id, component_product_id)`.

---

## State machines
No persistent pricing state machine — engine is stateless. Coupon usage counters + redemption rows are transactional writes.

Promotion `is_active` flip + schedule windows are not a state machine (two-state toggle).

## Audit
Every write to `tax_rates`, `promotions`, `coupons`, `b2b_tiers`, `account_b2b_tiers`, `product_tier_prices` calls `IAuditEventPublisher` (spec 003) with before/after JSON + actor account id.

## Explanation JSON shape (canonical)
```json
{
  "version": 1,
  "market": "ksa",
  "currency": "SAR",
  "nowUtc": "2026-04-22T10:00:00.000Z",
  "lines": [
    {
      "productId": "…",
      "qty": 2,
      "listMinor": 10000,
      "layers": [
        { "layer": "list",      "ruleId": null,          "appliedMinor": 20000 },
        { "layer": "tier",      "ruleId": "tier-2/p-…",  "appliedMinor": -2000 },
        { "layer": "promotion", "ruleId": "promo-uuid",  "appliedMinor": -0 },
        { "layer": "coupon",    "ruleId": "coupon-uuid", "appliedMinor": -1800 },
        { "layer": "tax",       "ruleId": "ksa/vat",     "appliedMinor": 2430 }
      ],
      "netMinor": 16200,
      "taxMinor": 2430,
      "grossMinor": 18630
    }
  ],
  "totals": {
    "subtotalMinor": 16200,
    "discountMinor": 3800,
    "taxMinor": 2430,
    "grandTotalMinor": 18630
  }
}
```

Canonical = sorted keys, minified, UTF-8. `explanation_hash` = SHA-256 of canonical bytes.
