# Feature Specification: Search (v1)

**Feature Number**: `006-search`
**Phase Assignment**: Phase 1B · Milestone 2 · Lane A (backend)
**Created**: 2026-04-22
**Input**: `docs/implementation-plan.md` §006; constitution Principles 4, 5, 6, 8, 12, 22, 23, 26, 27, 28, 29; ADR-005 (Meilisearch), ADR-010.

---

## Clarifications

### Session 2026-04-22

- Q1: Indexing transport from spec 005 → **B: Database outbox polled by a dedicated `SearchIndexerWorker`.** The outbox already exists (spec 005 `catalog_outbox`). Worker is the subscriber.
- Q2: Arabic normalization → **A: Pre-index tokenizer pass** doing alef/ya/ta-marbuta folding + diacritics stripping + stopwords, while keeping Meilisearch's own AR support intact. Both the indexer and query path apply the same normalization.
- Q3: Synonym authoring → **B: File-based reference data seeded at module init** (`Modules/Search/Synonyms/synonyms.*.yaml`). Admin UI for editing deferred to Phase 1.5 per implementation-plan.
- Q4: Service boundary shape → **A: `ISearchEngine` interface + `MeilisearchSearchEngine` implementation** under `Modules/Search/`. No storefront code references the Meilisearch SDK directly.
- Q5: Index partitioning → **B: One index per `(market_code, locale)` pair** (4 launch indexes: eg-ar, eg-en, ksa-ar, ksa-en). Simpler than a single fat index with locale filters; cheaper re-indexing per market rollout.

---

## User Scenarios & Testing

### User Story 1 — Search from the storefront (Priority: P1)

A dental clinic owner in Cairo types "قفازات جراحية" (surgical gloves) into the Arabic storefront. Results appear in < 300 ms with correct matches despite her using `قفازات` while products may be indexed as `قفّازات` (with shadda) or `قفازات جراحيه` (alternate ta-marbuta spelling).

**Acceptance Scenarios**:
1. *Given* the catalog seeded with 20 surgical glove products, *when* she searches `قفازات جراحية`, *then* the result set includes products whose raw text has `قفّازات جراحيه` or `قفازات الجراحيه`.
2. *Given* the same scenario in English locale, *when* she searches `"surgical glove"`, *then* matches include products tagged with the plural `gloves` and variants like `surgical-grade gloves`.
3. *Given* a misspelling `"surgcal glove"` (1 edit), *then* results still surface primary matches (typo tolerance).
4. *Given* results displayed, *when* she applies a brand facet and a price-range facet, *then* facet counts update and result set narrows without a full re-search.

---

### User Story 2 — SKU and barcode lookup (Priority: P1)

An admin at the warehouse scans a barcode or types an exact SKU in the admin storefront search. Result is the single exact match, surfaced first, with zero latency penalty.

**Acceptance Scenarios**:
1. *Given* a product with SKU `DX-001-KSA`, *when* an admin types that exact SKU, *then* the exact SKU hit is the first and only result (above any typo-tolerant neighbors).
2. *Given* a product with barcode `6291000000001`, *when* the barcode is submitted as a search query, *then* the match is surfaced within < 100 ms.

---

### User Story 3 — Autocomplete as the customer types (Priority: P1)

The customer starts typing `قفا` and sees the top 5 completions inside 50 ms per keystroke.

**Acceptance Scenarios**:
1. *Given* typing `قف`, *then* autocomplete returns top 5 product titles + top 3 category suggestions.
2. *Given* typing something with no matches, *then* autocomplete returns an empty list with a `noResultsReason` hint (`no_matches` \| `restricted_market`).
3. *Given* a restricted product matches, *then* autocomplete still surfaces it with a `restricted: true` flag — per Principle 8 the product is visible.

---

### User Story 4 — Incremental indexing of catalog events (Priority: P1)

A catalog editor publishes a new product. Within 5 s the product is searchable. They then archive a product and it disappears from search within 5 s.

