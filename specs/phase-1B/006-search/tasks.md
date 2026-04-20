# Tasks: Search (006)

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data Model**: [data-model.md](./data-model.md) | **Contracts**: [contracts/search.openapi.yaml](./contracts/search.openapi.yaml), [contracts/events.md](./contracts/events.md)

**Feature module root**: `services/backend_api/Features/Search/`
**Test projects**: `services/backend_api/Tests/Search.Unit/`, `Search.Integration/`, `Search.Contract/`
**Shared contracts**: `packages/shared_contracts/search/`

User stories from `spec.md` (priority order):
- **US1** — Keyword search (P1)
- **US2** — Facets + sort (P1)
- **US3** — Autocomplete (P1)
- **US4** — Admin full reindex (P2)
- **US5** — Event-driven incremental reindex (P1)
- **US6** — Provider swappability (P2)

**MVP** = Phase 1 + Phase 2 + Phases 3–5 (US1, US2, US3, US5).

---

## Phase 1 — Setup

- [ ] T001 Create feature module skeleton at `services/backend_api/Features/Search/` with subfolders `Query/`, `Indexing/`, `Reindex/`, `Provider/`, `Normalization/`, `Seeds/`, `Observability/`, `Persistence/`, `Shared/`
- [ ] T002 Add test projects `services/backend_api/Tests/Search.Unit/Search.Unit.csproj`, `Search.Integration/Search.Integration.csproj`, `Search.Contract/Search.Contract.csproj` (xUnit + FluentAssertions)
- [ ] T003 [P] Add NuGet dependencies: `Meilisearch` client, `YamlDotNet`, `Polly`, `FsCheck.xUnit`, `Testcontainers.Meilisearch` in the relevant csproj files
- [ ] T004 [P] Register Search module DI composition root in `services/backend_api/Program.cs` under `AddSearchModule()` extension in `Features/Search/SearchModuleExtensions.cs`
- [ ] T005 [P] Add config section `Search` to `services/backend_api/appsettings.json` + `appsettings.Development.json` (provider, Meilisearch URL, master key, index naming)
- [ ] T006 [P] Add Meilisearch to `infra/dev/docker-compose.yml` with pinned version and dev master key

## Phase 2 — Foundational (blocking prerequisites)

- [ ] T007 Create EF Core migration `V006_001__create_search_schema.sql` for `search.reindex_jobs`, `search.reindex_dead_letter`, `search.provider_health_snapshots` including partial unique index `uq_reindex_active_per_market` (see `data-model.md` §2)
- [ ] T008 Implement `ISearchProvider` port in `Features/Search/Provider/ISearchProvider.cs` with `QueryAsync`, `AutocompleteAsync`, `UpsertDocumentsAsync`, `DeleteDocumentsAsync`, `EnsureIndexAsync` (see research R1)
- [ ] T009 [P] Implement DTOs in `Features/Search/Shared/` — `SearchRequest`, `SearchHit`, `SearchResponse`, `EmptyStatePayload`, `AutocompleteResponse`, `ReindexJobDTO`, `ErrorEnvelope` (mirror OpenAPI in `contracts/search.openapi.yaml`)
- [ ] T010 [P] Implement `Features/Search/Shared/SearchErrorCodes.cs` enumerating the `search.*` codes + HTTP status mapping (R13)
- [ ] T011 [P] Implement `Features/Search/Normalization/ArabicQueryNormalizer.cs` (alef/ya/ta-marbuta folding, tatweel strip, diacritic removal) per research R2
- [ ] T012 [P] Implement seed loaders `Features/Search/Seeds/SeedLoader.cs` reading `synonyms.yaml`, `stopwords.ar.yaml`, `stopwords.en.yaml`, fail-fast on schema errors (R10)
- [ ] T013 [P] Ship initial seed YAMLs at `Features/Search/Seeds/synonyms.yaml`, `stopwords.ar.yaml`, `stopwords.en.yaml` with launch entries
- [ ] T014 Implement `Features/Search/Provider/Meilisearch/MeilisearchProvider.cs` (adapter), including `IndexSettings.cs` for ranking rules, searchable/filterable/facetable/sortable config, typo tolerance per R6/R7
- [ ] T015 Implement `Features/Search/Provider/Static/StaticSearchProvider.cs` stub — in-memory dictionary indexed by market, applies same `ArabicQueryNormalizer` so parity is testable (R1)
- [ ] T016 [P] Implement `Features/Search/Observability/SearchQueryLogger.cs` emitting structured fields (query_hash, query_len, lang, market, hit_count, latency_ms, clamp_flags, caller_hash, provider) per R11; HMAC-SHA256 for caller_hash with rotating key
- [ ] T017 [P] Publish OpenAPI contract to `packages/shared_contracts/search/search.openapi.yaml` (copy-of-truth from `contracts/search.openapi.yaml`) and wire the contract-diff CI gate
- [ ] T018 Wire RBAC policy `search.reindex` via spec 004's `AddAuthorization` pipeline; add policy constant in `Features/Search/Shared/SearchAuthorizationPolicies.cs`

