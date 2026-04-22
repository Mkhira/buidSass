# Data Model — Cart v1 (Spec 009)

**Date**: 2026-04-22 · Schema: `cart`.

## Tables (5)

### 1. `cart.carts`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `account_id` | uuid NULL FK → identity.accounts | NULL for anonymous |
| `cart_token_hash` | bytea NULL | sha-256 of signed token; indexed; required when account_id is null |
| `market_code` | citext NOT NULL | |
| `status` | citext NOT NULL | `active`\|`archived`\|`merged`\|`purged` |
| `coupon_code` | citext NULL | |
| `last_touched_at` | timestamptz NOT NULL | updated on any mutation or read |
| `archived_at` | timestamptz NULL | set on market switch |
| `archived_reason` | citext NULL | `market_switch`\|`merged`\|`admin` |
| `row_version` | bytea NOT NULL | |
| `owner_id` / `vendor_id` | P6 | |
| `created_at` / `updated_at` | timestamptz | |

Unique partial index: `(account_id, market_code) WHERE status='active' AND account_id IS NOT NULL` — enforces one active cart per `(account, market)`.
Index `(cart_token_hash) WHERE status='active'`.

### 2. `cart.cart_lines`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `cart_id` | uuid FK → carts | |
| `product_id` | uuid FK → catalog.products | |
| `qty` | int NOT NULL | ≥ 1 CHECK |
| `reservation_id` | uuid NULL FK → inventory.reservations | |
| `unavailable` | bool NOT NULL DEFAULT false | set by spec 005 archive hook |
| `restricted` | bool NOT NULL DEFAULT false | snapshotted at add; refreshed on read |
| `restriction_reason_code` | citext NULL | |
| `stock_changed` | bool NOT NULL DEFAULT false | set if re-reservation needed |
| `added_at` / `updated_at` | timestamptz | |
| `row_version` | bytea NOT NULL | |

Unique `(cart_id, product_id)`.

### 3. `cart.cart_saved_items`
| column | type | notes |
|---|---|---|
| `cart_id` | uuid FK → carts | |
| `product_id` | uuid FK → catalog.products | |
| `saved_at` | timestamptz NOT NULL | |

PK `(cart_id, product_id)`.

### 4. `cart.cart_b2b_metadata`
| column | type | notes |
|---|---|---|
| `cart_id` | uuid PK FK → carts | |
| `po_number` | text NULL | |
| `reference` | text NULL | |
| `notes` | text NULL | |
| `requested_delivery_from` | timestamptz NULL | |
| `requested_delivery_to` | timestamptz NULL | |
| `updated_at` | timestamptz | |

### 5. `cart.abandoned_emissions`
Dedupe abandonment events.
| column | type | notes |
|---|---|---|
| `cart_id` | uuid PK FK → carts | |
| `last_emitted_at` | timestamptz NOT NULL | |

---

## State machine — Cart
States: `active`, `archived`, `merged`, `purged`.

| from | to | trigger | actor |
|---|---|---|---|
| active | archived | market switch | customer |
| active | merged | login merges anon → auth | system |
| archived | active | restore within 7 days | customer |
| archived | purged | 7-day reaper | system |
| active | purged | 30-day guest cleanup | system |

## Audit + events
Events:
- `cart.line_added|updated|removed`
- `cart.coupon_applied|removed`
- `cart.abandoned`
- `cart.merged`
- `cart.archived|restored|purged`
- `cart.admin_viewed` (audit level)

Admin reads write audit rows per Principle 25; customer mutations use debug-level structured logs (volume).
