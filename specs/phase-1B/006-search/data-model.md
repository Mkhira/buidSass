# Phase 1 Data Model: Search (006)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Two data surfaces: (1) **search index document** (Meilisearch-side, per-market), (2) **module-owned Postgres tables** (reindex job tracking, dead-letter, audit source of truth for jobs).

---

## 1. Search Index Document (`products_<market>`)

One document per **product** (not per variant — variant SKUs concatenated into a searchable array; matches spec 005 variant grain).

### Fields

| Field | Type | Indexed | Filterable | Facetable | Sortable | Notes |
|---|---|---|---|---|---|---|
| `id` | string (ULID) | primary key | yes | — | — | Document identity; matches `products.id` in catalog |
| `market_code` | enum(`eg`,`ksa`) | no | yes | — | — | Per-index redundant but kept for diagnostics |
| `schema_version` | int | no | no | — | — | See R4; bootstrap gate |
| `name_ar` | string | yes (weight 1) | — | — | — | Dual-language field |
| `name_en` | string | yes (weight 1) | — | — | — | |
| `description_ar` | string | yes (weight 3) | — | — | — | Lower rank than name |
| `description_en` | string | yes (weight 3) | — | — | — | |
| `brand_id` | string | — | yes | yes | — | |
| `brand_name_ar` | string | yes (weight 2) | — | — | — | Denormalized from catalog |
| `brand_name_en` | string | yes (weight 2) | — | — | — | |
| `category_ids` | string[] | — | yes | yes | — | All ancestor category IDs for hierarchical facets |
| `category_names_ar` | string[] | yes (weight 3) | — | — | — | |
| `category_names_en` | string[] | yes (weight 3) | — | — | — | |
| `skus` | string[] | yes (weight 0, exactness boost) | yes | — | — | All non-archived variant SKUs; typo disabled |
| `barcodes` | string[] | yes (weight 0, exactness boost) | yes | — | — | Typo disabled |
| `attributes` | object | — | yes | yes | — | Flat keyed map (`attributes.material`, `attributes.sterile`) — taxonomy keys from catalog |
| `price_bucket` | enum | — | yes | yes | — | Pre-bucketed; see R8 |
| `availability_state` | enum(`in_stock`,`low`,`out`,`preorder`) | — | yes | yes | — | Snapshot from spec 008 event |
| `restricted` | bool | — | yes | yes | — | True if purchase gated |
| `restriction_reason_code` | string | — | yes | — | — | FK to `restriction_reason_codes` |
| `popularity_desc` | int | — | — | — | yes | Reserved for spec 028; defaults 0 |
| `created_at` | datetime | — | yes | — | yes | For "new arrivals" sort |
| `price_token` | string | — | no | — | — | Opaque handle resolved by spec 007-a |
| `thumbnail_url` | string | — | no | — | — | CDN URL from spec 005 media |
| `text_all` | string | yes (weight 5) | — | — | — | Concatenation of name/brand/category/description (both languages) — fallback cross-lang match |

### Index Settings

- **Ranking rules**: `words, typo, proximity, attribute, sort, exactness, popularity_desc:desc`
- **Searchable attributes (ordered)**: `skus, barcodes, name_ar, name_en, brand_name_ar, brand_name_en, category_names_ar, category_names_en, description_ar, description_en, text_all`
- **Filterable attributes**: `market_code, brand_id, category_ids, skus, barcodes, attributes.*, price_bucket, availability_state, restricted, restriction_reason_code, created_at`
- **Facetable attributes**: `brand_id, category_ids, attributes.*, price_bucket, availability_state, restricted`
- **Sortable attributes**: `created_at, popularity_desc`
- **Typo tolerance**: `minWordSizeForTypos.oneTypo=4, twoTypos=8; disableOnAttributes=[skus, barcodes]`
- **Stop words**: loaded from `stopwords.ar.yaml` + `stopwords.en.yaml` (see R10)
- **Synonyms**: loaded from `synonyms.yaml`

---

## 2. Postgres Tables (module-owned, schema `search`)

### 2.1 `search.reindex_jobs`

Tracks admin-initiated full reindex lifecycle. Also acts as audit source of truth for job state (supplementing `audit_events`).

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `ulid` | PK | |
| `market_code` | `text` | NOT NULL, CHECK in (`eg`,`ksa`) | |
| `scope` | `text` | NOT NULL, CHECK in (`full`,`subset`) | `subset` reserved for future |
| `subset_filter` | `jsonb` | NULL | e.g., `{"category_id":"…"}` |
| `state` | `text` | NOT NULL, CHECK in (`queued`,`running`,`succeeded`,`failed`,`cancelled`) | |
| `total_products` | `int` | NULL | Populated after enumeration |
| `processed_products` | `int` | NOT NULL DEFAULT 0 | |
| `failed_products` | `int` | NOT NULL DEFAULT 0 | |
| `started_at` | `timestamptz` | NULL | |
| `completed_at` | `timestamptz` | NULL | |
| `triggered_by` | `ulid` | NOT NULL | FK to identity subject (spec 004) |
| `correlation_id` | `ulid` | NOT NULL | |
| `error_summary` | `text` | NULL | Truncated top error reason |
| `xmin_row_version` | `xid` | system | Concurrency guard |

