# Quickstart: Search (006)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Bring-up, smoke, and Definition of Done checklist for the Search module. Assumes spec 004 (identity) and spec 005 (catalog) are running locally.

---

## 1. Local Bring-Up

```bash
# from repo root
docker compose -f infra/dev/docker-compose.yml up -d meilisearch
# env
export SEARCH__Provider=meilisearch
export SEARCH__Meilisearch__Url=http://localhost:7700
export SEARCH__Meilisearch__MasterKey=dev-master-key
# run backend with Search feature enabled
dotnet run --project services/backend_api
```

Boot sequence does:
1. `EnsureIndexAsync` for each market → creates `products_eg`, `products_ksa` if missing, applies settings from `Features/Search/Provider/Meilisearch/IndexSettings.cs`.
2. Loads `Seeds/synonyms.yaml`, `Seeds/stopwords.ar.yaml`, `Seeds/stopwords.en.yaml` → pushes to Meilisearch.
3. Subscribes to catalog domain events via MediatR.

---

## 2. Smoke Walk — Customer Flows

### 2.1 Keyword search (US1)
```bash
curl "http://localhost:5080/search?market=ksa&q=braces&lang=en"
# expect: 200, hits[] populated, facets{brand, category, priceBucket, availability}, priceToken on every hit
```

### 2.2 Arabic keyword with normalization (US1)
```bash
curl "http://localhost:5080/search?market=ksa&q=تقويم&lang=ar"
# expect: matches products tagged 'braces' via synonym + alef/ya folding
```

### 2.3 SKU short-circuit (FR-008)
```bash
curl "http://localhost:5080/search?market=ksa&q=3M-68543"
# expect: single exact hit, typo disabled
```

### 2.4 Facet intersection (US2)
```bash
curl "http://localhost:5080/search?market=ksa&q=&brand=3M&category=orthodontics"
# expect: facets.brand counts narrowed to orthodontics subset
```

### 2.5 Autocomplete (US3)
```bash
curl "http://localhost:5080/search/autocomplete?market=ksa&q=brac"
# expect: <8 products, <3 brands, <3 categories; p95 ≤ 150ms
```

### 2.6 Empty state (FR-017)
```bash
curl "http://localhost:5080/search?market=ksa&q=xyzneverexists"
# expect: hits=[], emptyState{didYouMean?, suggestedFilterRelaxations[], suggestions[]}
```

### 2.7 Clamp flags (FR-018)
```bash
curl "http://localhost:5080/search?market=ksa&q=$(printf 'a%.0s' {1..250})"
# expect: 200, clampFlags.query=true, query truncated to 200 chars
```

---

## 3. Smoke Walk — Admin Flows

### 3.1 Trigger full reindex (US4)
```bash
curl -X POST -H "Authorization: Bearer $ADMIN_JWT" \
  "http://localhost:5080/admin/search/reindex?market=ksa"
# expect: 202, ReindexJob with state=queued
```

### 3.2 Poll job status
```bash
curl -H "Authorization: Bearer $ADMIN_JWT" \
  "http://localhost:5080/admin/search/reindex/<jobId>"
# expect: state transitions queued → running → succeeded within 5 min for 10k products (SC-007)
```

### 3.3 Health
```bash
curl -H "Authorization: Bearer $ADMIN_JWT" \
  "http://localhost:5080/admin/search/health"
# expect: markets[].status=up, indexDocCount matches catalog count
```

---

## 4. Smoke Walk — Event Propagation (US5)

1. Create/publish a product via spec 005 admin API.
2. Within 2s, search for the product by name → expect hit (SC-001).
3. Archive the product → search for it → expect zero hits within 2s.
4. Force a Meilisearch outage (`docker compose stop meilisearch`), publish another product, restart → dead-letter entry visible via `/admin/search/dead-letter`; replay succeeds.

---

## 5. Provider Swap (US6)

```bash
export SEARCH__Provider=static
dotnet run --project services/backend_api
# Run contract tests → identical DTO shapes, expected-result parity for the curated AR corpus.
```

---

## 6. Definition of Done Checklist

- [ ] All 24 FRs implemented and covered by at least one automated test
- [ ] All 8 SCs verifiable via the test suite:
  - [ ] SC-001 propagation ≤ 2s (integration test with wall clock)
  - [ ] SC-002 search p95 ≤ 500ms at 24 hits (k6 script)
  - [ ] SC-003 autocomplete p95 ≤ 150ms (k6 script)
  - [ ] SC-004 100% SKU/barcode exact-match coverage (corpus test)
  - [ ] SC-005 ≥ 95% Arabic corpus pair accuracy (FsCheck + fixture)
  - [ ] SC-006 100% restricted products remain searchable with priceToken
  - [ ] SC-007 full reindex 10k products ≤ 5 min (integration, measured)
  - [ ] SC-008 provider swap via `StaticSearchProvider` with zero caller changes
- [ ] Constitution gates re-checked post-implementation
- [ ] OpenAPI contract published to `packages/shared_contracts/search/` and consumed by Flutter client generator
- [ ] Audit events for admin reindex visible in `audit_events` table (spec 003 alignment)
- [ ] Observability: Serilog structured logs + OTel spans emitting all fields from R11
- [ ] No raw query text in any log sink
- [ ] RBAC policy `search.reindex` wired through spec 004
- [ ] Dead-letter replay verified end-to-end
- [ ] Seed YAMLs reviewed by bilingual domain editor
- [ ] Docs: operator runbook (reindex, dead-letter, health) in `docs/runbooks/search.md`
