# Phase 1 Data Model — Catalog (005)

**Feature**: `specs/phase-1B/005-catalog/spec.md`
**Plan**: `./plan.md`
**Research**: `./research.md`

All tables live in Postgres schema `catalog`. All tables carry `market_code char(3) not null` per ADR-010 (values: `EG`, `KSA`). All tables carry `owner_id uuid not null` and `vendor_id uuid null` per FR-027. Soft-delete via `deleted_at timestamptz null` with EF Core global query filter. Timestamps are `timestamptz not null default now()`. Primary keys are `uuid` defaulting to `gen_random_uuid()`.

---

## Reference / seeded tables

### `taxonomy_keys`

Migration-seeded only per Clarification Q5. No admin self-service in Phase 1B.

| Column | Type | Notes |
|---|---|---|
| key | text | PK, matches `^[a-z][a-z0-9_]{1,47}$` |
| value_type | text | CHECK in (`string`, `number`, `boolean`, `enum`) |
| unit | text null | e.g. `mm`, `count` |
| display_label_ar | text | not null |
| display_label_en | text | not null |
| enum_values | jsonb null | required iff `value_type = enum`; array of `{ code, label_ar, label_en }` |
| is_variant_axis | boolean | default false; if true, key is eligible to be a variant axis |
| created_at, updated_at | timestamptz | |

### `restriction_reason_codes`

Seeded enum reference. Values at launch: `dental-professional`, `controlled-substance`, `institution-only`.

| Column | Type | Notes |
|---|---|---|
| code | text | PK |
| policy_key | text | Pointer into spec 004 authorization policies (e.g. `customer.verified-professional`) |
| label_ar | text | not null |
| label_en | text | not null |

---

## Core catalog tables

### `categories`

Materialized-path tree per research.md §1.

| Column | Type | Notes |
|---|---|---|
| id | uuid | PK |
| parent_id | uuid null | FK → `categories.id`, nullable for root children |
| path | text | materialized path (e.g. `0001.0003`), maintained by handler |
| depth | smallint | CHECK ≤ 6 per FR-001 |
| position | int | 0-based within siblings; reorder handler rewrites in one txn |
| active | boolean | default true |
| name_ar, name_en | text | not null |
| slug_ar, slug_en | text | not null; unique per parent |
| market_code, owner_id, vendor_id, deleted_at | | standard columns |
| xmin | xid | Postgres system column, mapped as row-version |

Indexes: `(parent_id, position)`, `(path)` (text_pattern_ops for `LIKE 'prefix.%'`), partial unique `(parent_id, slug_ar) where deleted_at is null`, partial unique `(parent_id, slug_en) where deleted_at is null`.

### `brands`

| Column | Type | Notes |
|---|---|---|
| id | uuid | PK |
| slug | text | unique; partial unique `where deleted_at is null` |
| name_ar, name_en | text | not null |
| description_ar, description_en | text | not null |
| origin_country_code | char(2) null | ISO 3166-1 alpha-2 |
| logo_media_id | uuid null | FK → `product_media.id` (dedicated brand media path permitted) |
| active | boolean | default true |
| market_code, owner_id, vendor_id, deleted_at, created_at, updated_at, xmin | | standard |

### `manufacturers`

| Column | Type | Notes |
|---|---|---|
| id | uuid | PK |
| name_ar, name_en | text | not null |
| legal_name | text | not null |
| regulatory_registration_number | text null | |
| market_code, owner_id, vendor_id, deleted_at, created_at, updated_at, xmin | | standard |

### `products`

Shared content; owns 1..N variants.

| Column | Type | Notes |
|---|---|---|
| id | uuid | PK |
| brand_id | uuid | FK → `brands.id` |
| manufacturer_id | uuid null | FK → `manufacturers.id` |
| name_ar, name_en | text | not null |
| marketing_description_ar, marketing_description_en | text | not null |
| short_description_ar, short_description_en | text | not null |
| publish_status | text | CHECK in (`draft`, `published`, `archived`), default `draft` |
| active | boolean | default true; independent of publish_status |
| restricted_for_purchase | boolean | default false |
| restriction_reason_code | text null | FK → `restriction_reason_codes.code`; required when `restricted_for_purchase = true` |
| restriction_rationale_ar, restriction_rationale_en | text null | required when `restricted_for_purchase = true` |
| market_code, owner_id, vendor_id, deleted_at, created_at, updated_at, xmin | | standard |

CHECK constraint: `restricted_for_purchase = false OR (restriction_reason_code IS NOT NULL AND restriction_rationale_ar IS NOT NULL AND restriction_rationale_en IS NOT NULL)`.

### `product_variants`

Child of `products`; owns the sellable unit.

| Column | Type | Notes |
|---|---|---|
| id | uuid | PK |
| product_id | uuid | FK → `products.id` on delete restrict |
| sku | text | NOT NULL; matches `^[A-Z0-9][A-Z0-9-]{2,31}$` |
| barcode | text null | |
| position | int | 0-based within product |
| status | text | CHECK in (`active`, `inactive`, `archived`), default `active` |
| market_code, owner_id, vendor_id, deleted_at, created_at, updated_at, xmin | | standard |

Partial unique index: `CREATE UNIQUE INDEX ix_product_variants_sku_active ON catalog.product_variants (sku) WHERE status <> 'archived' AND deleted_at IS NULL;` per Clarification Q3.

