---
description: "Dependency-ordered tasks for spec 006 — search"
---

# Tasks: Search v1

**Input**: spec.md (21 FRs, 9 SCs, 6 user stories), plan.md, research.md, data-model.md, contracts/search-contract.md.

## Phase 1: Setup
- [ ] T001 Create module tree `services/backend_api/Modules/Search/{Primitives,Primitives/Normalization,Customer,Admin,Workers,Entities,Persistence/{Configurations,Migrations},Synonyms,Messages}` + tests `tests/Search.Tests/{Unit,Integration,Contract,Resources}`
- [ ] T002 Register `AddSearchModule` in `Modules/Search/SearchModule.cs`; wire into `Program.cs`
- [ ] T003 [P] Add NuGet: `Meilisearch` ≥ 0.15.*; reuse `YamlDotNet`

## Phase 2: Foundational
### Primitives
- [ ] T004 [P] `ISearchEngine` interface in `Modules/Search/Primitives/ISearchEngine.cs`
- [ ] T005 [P] `MeilisearchSearchEngine` implementation in `Modules/Search/Primitives/MeilisearchSearchEngine.cs`
- [ ] T006 [P] `ArabicNormalizer` in `Modules/Search/Primitives/Normalization/ArabicNormalizer.cs`
- [ ] T007 [P] `ProductSearchProjection` DTO + mapper from `ProductPublishedEvent` in `Modules/Search/Primitives/ProductSearchProjection.cs`
- [ ] T008 [P] `IndexNames` + `SearchIndexConfig` in `Modules/Search/Primitives/IndexNames.cs`
- [ ] T009 [P] `SearchBootstrapHostedService` (ensures indexes + settings + synonyms) in `Modules/Search/Primitives/SearchBootstrapHostedService.cs`

### Persistence
- [ ] T010 `SearchIndexerCursor` + `ReindexJob` entities in `Modules/Search/Entities/*.cs`
- [ ] T011 EF configurations in `Modules/Search/Persistence/Configurations/*.cs`
- [ ] T012 `SearchDbContext` with unique partial index on active reindex jobs
- [ ] T013 Migration `Search_Initial`
- [ ] T014 Synonyms seed files `Modules/Search/Synonyms/{synonyms,stopwords}.{ar,en}.{yaml,txt}` (skeleton, 20 pairs/locale)
- [ ] T015 Arabic normalizer unit tests `tests/Search.Tests/Unit/ArabicNormalizerTests.cs` (40+ fold cases)
- [ ] T016 `SearchTestFactory` + Testcontainers Meili + Postgres in `tests/Search.Tests/Infrastructure/*.cs`

## Phase 3: US1/US2/US3 — Customer search (P1) 🎯 MVP
- [ ] T017 [P] [US1] Contract test `SearchProducts_ArabicQuery_FoldsDiacritics` at `tests/Search.Tests/Contract/Customer/SearchProductsContractTests.cs`
- [ ] T018 [P] [US1] Contract test `SearchProducts_FacetFilter_NarrowsResults` in same file
- [ ] T019 [P] [US1] Contract test `SearchProducts_RestrictedProduct_SurfacesWithFlag` in same file (P8)
- [ ] T020 [P] [US2] Contract test `Lookup_ExactSku_ReturnsSingleHit` at `tests/Search.Tests/Contract/Customer/LookupContractTests.cs`
- [ ] T021 [P] [US2] Contract test `Lookup_Barcode_Under100ms` in same file
- [ ] T022 [P] [US3] Contract test `Autocomplete_ThreeChars_Under50ms` at `tests/Search.Tests/Contract/Customer/AutocompleteContractTests.cs`
- [ ] T023 [P] [US1] Integration test `CrossMarket_NoLeakage` (SC-007) at `tests/Search.Tests/Integration/CrossMarketIsolationTests.cs`
- [ ] T024 [US1] Implement `Customer/SearchProducts/{Request,Handler,Endpoint}.cs`
- [ ] T025 [US2] Implement `Customer/LookupBySkuOrBarcode/*.cs`
- [ ] T026 [US3] Implement `Customer/Autocomplete/*.cs`
- [ ] T027 [US1] Implement empty-query → featured fallback (calls spec 005 read model)
- [ ] T028 [US1] Populate `Messages/search.{ar,en}.icu` reason codes

## Phase 4: US4 — Indexer worker (P1)
- [ ] T029 [P] [US4] Integration test `Indexer_PublishedEvent_SearchableWithin5s` at `tests/Search.Tests/Integration/IndexerLagTests.cs`
- [ ] T030 [P] [US4] Integration test `Indexer_ArchivedEvent_RemovesFromAllIndexes` in same file
- [ ] T031 [P] [US4] Integration test `Indexer_Redelivery_IsIdempotent` in same file
- [ ] T032 [US4] Implement `Workers/SearchIndexerWorker.cs` (2 s poll, cursor-advance, batch upsert, 404-tolerant deletes)
- [ ] T033 [US4] Wire cursor `FOR UPDATE SKIP LOCKED` to allow future horizontal scaling

## Phase 5: US5 — Admin reindex (P2)
- [ ] T034 [P] [US5] Contract test `Reindex_Started_ReturnsJobAndStream` at `tests/Search.Tests/Contract/Admin/ReindexContractTests.cs`
- [ ] T035 [P] [US5] Contract test `Reindex_Concurrent_Returns409` in same file
- [ ] T036 [P] [US5] Integration test `Reindex_WhileLive_NoEventLoss` at `tests/Search.Tests/Integration/ReindexLiveTests.cs`
- [ ] T037 [US5] Implement `Admin/Reindex/{Request,Handler,Endpoint}.cs` with SSE channel
- [ ] T038 [US5] Implement `Admin/Health/{Request,Handler,Endpoint}.cs`
- [ ] T039 [US5] Implement `Admin/ListJobs/*.cs`

## Phase 6: US6 — Synonyms (P2)
- [ ] T040 [P] [US6] Contract test `Synonyms_LoadedAtBoot_ArDental_Expands` at `tests/Search.Tests/Contract/Customer/SynonymsContractTests.cs`
- [ ] T041 [US6] Implement `SynonymsSeeder` invoked by `SearchBootstrapHostedService`

## Phase 7: Observability + Polish
- [ ] T042 [P] Structured query log emitter (FR-020, SC-008) in `Modules/Search/Primitives/QueryLogger.cs`
- [ ] T043 [P] Metrics: `search_indexer_lag_seconds` gauge + `search_query_latency_ms` histogram
- [ ] T044 [P] Gold-standard AR dataset `tests/Search.Tests/Resources/ar-gold.jsonl` (≥ 500 pairs) + `ArabicCoverageTests` (SC-006)
- [ ] T045 [P] AR editorial pass on `search.ar.icu`; add `needs-ar-editorial-review` label if any key un-reviewed
- [ ] T046 [P] OpenAPI regeneration + contract diff green (Guardrail #2)
- [ ] T047 Fingerprint + DoD walk-through; attach query-log spot check

**Totals**: 47 tasks across 7 phases. MVP = Phases 1 + 2 + 3 + 4.