**Acceptance Scenarios**:
1. *Given* a product published in spec 005, *when* the indexer runs its next cycle (≤ 2 s poll + ≤ 2 s index flush), *then* the product is queryable in its `(market, locale)` indexes.
2. *Given* a product archived, *when* the indexer processes the event, *then* the product document is removed from all its indexes.
3. *Given* a product edited (name, attributes, primary image), *when* the indexer processes the `product.updated` event, *then* the document is upserted with the latest projection.

---

### User Story 5 — Initial index bootstrap (Priority: P2)

On first deploy (or after an index wipe), an operator triggers `POST /v1/admin/search/reindex?index=ksa-ar` to rebuild the index from the catalog source of truth.

**Acceptance Scenarios**:
1. *Given* an empty `ksa-ar` index, *when* the operator triggers reindex, *then* the worker streams all published products in the `ksa` market with `ar` content into Meilisearch, emitting progress events.
2. *Given* an in-progress reindex, *when* a new `product.published` event arrives, *then* the event is queued and applied after bootstrap completes — no lost event, no double-indexing.

---

### User Story 6 — Synonyms make domain queries work (Priority: P2)

A dentist types `طقم أسنان` (dental prosthesis); results include products tagged `طقم صناعي` (artificial set) because the synonym bundle pairs them.

**Acceptance Scenarios**:
1. *Given* the AR synonyms bundle maps `طقم أسنان ↔ طقم صناعي ↔ prosthesis`, *when* the customer searches any of those terms, *then* results from all three-tagged products appear.
2. *Given* synonyms updated in the YAML bundle + redeployed, *then* queries use the new mappings within one service restart.

---

### Edge Cases
1. Meilisearch down at request time → return `503 search.engine_unavailable` with `Retry-After: 5`; do not fall through to a DB full-text scan (would be too slow and inconsistent).
2. Indexer lag > 60 s → operator alert emitted; admin dashboard shows lag metric.
3. Product with no primary media → still indexed with a `missing_media` flag; frontend picks a placeholder.
4. Mixed-locale query ("قفازات gloves") → use the customer's selected locale index first, fall back to the other locale with a `x-search-locale-fallback` header.
5. Reindex invoked twice concurrently → second call returns `409 search.reindex.in_progress` with current job id.
6. Typo tolerance interacts badly with short tokens (≤ 3 chars) → tolerance disabled below 3 chars.
7. Restricted products must appear in search with restriction badge; NEVER hidden (Principle 8).
8. Market-scoped query: EG customer must never see a product limited to KSA. Enforced at index boundary (separate indexes).
9. Seed-data products flagged `restricted_markets = ['ksa']` → indexed in `ksa-ar`/`ksa-en` with `restricted=true`; indexed in `eg-*` with `restricted=false`.
10. Empty query → return the market-featured set (from spec 005 published-featured list); not an error.

---

## Requirements (FR-)

