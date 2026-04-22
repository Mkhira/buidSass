# Data Model — Catalog v1 (Spec 005)

**Date**: 2026-04-22. Schema: `catalog`.

## Tables (12)

### 1. `catalog.categories`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `slug` | citext NOT NULL | unique within `(parent_id, slug)` |
| `parent_id` | uuid NULL FK → categories | |
| `name_ar` | text NOT NULL | |
| `name_en` | text NOT NULL | |
| `display_order` | int NOT NULL DEFAULT 0 | |
| `is_active` | bool NOT NULL DEFAULT true | |
| `owner_id` | citext NOT NULL DEFAULT 'platform' | P6 |
| `vendor_id` | uuid NULL | P6 |
| `created_at` / `updated_at` / `deleted_at` | timestamptz | soft-delete filter |

### 2. `catalog.category_closure`
| column | type | notes |
|---|---|---|
| `ancestor_id` | uuid NOT NULL FK → categories | |
| `descendant_id` | uuid NOT NULL FK → categories | |
| `depth` | int NOT NULL | 0 = self |

PK `(ancestor_id, descendant_id)`. Index `(descendant_id, depth)`.

### 3. `catalog.category_attribute_schemas`
| column | type | notes |
|---|---|---|
| `category_id` | uuid PK FK → categories | |
| `schema_json` | jsonb NOT NULL | JSON-Schema draft 2020-12 |
| `version` | int NOT NULL | monotonically increments |
| `updated_at` | timestamptz | |

### 4. `catalog.brands`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `slug` | citext UNIQUE NOT NULL | |
| `name_ar` / `name_en` | text NOT NULL | |
| `logo_media_id` | uuid NULL FK → product_media | |
| `owner_id` / `vendor_id` | as P6 | |
| `is_active` | bool NOT NULL DEFAULT true | |

### 5. `catalog.manufacturers`
Same shape as brands.

### 6. `catalog.products`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `sku` | citext UNIQUE NOT NULL | |
| `barcode` | text NULL | indexed |
| `brand_id` | uuid NOT NULL FK → brands | |
| `manufacturer_id` | uuid NULL FK → manufacturers | |
| `slug_ar` / `slug_en` | citext NOT NULL | unique per locale per market; immutable post first-publish |
| `name_ar` / `name_en` | text NOT NULL | |
| `short_description_ar` / `short_description_en` | text NULL | |
| `description_ar` / `description_en` | text NULL | |
| `attributes` | jsonb NOT NULL DEFAULT '{}' | validated per-category |
| `market_codes` | citext[] NOT NULL | subset of configured markets |
| `status` | citext NOT NULL | state machine |
| `restricted` | bool NOT NULL DEFAULT false | |
| `restriction_reason_code` | citext NULL | `professional_verification` launch |
| `restriction_markets` | citext[] NOT NULL DEFAULT '{}' | empty → all configured markets |
| `price_hint_minor_units` | bigint NULL | non-authoritative; pricing owns truth |
| `published_at` | timestamptz NULL | |
| `archived_at` | timestamptz NULL | |
| `owner_id` / `vendor_id` | P6 | |
| `created_by_account_id` | uuid NOT NULL FK → identity.accounts | |
| `updated_at` / `deleted_at` | timestamptz | |

Indexes: `(status, market_codes)`, GIN `(attributes)`, `(brand_id)`, `(barcode)`, `(restricted, restriction_markets)`.

### 7. `catalog.product_categories`
| column | type | notes |
|---|---|---|
| `product_id` | uuid FK → products | |
| `category_id` | uuid FK → categories | |
| `is_primary` | bool NOT NULL | exactly one true per product |

PK `(product_id, category_id)`.

### 8. `catalog.product_media`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `product_id` | uuid FK → products | |
| `storage_key` | text NOT NULL | content-addressed key |
| `content_sha256` | bytea NOT NULL | |
| `mime_type` | text NOT NULL | |
| `bytes` | bigint NOT NULL | |
| `width_px` / `height_px` | int NOT NULL | original |
| `display_order` | int NOT NULL DEFAULT 0 | |
| `is_primary` | bool NOT NULL DEFAULT false | |
| `alt_ar` / `alt_en` | text NULL | required at publish |
| `variants` | jsonb NOT NULL DEFAULT '{}' | `{ "thumb": {url,bytes,...}, "card":…, "detail":…, "hero":… }` |
| `variant_status` | citext NOT NULL | `pending` \| `ready` \| `failed` |
| `owner_id` / `vendor_id` | P6 | |

### 9. `catalog.product_documents`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `product_id` | uuid FK → products | |
| `doc_type` | citext NOT NULL | `msds` \| `datasheet` \| `regulatory_cert` \| `ifu` \| `brochure` |
| `locale` | citext NOT NULL | `ar` \| `en` |
| `storage_key` | text NOT NULL | |
| `content_sha256` | bytea NOT NULL | |
| `title_ar` / `title_en` | text NULL | |

Unique `(product_id, doc_type, locale)`.

### 10. `catalog.product_state_transitions`
| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `product_id` | uuid FK → products | |
| `from_status` / `to_status` | citext | |
| `actor_account_id` | uuid FK → identity.accounts | |
| `reason` | text NULL | |
| `occurred_at` | timestamptz NOT NULL | |

### 11. `catalog.scheduled_publishes`
| column | type | notes |
|---|---|---|
| `product_id` | uuid PK FK → products | |
| `publish_at` | timestamptz NOT NULL | |
| `scheduled_by_account_id` | uuid | |
| `scheduled_at` | timestamptz NOT NULL | |
| `worker_claimed_at` | timestamptz NULL | |
| `worker_completed_at` | timestamptz NULL | |

### 12. `catalog.catalog_outbox`
| column | type | notes |
|---|---|---|
| `id` | bigserial PK | |
| `event_type` | citext NOT NULL | `catalog.product.published`, `catalog.product.archived`, `catalog.product.field_updated`, `catalog.product.restriction_changed` |
| `aggregate_id` | uuid NOT NULL | product id |
| `payload_json` | jsonb NOT NULL | |
| `committed_at` | timestamptz NOT NULL | |
| `dispatched_at` | timestamptz NULL | |

Partial index `WHERE dispatched_at IS NULL`.

---

## State Machines

### SM-1 · Product
States: `draft`, `in_review`, `scheduled`, `published`, `archived`.

| from | to | trigger | actor | failure |
|---|---|---|---|---|
| draft | in_review | `submit_for_review` | catalog.product.submit | validation gate fails → reject |
| in_review | draft | `withdraw` | catalog.product.submit | — |
| in_review | scheduled | `publish` + future `publish_at` | catalog.product.publish | publish_at in past → 400 |
| in_review | published | `publish` | catalog.product.publish | media/locale validation → 400 |
| scheduled | published | `worker_fire` | system | — |
| scheduled | in_review | `cancel_schedule` | catalog.product.publish | — |
| published | archived | `archive` | catalog.product.archive | referenced by active orders → soft-archive flag (still archives) |

Every transition writes `product_state_transitions` + audit event + outbox entry.

### SM-2 · MediaVariantJob
States: `pending`, `processing`, `ready`, `failed`.
Transitions driven by `MediaVariantWorker`. On `failed` + retry count > 3, operator alert emitted; product can still publish (see Edge Case #6).