**Indexes**: `(state, started_at DESC)` for active-job lookup; `(market_code, started_at DESC)` for per-market history.

**Invariants**:
- Only one row with `state IN ('queued','running')` per `market_code` at any time (enforced via partial unique index `uq_reindex_active_per_market`).
- State transitions: `queued → running → (succeeded | failed | cancelled)`; no other transitions allowed.

### 2.2 `search.reindex_dead_letter`

Incremental-reindex failures after retry exhaustion (R5).

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `id` | `ulid` | PK | |
| `market_code` | `text` | NOT NULL | |
| `command_type` | `text` | NOT NULL, CHECK in (`upsert`,`delete`) | |
| `product_id` | `ulid` | NOT NULL | |
| `source_event_id` | `ulid` | NOT NULL | FK to catalog event |
| `payload` | `jsonb` | NOT NULL | Original command for replay |
| `last_error` | `text` | NOT NULL | |
| `attempts` | `int` | NOT NULL | |
| `created_at` | `timestamptz` | NOT NULL DEFAULT `now()` | |
| `resolved_at` | `timestamptz` | NULL | Set when admin replays successfully |

**Indexes**: `(resolved_at NULLS FIRST, created_at)` for operator dashboard.

### 2.3 `search.provider_health_snapshots`

Periodic provider health writes (for `/admin/search/health` stability window).

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `recorded_at` | `timestamptz` | NOT NULL | |
| `provider` | `text` | NOT NULL | `meilisearch` |
| `market_code` | `text` | NOT NULL | |
| `status` | `text` | NOT NULL, CHECK in (`up`,`degraded`,`down`) | |
| `index_doc_count` | `bigint` | NULL | |
| `queue_depth` | `int` | NULL | In-process channel depth |
| `dead_letter_open` | `int` | NULL | Count where `resolved_at IS NULL` |

**Retention**: rolling 7 days (cleanup job; trivial).

---

## 3. Seed Files (code-resident, not DB)

### 3.1 `Features/Search/Seeds/synonyms.yaml`

```yaml
- canonical: braces
  variants: [brackets, orthodontic-brackets, تقويم, أقواس]
- canonical: anaesthetic
  variants: [anesthetic, مخدر, بنج]
# …domain-curated list, launch set ~60 entries
```

### 3.2 `Features/Search/Seeds/stopwords.ar.yaml`

```yaml
- في
- من
- على
- إلى
# …curated AR stop list
```

### 3.3 `Features/Search/Seeds/stopwords.en.yaml`

```yaml
- the
- and
- of
# …curated EN stop list
```

---

## 4. DTOs (API-facing, versioned in `packages/shared_contracts/search/`)

- `SearchRequest` — `{ q, lang?, market, page?, pageSize?, filters?, sort? }`
- `SearchHit` — `{ id, name, brandName, categoryPath, thumbnailUrl, priceToken, priceBucket, availabilityState, restricted, restrictionReasonCode? }`
- `SearchResponse` — `{ hits[], totalEstimated, facets{}, page, pageSize, clampFlags, emptyState? }`
- `EmptyStatePayload` — `{ didYouMean?, suggestedFilterRelaxations[], suggestions[] }`
- `AutocompleteResponse` — `{ products[], brands[], categories[] }`
- `ReindexJobDTO` — mirrors `search.reindex_jobs` with camelCase and ISO timestamps
- `ErrorEnvelope` — `{ code, messageEn, messageAr, details?, correlationId }`

---

## 5. Relationships (ER sketch)

```
catalog.products ──(events)──▶ search.index_doc (per market)
                              │
                              └─ on failure ──▶ search.reindex_dead_letter

admin action ──▶ search.reindex_jobs ──(worker)──▶ search.index_doc

identity.subject (spec 004) ──(FK)──▶ search.reindex_jobs.triggered_by
                                      │
                                      └─(policy "search.reindex")─▶ admin endpoints
```

---

## 6. Migrations

- `V006_001__create_search_schema.sql` — schema + three tables + indexes + partial unique constraint.
- `V006_002__seed_restriction_reason_mirror.sql` — no-op placeholder if spec 005 already owns the table; this module reads-only.