### `product_variant_axes`

Typed axis values per variant (e.g. `pack_size = 100`, `gauge = 27G`).

| Column | Type | Notes |
|---|---|---|
| variant_id | uuid | FK → `product_variants.id` on delete cascade |
| key | text | FK → `taxonomy_keys.key` with `is_variant_axis = true` |
| value_text | text null | |
| value_num | numeric null | |
| value_bool | boolean null | |
| enum_code | text null | |
| PK | (variant_id, key) | |

CHECK: exactly one of `value_text / value_num / value_bool / enum_code` populated, matching the key's `value_type`.

### `product_categories`

Many-to-many junction.

| Column | Type |
|---|---|
| product_id | uuid, FK products |
| category_id | uuid, FK categories |
| PK | (product_id, category_id) |

### `product_attributes`

Typed attribute pairs (product-level; variants overlay via `variant_attribute_overlays`).

| Column | Type | Notes |
|---|---|---|
| product_id | uuid | FK → `products.id` on delete cascade |
| key | text | FK → `taxonomy_keys.key` |
| value_text | text null | |
| value_num | numeric null | |
| value_bool | boolean null | |
| enum_code | text null | |
| PK | (product_id, key) | |

Same CHECK as axes (exactly one typed value column).

### `variant_attribute_overlays`

Optional per-variant overrides/additions to `product_attributes`.

| Column | Type | Notes |
|---|---|---|
| variant_id | uuid | FK → `product_variants.id` on delete cascade |
| key | text | FK → `taxonomy_keys.key` |
| value_text, value_num, value_bool, enum_code | | same CHECK |
| PK | (variant_id, key) | |

### `product_media`

Ordered image collection; brand logos reuse this table via `logo_media_id`.

| Column | Type | Notes |
|---|---|---|
| id | uuid | PK |
| product_id | uuid null | FK → `products.id` (nullable to allow brand-owned media) |
| brand_id | uuid null | FK → `brands.id` |
| variant_id | uuid null | FK → `product_variants.id` for variant-specific overlay |
| storage_ref | text | opaque object-storage key (spec 003) |
| mime_type | text | CHECK in (`image/jpeg`, `image/png`, `image/webp`, `image/avif`) |
| size_bytes | int | CHECK ≤ 8 388 608 (8 MB) |
| position | int | 0-based within owner |
| is_primary | boolean | at most one per (product_id) via partial unique index |
| alt_text_ar, alt_text_en | text | not null |
| virus_scan_verdict | text | CHECK in (`clean`) — rows only inserted on clean verdict |
| market_code, owner_id, vendor_id, deleted_at, created_at, updated_at, xmin | | standard |

CHECK: exactly one of (`product_id`, `brand_id`) is non-null.

### `product_documents`

| Column | Type | Notes |
|---|---|---|
| id | uuid | PK |
| product_id | uuid | FK → `products.id` |
| type_tag | text | CHECK in (`spec-sheet`, `instructions-for-use`, `certification`, `external-video-link`) |
| title_ar, title_en | text | not null |
| storage_ref | text null | NULL iff `type_tag = external-video-link` |
| external_url | text null | NOT NULL iff `type_tag = external-video-link` |
| mime_type | text null | CHECK when not null in (`application/pdf`, `image/png`) |
| size_bytes | int null | CHECK ≤ 20 971 520 (20 MB) when storage_ref present |
| virus_scan_verdict | text null | `clean` when `storage_ref` present; null for external links |
| market_code, owner_id, vendor_id, deleted_at, created_at, updated_at, xmin | | standard |

CHECK: `(storage_ref IS NOT NULL) <> (external_url IS NOT NULL)` i.e. one-of.

---

## Product publish state machine (FR-014)

```
draft ──(admin:catalog.publish + FR-015 parity check pass)──▶ published
published ──(admin:catalog.publish)──▶ archived
archived ──(admin:catalog.publish)──▶ draft
```

Every transition writes a `catalog.product.<transition>` audit event with actor, before, after, correlation id (FR-026). Publish handler also emits `ProductPublished` domain event for spec 006.

## Variant status state machine

```
active ◀──▶ inactive ──▶ archived
```

Archived → active reactivation is permitted iff SKU is not taken by another non-archived variant (FR-009a + Clarification Q3). Inactive → archived writes the terminal audit + releases SKU for reuse.

## Category lifecycle

Create → (optional) move → (optional) deactivate → (optional) reactivate. Delete is soft only, and only if no products reference the category AND no active children exist.

---

## Delegation interfaces (ports into other specs)

- `IObjectStorage.UploadAsync(mime, bytes, key) → storageRef` — spec 003.
- `IVirusScanner.ScanAsync(storageRef) → Verdict` — spec 003.
- `IAuditEventPublisher.PublishAsync(AuditEvent)` — spec 003 audit-log module.
- `IVerifiedProfessionalPolicy.AuthorizeAsync(customerId) → Decision` — spec 004 `/internal/authorize` call.
- `IVariantAvailabilityReader.ReadAsync(variantIds) → map<variantId, bool>` — spec 008 (default `StaticTrueAvailabilityReader` until spec 008 ships).
- `IProductPriceTokenRenderer.Render(variantId, marketCode) → PriceToken` — internal; consumed by spec 007-a at resolution time.
