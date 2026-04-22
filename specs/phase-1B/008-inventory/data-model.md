# Data Model — Inventory v1 (Spec 008)

**Date**: 2026-04-22 · Schema: `inventory`.

## Tables (6)

### 1. `inventory.warehouses`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `code` | citext UNIQUE NOT NULL | `eg-main`, `ksa-main` |
| `market_code` | citext NOT NULL | |
| `display_name` | text | |
| `is_active` | bool NOT NULL DEFAULT true | |
| `owner_id` / `vendor_id` | P6 | |

### 2. `inventory.stocks`
One row per `(product_id, warehouse_id)`.
| column | type | notes |
|---|---|---|
| `product_id` | uuid NOT NULL FK → catalog.products | |
| `warehouse_id` | uuid NOT NULL FK → warehouses | |
| `on_hand` | int NOT NULL DEFAULT 0 | ≥ 0 CHECK constraint |
| `reserved` | int NOT NULL DEFAULT 0 | ≥ 0 CHECK |
| `safety_stock` | int NOT NULL DEFAULT 0 | ≥ 0 CHECK |
| `reorder_threshold` | int NOT NULL DEFAULT 0 | ≥ 0 CHECK |
| `bucket_cache` | citext NOT NULL DEFAULT 'out_of_stock' | `in_stock`\|`backorder`\|`out_of_stock` |
| `updated_at` | timestamptz NOT NULL | |
| `row_version` | bytea NOT NULL | xmin/`rowversion` for debugging |

PK `(product_id, warehouse_id)`. Index `(bucket_cache)` for search-sync queries.

### 3. `inventory.batches`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `product_id` | uuid NOT NULL FK → catalog.products | |
| `warehouse_id` | uuid NOT NULL FK → warehouses | |
| `lot_no` | text NOT NULL | |
| `expiry_date` | date NOT NULL | |
| `qty_on_hand` | int NOT NULL | ≥ 0 CHECK |
| `status` | citext NOT NULL | `active`\|`expired`\|`depleted` |
| `received_at` | timestamptz NOT NULL | |
| `received_by_account_id` | uuid | |
| `notes` | text NULL | |

Unique `(product_id, warehouse_id, lot_no)`. Index `(product_id, warehouse_id, expiry_date)` for FEFO.

### 4. `inventory.reservations`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `product_id` | uuid FK → catalog.products | |
| `warehouse_id` | uuid FK → warehouses | |
| `qty` | int NOT NULL | |
| `cart_id` | uuid NULL | spec 009 |
| `order_id` | uuid NULL | populated on convert |
| `picked_batch_id` | uuid NULL FK → batches | FEFO result |
| `status` | citext NOT NULL | `active`\|`released`\|`converted` |
| `expires_at` | timestamptz NOT NULL | |
| `created_at` | timestamptz NOT NULL | |
| `released_at` | timestamptz NULL | |
| `converted_at` | timestamptz NULL | |

Index `(status, expires_at)` for release worker.

### 5. `inventory.movements`
Append-only ledger.
| column | type | notes |
|---|---|---|
| `id` | bigserial PK | |
| `product_id` | uuid FK → catalog.products | |
| `warehouse_id` | uuid FK → warehouses | |
| `batch_id` | uuid NULL FK → batches | |
| `kind` | citext NOT NULL | `receipt`\|`sale`\|`return`\|`adjustment`\|`writeoff`\|`transfer_in`\|`transfer_out` |
| `delta` | int NOT NULL | signed |
| `reason` | text NULL | |
| `source_kind` | citext NULL | `order`\|`return`\|`manual`\|`worker` |
| `source_id` | uuid NULL | |
| `actor_account_id` | uuid NULL | |
| `occurred_at` | timestamptz NOT NULL | |

Index `(product_id, warehouse_id, occurred_at)`.

### 6. `inventory.reorder_event_emissions`
Debounce record.
| column | type | notes |
|---|---|---|
| `product_id` | uuid | |
| `warehouse_id` | uuid | |
| `last_emitted_at` | timestamptz NOT NULL | |

PK `(product_id, warehouse_id)`.

---

## State machines

### SM-1 · Reservation
States: `active`, `released`, `converted`.

| from | to | trigger | actor |
|---|---|---|---|
| active | released | TTL expired OR explicit release | worker / spec 010 |
| active | converted | order-confirm | system (spec 011) |

Terminal: `released`, `converted`. No re-activation.

### SM-2 · Batch
States: `active`, `expired`, `depleted`.

| from | to | trigger |
|---|---|---|
| active | expired | daily expiry worker |
| active | depleted | `qty_on_hand` reaches 0 |
| expired | expired | terminal |

## Audit
Every `movements` row is a durable audit record. Admin-initiated movements additionally call `IAuditEventPublisher` with before/after snapshots per Principle 25.

## Events emitted
- `inventory.reservation.created|released|converted`
- `inventory.movement.posted` (kind-tagged)
- `inventory.reorder_threshold_crossed` (debounced)
- `inventory.batch_expired` (daily)
- `product.availability.changed` (bucket transition)