## Phase 3 — US1: Keyword Search (P1)

**Goal**: Customer can search products by keyword in AR or EN, see priced hits (via priceToken) within SC-002 latency.
**Independent test criteria**: Given a seeded catalog, a GET `/search?market=ksa&q=…` returns relevant hits with bilingual matching, Arabic normalization applied, SKU short-circuit working, restricted products visible with priceToken.

- [ ] T019 [US1] Implement `Features/Search/Query/SearchQuery.cs` MediatR request + validator (FluentValidation: q ≤ 200, market required, page/pageSize bounds)
- [ ] T020 [US1] Implement `Features/Search/Query/SearchQueryHandler.cs` — normalize query, delegate to `ISearchProvider.QueryAsync`, map provider response to `SearchResponse` DTO
- [ ] T021 [US1] Implement clamp logic in handler (query truncation, pagination depth) setting `clampFlags` per FR-018 / R12
- [ ] T022 [P] [US1] Implement restricted-product visibility: always include restricted hits with priceToken; add `restrictionReasonCode` to SearchHit (FR-003 / R15)
- [ ] T023 [US1] Implement numeric-only query detection (≥3 digits → route to SKU/barcode exact filter, bypass typo) per R7
- [ ] T024 [US1] Add minimal API endpoint `GET /search` in `Features/Search/SearchEndpoints.cs` using the handler; register with `AddSearchModule`
- [ ] T025 [P] [US1] Unit tests in `Tests/Search.Unit/Query/SearchQueryHandlerTests.cs` covering: validation, normalization invocation, clamp flags, empty query, numeric short-circuit
- [ ] T026 [P] [US1] Arabic corpus fixture in `Tests/Search.Unit/Normalization/ArabicCorpus.json` (100 query/expected-hit pairs) and `ArabicCorpusTests.cs` driving `StaticSearchProvider` — SC-005 gate ≥ 95%
- [ ] T027 [US1] Integration test in `Tests/Search.Integration/Query/SearchFlowTests.cs` using Testcontainers Meilisearch + seeded products — covers AR + EN + SKU short-circuit + restricted visibility (SC-004, SC-006)
- [ ] T028 [P] [US1] k6 script `scripts/perf/search.js` asserting p95 ≤ 500ms at 24 hits (SC-002)

## Phase 4 — US2: Facets + Sort (P1)

**Goal**: Customer can narrow results by brand/category/priceBucket/availability with intersection semantics; sort by relevance/newest/popularity.
**Independent test criteria**: Selecting filters reduces both `hits` and facet counts correctly; sort parameter changes ordering deterministically.

