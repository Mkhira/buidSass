# Quickstart — Search v1 (Spec 006)

## Prerequisites
- Branch `phase-1B-specs`.
- Spec 005 merged (outbox + projection shape).
- Spec 004 admin JWT + `search.reindex` / `search.read` permissions.
- A1 env: `scripts/dev/up` brings Meilisearch on `:7700`.

## 30-minute walk-through
1. **Primitives.** `ISearchEngine` + `MeilisearchSearchEngine`; `ArabicNormalizer`; `ProductSearchProjection` DTO; `IndexNames`.
2. **Persistence.** `search_indexer_cursor`, `reindex_jobs`; migration `Search_Initial`.
3. **Bootstrap.** `SearchBootstrapHostedService` ensures 4 indexes exist with settings + synonyms + stopwords.
4. **Outbox subscriber.** `SearchIndexerWorker` polls `catalog.catalog_outbox` every 2 s; per event fans out to the matching `(market, locale)` indexes.
5. **Customer slices.** `SearchProducts`, `Autocomplete`, `LookupBySkuOrBarcode`. Empty query → featured fallback.
6. **Admin slices.** `Reindex` (SSE), `Health`, `ListJobs`.
7. **Synonyms.** `synonyms.ar.yaml`, `synonyms.en.yaml` — start with 20 pairs per locale, expand later.
8. **Tests.** Normalizer unit tests, Testcontainers Meili integration tests, contract tests (one per FR). Gold-standard AR dataset drives SC-006.
9. **Observability.** Structured query log, `search_indexer_lag_seconds` gauge.
10. **AR editorial pass on `search.ar.icu`.**

## DoD
- [ ] 21 FRs → ≥ 1 contract test each.
- [ ] 9 SCs → measurable check.
- [ ] Cross-market leakage integration test green (SC-007).
- [ ] Reindex idempotency test green.
- [ ] Indexer lag p95 ≤ 5 s under a 100-event burst.
- [ ] Autocomplete p95 ≤ 50 ms on 3-char queries, warm cache.
- [ ] AR gold-standard dataset ≥ 500 pairs, coverage ≥ 99 %.
- [ ] Fingerprint + constitution check on PR.
