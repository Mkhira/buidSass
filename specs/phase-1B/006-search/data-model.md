# Data Model — Search v1 (Spec 006)

**Date**: 2026-04-22. Schema: `search` (Postgres) + Meilisearch indexes.

## Postgres tables (1)

### 1. `search.search_indexer_cursor`
Tracks indexer progress per logical index so the worker survives restarts.

| column | type | notes |
|---|---|---|
| `index_name` | citext PK | e.g. `products-ksa-ar` |
| `outbox_last_id_applied` | bigint NOT NULL | last `catalog.catalog_outbox.id` successfully applied |
| `last_success_at` | timestamptz NOT NULL | |
| `lag_seconds_last_observed` | int NOT NULL | populated each tick |
| `updated_at` | timestamptz NOT NULL | |

One row per index. Reads are cheap; worker uses `SELECT ... FOR UPDATE SKIP LOCKED` to serialize concurrent worker instances.

### 2. `search.reindex_jobs`
Tracks reindex job state for the admin SSE surface.

| column | type | notes |
|---|---|---|
| `id` | uuid PK | v7 |
| `index_name` | citext NOT NULL | |
| `status` | citext NOT NULL | `pending` \| `running` \| `completed` \| `failed` |
| `started_by_account_id` | uuid NOT NULL FK → identity.accounts | |
| `started_at` | timestamptz NOT NULL | |
| `completed_at` | timestamptz NULL | |
| `docs_expected` | int NULL | |
| `docs_written` | int NOT NULL DEFAULT 0 | |
| `error` | text NULL | |

Unique partial index: `WHERE status IN ('pending','running')` on `index_name` — enforces single-concurrent-job guard at the DB.

---

## Meilisearch documents — `ProductSearchProjection`

One document per product per (market_code, locale). Same `id` (product uuid) across indexes.

```json
{
  "id": "018f01a9-…-uuid",
  "sku": "DX-001-KSA",
  "barcode": "6291000000001",
  "name": "قفازات جراحية لاتكس",
  "nameNormalized": "قفازات جراحيه لاتكس",
  "shortDescription": "…",
  "brandId": "…-uuid",
  "brandName": "3M Dental",
  "categoryIds": ["…", "…"],
  "categoryBreadcrumb": ["Consumables", "Gloves"],
  "attributes": { "size": "M", "sterile": true },
  "priceHintMinorUnits": 8500,
  "restricted": false,
  "restrictionReasonCode": null,
  "availability": "in_stock",
  "featuredAt": "2026-04-18T10:00:00Z",
  "publishedAt": "2026-04-12T08:12:00Z",
  "primaryMedia": {
    "thumbUrl": "https://…/thumb.webp",
    "cardUrl": "https://…/card.webp"
  },
  "marketCode": "ksa",
  "locale": "ar",
  "vendorId": null
}
```

**Searchable attributes** (order matters for ranking):
1. `name`
2. `nameNormalized`
3. `sku`
4. `barcode`
5. `brandName`
6. `categoryBreadcrumb`
7. `shortDescription`

**Filterable attributes**: `brandId`, `categoryIds`, `priceHintMinorUnits`, `restricted`, `availability`, `marketCode`.

**Sortable attributes**: `priceHintMinorUnits`, `publishedAt`, `featuredAt`.

**Distinct attribute**: `id` (no dupes per index).

**Typo tolerance**: `minWordSizeForTypos = { oneTypo: 4, twoTypos: 9 }`.

**Stop-words**: per-locale list seeded from `Synonyms/stopwords.{ar,en}.txt`.

**Synonyms**: seeded from `Synonyms/synonyms.{ar,en}.yaml` at boot.

---

## State machine — `ReindexJob`
States: `pending`, `running`, `completed`, `failed`.

| from | to | trigger | actor | failure |
|---|---|---|---|---|
| pending | running | worker claim | system | — |
| running | completed | all docs streamed + engine commit | system | — |
| running | failed | engine error after 3 retries | system | row records `error` |
| pending | failed | conflict detected at start | system | — |

Transitions logged to audit trail (spec 003 `audit_log_entries`).

---

## Index catalog (config, not DB)
| name | market | locale | purpose |
|---|---|---|---|
| `products-eg-ar` | eg | ar | Egypt Arabic storefront |
| `products-eg-en` | eg | en | Egypt English storefront |
| `products-ksa-ar` | ksa | ar | KSA Arabic storefront |
| `products-ksa-en` | ksa | en | KSA English storefront |

Defined in `appsettings.json` under `Search:Indexes`; bootstrap seeder ensures each exists with settings applied.