- [ ] T029 [US2] Extend `SearchQuery` with `filters` + `sort` per OpenAPI; validator enforces enums
- [ ] T030 [US2] Extend `MeilisearchProvider` filter builder to emit Meilisearch filter expressions from DTO filter shape (brand, category ancestor match, priceBucket, availability)
- [ ] T031 [US2] Map facet counts from Meilisearch response into `SearchResponse.facets` dictionary, preserving selected-filter intersection semantics (R8)
- [ ] T032 [P] [US2] Property tests in `Tests/Search.Unit/Query/FacetIntersectionTests.cs` using FsCheck — selecting a facet value never increases other facets' counts (FR-009)
- [ ] T033 [P] [US2] Integration test `Tests/Search.Integration/Query/FacetFlowTests.cs` seeding multi-brand/category corpus, asserting intersection + sort behavior
- [ ] T034 [P] [US2] Ensure `priceBucket` field populated at index time during upsert (derived from catalog price at reindex moment; future: read from spec 007-a pricing snapshot)

## Phase 5 — US3: Autocomplete (P1)

**Goal**: Typeahead returns ≤ 8 products + 3 brands + 3 categories under 150ms p95.
**Independent test criteria**: GET `/search/autocomplete?q=brac` returns composite payload with bounded sizes; Arabic prefixes work.

- [ ] T035 [US3] Implement `Features/Search/Query/AutocompleteQuery.cs` + handler using Meilisearch `/multi-search` for three parallel queries (R9)
- [ ] T036 [US3] Add endpoint `GET /search/autocomplete` in `SearchEndpoints.cs`
- [ ] T037 [US3] Anonymous vs authenticated context: anonymous autocomplete excludes `restricted=true` products; authenticated includes them (R15)
- [ ] T038 [P] [US3] Unit tests `Tests/Search.Unit/Query/AutocompleteHandlerTests.cs` — payload bounds, anon vs auth filtering
- [ ] T039 [P] [US3] Integration test `Tests/Search.Integration/Query/AutocompleteFlowTests.cs` + k6 perf script asserting p95 ≤ 150ms (SC-003)

## Phase 6 — US5: Event-Driven Incremental Reindex (P1)

**Goal**: Catalog domain events propagate to search index within 2s (SC-001) with at-least-once semantics via retry + dead-letter.
**Independent test criteria**: Publishing a product via spec 005 makes it searchable within 2s; forcing a provider outage lands the command in dead-letter; replay succeeds.

- [ ] T040 [US5] Implement `Features/Search/Indexing/ProductDocumentBuilder.cs` — build full `ProductDoc` from catalog read model (joins brand, categories, variants, attributes)
- [ ] T041 [US5] Implement `Features/Search/Indexing/ReindexCommand.cs` record + bounded `Channel<ReindexCommand>` in `Features/Search/Indexing/ReindexQueue.cs` (R5)
- [ ] T042 [US5] Implement `Features/Search/Indexing/ReindexWorker.cs` (`IHostedService`) — drains queue, invokes provider, Polly retry with exponential backoff, dead-letters after 5 attempts
- [ ] T043 [P] [US5] Implement notification handlers in `Features/Search/Indexing/Subscribers/` for `ProductPublished`, `ProductUpdated`, `ProductUnpublished`, `ProductArchived`, `VariantPublished`, `VariantUpdated`, `VariantArchived`, `CategoryRenamed`, `CategoryMoved`, `BrandRenamed` (see `contracts/events.md` §1)
- [ ] T044 [US5] Implement dead-letter persistence in `Features/Search/Persistence/ReindexDeadLetterRepository.cs` (EF Core against `search.reindex_dead_letter`)
- [ ] T045 [P] [US5] Unit tests `Tests/Search.Unit/Indexing/ReindexWorkerTests.cs` — retry exhaustion, dead-letter write, success clears correlation
- [ ] T046 [P] [US5] Integration test `Tests/Search.Integration/Indexing/PropagationTests.cs` — publish a product via catalog module, assert searchable within 2s (SC-001)
- [ ] T047 [P] [US5] Integration test for outage recovery: stop Testcontainers Meilisearch mid-upsert, verify dead-letter population, restart, replay succeeds

## Phase 7 — US4: Admin Full Reindex (P2)

