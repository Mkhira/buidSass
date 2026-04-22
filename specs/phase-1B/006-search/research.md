# Research — Search v1 (Spec 006)

**Date**: 2026-04-22

## R1 — Engine choice
- **Decision**: Meilisearch v1.9+ (ADR-005).
- **Rationale**: Strong Arabic normalization out of the box, first-class typo tolerance, simple ops for solo + AI-agent execution, good facet API.
- **Alternatives**: OpenSearch (operational overhead), Typesense (weaker AR), Postgres FTS (insufficient AR handling + no facets).

## R2 — Index partitioning
- **Decision**: 4 launch indexes `products-{eg|ksa}-{ar|en}`.
- **Rationale**: Physical isolation per market removes a whole class of leakage bugs (SC-007). Per-locale index lets each index tune its own stopwords / synonyms / tokenization.
- **Alternatives**: Single index + `market_code` + `locale` filters — rejected because filter-bypass mistakes would be silent leakages.

## R3 — Ingestion transport
- **Decision**: Poll the spec 005 `catalog.catalog_outbox` every ≤ 2 s.
- **Rationale**: Reuses existing transactional outbox; no new broker; survives restarts; lag budget 5 s is achievable.
- **Alternatives**: Push via in-proc `MediatR` notification (couples Catalog+Search tightly); Debezium/CDC (new infra); Redis streams (new infra).

## R4 — Arabic normalization
- **Decision**: Do a pre-index/pre-query pass in `.NET` that applies:
  - NFKC Unicode normalization
  - alef variants → bare alef (`أ إ آ ٱ → ا`)
  - ya variants → bare ya (`ى ئ → ي`)
  - ta-marbuta → ha (`ة → ه`) for matching (preserve original for display)
  - Diacritics (tashkeel) strip (U+064B..U+0652, U+0670)
  - Arabic tatweel strip (U+0640)
  - Stopwords list (~50 common words)
- **Rationale**: Meilisearch's own AR support is decent but not enough for dental domain (shadda variants, tab-marbuta spelling drift in UGC). Stacking our pass on top is the cheapest way to meet the SC-006 bar.
- **Alternatives**: Rely solely on Meilisearch built-in AR handling (fails editorial bar); train custom tokenizer (out of scope Phase 1B).

## R5 — Synonym management
- **Decision**: File-based `Modules/Search/Synonyms/synonyms.{ar,en}.yaml`, seeded at boot.
- **Rationale**: Versioned with code; review path through normal PR. Admin UI deferred to Phase 1.5.
- **Alternatives**: DB-backed synonyms (needs admin UI now), Meilisearch native synonyms only (no version control).

## R6 — SKU/barcode shortcut
- **Decision**: Regex-detect SKU-shaped (`^[A-Z0-9-]{4,}$`) or barcode-shaped (`^\d{8,14}$`) queries and short-circuit via the engine's exact-match filter ahead of the relevance search.
- **Rationale**: Warehouse UX expects 1 result instantly; relevance ranking adds noise.
- **Alternatives**: Rank boost only — still returns typo neighbors; rejected.

## R7 — Typo tolerance policy
- **Decision**: Meilisearch default ramped — tolerance disabled below 3 chars (Meilisearch setting), 1 edit allowed 4–8 chars, 2 edits 9+.
- **Rationale**: Short AR tokens too often collide (e.g. `قطن` vs `قطر`). Matches Meilisearch recommended defaults.

## R8 — Facet design
- **Decision**: Facets on `brand_id`, `category_id`, price-range buckets (minor units), `restricted` bool, `availability` (seam with spec 008).
- **Rationale**: Covers the canonical storefront filter surface. Price buckets avoid heavy numeric-range facet queries.

## R9 — Autocomplete design
- **Decision**: Separate endpoint, separate Meilisearch call with `limit=5`, `attributesToSearchOn=["name","brand_name"]`, engine-native prefix matching.
- **Rationale**: Lightweight; 50 ms p95 is achievable.
- **Alternatives**: Search-as-you-type on the main endpoint — increases load, harder to cache.

## R10 — Reindex concurrency
- **Decision**: Single-concurrent-job guard on `POST /v1/admin/search/reindex?index=`; second call returns `409 search.reindex.in_progress` with the active job id.
- **Rationale**: Avoids race conditions where a second operator kicks off a re-indexing while one is already running.

## R11 — Reindex streams
- **Decision**: SSE channel emits heartbeat per batch + final `completed` event with counts.
- **Rationale**: Operator needs feedback for long bootstraps (~5 min for 20k products).

## R12 — Empty-query handling
- **Decision**: No engine call. Serve the spec 005 featured-product read-model directly.
- **Rationale**: Faster and avoids warming the relevance ranker with zero-signal input.

## Open items
- Stopword list curation: AR linguist review booked for the week of 2026-05-05 (tracked as Phase J task).
- Gold-standard AR dataset creation: product leadership owner assigned; target 500 pairs by Phase J close.