- **FR-001**: System MUST expose a customer search endpoint `POST /v1/customer/search/products` accepting `{ query, marketCode, locale, filters, sort, page, pageSize }` and returning `{ hits, facets, totalEstimate, queryDurationMs, engineLatencyMs }`.
- **FR-002**: System MUST maintain 4 launch indexes: `products-eg-ar`, `products-eg-en`, `products-ksa-ar`, `products-ksa-en`.
- **FR-003**: System MUST subscribe to the spec 005 `catalog_outbox` via `SearchIndexerWorker`; consumer is idempotent (at-least-once delivery expected).
- **FR-004**: Indexer lag (event committed → searchable) MUST be ≤ 5 s at p95 under normal load.
- **FR-005**: System MUST apply Arabic normalization (alef/ya/ta-marbuta folding, diacritics strip, stopwords) to both index payloads and queries before handing to the engine.
- **FR-006**: System MUST support autocomplete via `POST /v1/customer/search/autocomplete` with p95 ≤ 50 ms per keystroke for 3-char queries against a 20k-product index.
- **FR-007**: System MUST expose SKU + barcode lookup as an exact-match shortcut: if query matches an indexed SKU or barcode exactly, that single product surfaces first regardless of relevance ranking.
- **FR-008**: System MUST support facets on `brand_id`, `category_id`, price-range buckets (`0-99`, `100-499`, `500-1999`, `2000+` minor units), `restricted` flag, `availability` (seam with spec 008; populated once spec 008 ships).
- **FR-009**: System MUST support sort modes `relevance` (default), `price-asc`, `price-desc`, `newness`, `featured`.
- **FR-010**: System MUST expose admin reindex endpoint `POST /v1/admin/search/reindex?index={name}` with progress streaming (Server-Sent Events) and a single-concurrent-job guard.
- **FR-011**: System MUST expose admin index-health endpoint `GET /v1/admin/search/health` returning indexer lag, document count per index, last-success timestamp.
- **FR-012**: Synonyms MUST be defined in `Modules/Search/Synonyms/synonyms.{ar,en}.yaml` and seeded into each index on bootstrap; changes take effect after service restart.
- **FR-013**: Typo tolerance MUST be enabled for queries ≥ 4 chars; disabled otherwise.
- **FR-014**: Restricted products MUST remain searchable (Principle 8); result payload carries `restricted: bool` and `restrictionReasonCode` fields.
- **FR-015**: System MUST never return a product for a market it is not configured for — enforced at index boundary.
- **FR-016**: The `ISearchEngine` interface MUST abstract the underlying engine (Meilisearch); no storefront/customer/admin slice references Meilisearch SDK types directly.
- **FR-017**: Indexer MUST record its progress in `search_indexer_cursor` table keyed by `(outbox_last_id_applied, index_name)` to survive restarts.
- **FR-018**: Reindex job MUST stream from the catalog read model, batch-upsert (≤ 500 docs/batch), and emit heartbeat every batch.
- **FR-019**: Search response MUST carry localized product fields per the requested locale (name, short description, media primary URL).
- **FR-020**: System MUST emit a structured log + metric for every query: `query_hash`, `marketCode`, `locale`, `resultCount`, `latencyMs`, `filters`.
- **FR-021**: Empty-query requests MUST return the "featured" product set for the market (configured in spec 005 via `featured_at` or derived from top-recency fallback) without calling the engine when possible.

### Key Entities

- **SearchIndex** — logical name per `(market, locale)`; not a DB table, tracked in config.
- **SearchIndexerCursor** — persistent cursor per index name.
- **ProductSearchProjection** — shape of a single Meilisearch document (derived from outbox event payload).
- **SynonymGroup** — entry from the YAML bundle.

---

## Success Criteria (SC-)

- **SC-001**: Customer search p95 ≤ 300 ms for a 20k-product index with 2-filter query.
- **SC-002**: Autocomplete p95 ≤ 50 ms for 3-char queries on warm cache.
- **SC-003**: SKU/barcode exact lookup p95 ≤ 100 ms.
- **SC-004**: Indexer lag p95 ≤ 5 s (FR-004).
- **SC-005**: Initial reindex of 20k products completes in ≤ 5 minutes.
- **SC-006**: Arabic normalization coverage ≥ 99 % on a gold-standard test set (500 AR query/result pairs curated by product leadership).
- **SC-007**: Zero cross-market leakage (EG query sees zero KSA-exclusive products); verified by integration test.
- **SC-008**: Every query emits a structured log line with the 6 fields listed in FR-020.
- **SC-009**: Indexer survives Meilisearch restart; no events lost (verified by fault-injection test).

---

## Dependencies

- Spec 005 (catalog) merged — this spec depends on `catalog_outbox` + the `ProductPublishedEvent` projection shape.
- A1 environments — Meilisearch container in `infra/local/docker-compose.yml` (already health-gated).
- Spec 004 — admin JWT + `search.reindex` permission.

## Assumptions

- Meilisearch v1.9+; single-node launch (HA deferred to Phase 1F).
- Synonyms bundle curated manually; size ≤ 1000 entries per locale at launch.
- No full-text fallback required — Meilisearch unavailability returns 503.
- Spec 008 (inventory) ships after this; availability facet values populated once inventory is online.

## Out of Scope

- Synonym editing UI (Phase 1.5).
- Personalization / ranking by user behavior (Phase 2).
- Vector search / semantic search (Phase 2).
- Multi-node Meilisearch HA (Phase 1F spec 029).
- Admin UI for search tuning (deferred).