**Goal**: Admin can trigger a market-scoped full reindex of 10k products in ≤ 5 min with audit trail.
**Independent test criteria**: POST `/admin/search/reindex` under RBAC returns job; GET `/admin/search/reindex/{id}` transitions queued→running→succeeded; audit events visible.

- [ ] T048 [US4] Implement `Features/Search/Reindex/TriggerReindexCommand.cs` + handler — validates active-job invariant (partial unique index backstop), inserts `reindex_jobs` row with state=queued, audits `search.reindex.requested`
- [ ] T049 [US4] Implement `Features/Search/Reindex/FullReindexJob.cs` — streams products from catalog in pages, pushes upsert commands into queue, updates `processed_products`/`failed_products`, final audit `succeeded`/`failed`
- [ ] T050 [US4] Implement `Features/Search/Reindex/GetReindexJobQuery.cs` + handler for status polling
- [ ] T051 [US4] Add admin endpoints `POST /admin/search/reindex`, `GET /admin/search/reindex/{jobId}`, `GET /admin/search/health`, `GET /admin/search/dead-letter`, `POST /admin/search/dead-letter/{id}/replay` in `Features/Search/AdminSearchEndpoints.cs` gated by `search.reindex` policy
- [ ] T052 [P] [US4] Unit tests `Tests/Search.Unit/Reindex/TriggerReindexHandlerTests.cs` — 409 on active-job collision, correct audit emission, market validation
- [ ] T053 [P] [US4] Integration test `Tests/Search.Integration/Reindex/FullReindexFlowTests.cs` — seed 10k products, run reindex, assert completion within 5 min (SC-007), verify audit events landed in `audit_events`
- [ ] T054 [P] [US4] Integration test `Tests/Search.Integration/Reindex/DeadLetterReplayTests.cs` for operator replay path

## Phase 8 — US6: Provider Swappability (P2)

**Goal**: Swapping `SEARCH__Provider=static` requires zero caller-code changes and passes the contract test suite.
**Independent test criteria**: Contract tests pass against both `MeilisearchProvider` and `StaticSearchProvider` using the same fixtures.

- [ ] T055 [US6] Parameterize `Tests/Search.Contract/` harness with both providers via xUnit theory data
- [ ] T056 [US6] Implement `Tests/Search.Contract/Provider/ProviderParityTests.cs` — same query/expected-hit corpus against both providers; delta ≤ 5% for non-trivial ranking cases (SC-008 gate)
- [ ] T057 [P] [US6] Document provider-swap runbook section in `quickstart.md` §5 (already stubbed) with explicit env var matrix
- [ ] T058 [P] [US6] Add DI switch in `SearchModuleExtensions.cs` binding `ISearchProvider` based on `Search:Provider` config value

## Phase 9 — Polish & Cross-Cutting

- [ ] T059 [P] Implement error-response middleware in `Features/Search/Shared/SearchExceptionHandler.cs` converting exceptions to `ErrorEnvelope` with correlation ID (FR-019)
- [ ] T060 [P] Implement rate-limit policy `search-default` (per-IP token bucket) gating `/search` and `/search/autocomplete` → 429 with envelope (FR-019)
- [ ] T061 [P] Wire OpenTelemetry spans for all handlers + provider calls; emit R11 span attributes
- [ ] T062 [P] Add health snapshot writer `Features/Search/Observability/HealthSnapshotWorker.cs` (`IHostedService`) writing to `search.provider_health_snapshots` every 60s
- [ ] T063 [P] Add OpenAPI-diff snapshot test `Tests/Search.Contract/OpenApiSnapshotTests.cs` ensuring `packages/shared_contracts/search/search.openapi.yaml` matches canonical
- [ ] T064 [P] Add contract DTO snapshot test `Tests/Search.Contract/DtoSnapshotTests.cs` detecting breaking changes to `SearchHit`, `SearchResponse`, etc.
- [ ] T065 [P] Operator runbook `docs/runbooks/search.md` covering reindex triggers, dead-letter resolution, health check interpretation, seed-file update flow
- [ ] T066 [P] Constitution re-check: re-run plan.md Constitution Check gates against implemented code; append PASS/FAIL note to `plan.md` post-implementation section
- [ ] T067 Verify Definition of Done checklist in `quickstart.md` §6; check every box
- [ ] T068 End-to-end smoke: run full quickstart §2 + §3 + §4 against a freshly bootstrapped environment

---

## Dependency Graph

```
Phase 1 (Setup) ──▶ Phase 2 (Foundational)
                         │
                         ├──▶ Phase 3 (US1 Keyword)   ──┐
                         ├──▶ Phase 4 (US2 Facets)    ──┤
                         ├──▶ Phase 5 (US3 Autocomp)  ──┤
                         ├──▶ Phase 6 (US5 Incremental)┤──▶ Phase 9 (Polish)
                         ├──▶ Phase 7 (US4 Admin)     ──┤
                         └──▶ Phase 8 (US6 Swap)      ──┘
```

Within Phase 2, `T008` (port) and `T014` (MeilisearchProvider) block Phases 3–8. `T015` (StaticProvider) unblocks Phase 8 earliest.

Phases 3, 4, 5, 6, 7, 8 are independent once Phase 2 completes — can be built in parallel per user story.

---

## Parallel Execution Opportunities

**Phase 1** — T003, T004, T005, T006 run in parallel after T001+T002.
**Phase 2** — T009, T010, T011, T012, T013, T016, T017 run in parallel after T008.
**Phase 3** — T022, T025, T026, T028 in parallel after T024.
**Phase 4** — T032, T033, T034 in parallel after T031.
**Phase 5** — T038, T039 in parallel after T037.
**Phase 6** — T043, T045, T046, T047 in parallel after T044.
**Phase 7** — T052, T053, T054 in parallel after T051.
**Phase 8** — T057, T058 parallel after T056.
**Phase 9** — T059–T065 fully parallel.

---

## Suggested MVP Scope

Phases 1 + 2 + 3 + 4 + 5 + 6 = **T001 through T047** (47 tasks).
Delivers: customer keyword search + facets/sort + autocomplete + event-driven incremental reindex. Admin full-reindex (US4) and explicit provider-swap certification (US6) can ship in a follow-up iteration.

---

## Totals

- **Total tasks**: 68 (T001–T068)
- **Phase counts**: Setup 6 · Foundational 12 · US1 10 · US2 6 · US3 5 · US5 8 · US4 7 · US6 4 · Polish 10
- **Parallel markers**: 38 tasks flagged `[P]`
- **User-story–labeled tasks**: 40 (across US1–US6)

---

## Amendment A1 — Environments, Docker, Seeding

**Source**: [`docs/missing-env-docker-plan.md`](../../../docs/missing-env-docker-plan.md)

**Hard dependency**: PR A1 + PR 004 + PR 005 must merge before this PR.

### New tasks

- [ ] T069 Formalise `ISearchIndexer` contract in `services/backend_api/Features/Search/ISearchIndexer.cs` with `Task IndexVariantAsync(Guid variantId, CancellationToken ct)` and `Task ReindexAllAsync(CancellationToken ct)`. Already implied by the service-boundary ADR (ADR-005); A1 requires it as the stable surface seeders call.
- [ ] T070 [US1] Implement `services/backend_api/Features/Seeding/Seeders/_006_SearchSeeder.cs` (`Name="search-v1"`, `Version=1`, `DependsOn=["catalog-v1"]`). Calls `ISearchIndexer.ReindexAllAsync()` — handles the case where catalog was seeded before the search module was registered.
- [ ] T071 [US1] Integration test `Tests/Search.Integration/Seeding/SearchSeederTests.cs`: after seeder runs, `curl meilisearch:7700/indexes/variants/search -d '{"q":"forceps"}'` returns ≥ 1 hit; Arabic query `{"q":"ملقط"}` also returns ≥ 1 hit.
