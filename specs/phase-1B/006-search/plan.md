# Implementation Plan — Search v1 (Spec 006)

**Branch**: `phase-1B-specs` · **Date**: 2026-04-22 · **Input**: `spec.md`

## Technical Context
- **Language/Runtime**: C# 12 / .NET 9 (LTS), matches rest of monorepo (ADR per spec 003/004/005).
- **Engine**: Meilisearch v1.9+ single-node (ADR-005); container already in `infra/local/docker-compose.yml` (A1).
- **Persistence**: PostgreSQL 16 for `search_indexer_cursor` + dispatch-state tables; event source is the existing `catalog.catalog_outbox` (spec 005).
- **Module**: `services/backend_api/Modules/Search/` following ADR-003 vertical-slice pattern.
- **Dependencies (NuGet)**: `Meilisearch` (official SDK) ≥ 0.15.x, `YamlDotNet` 16.* (shared with spec 005), `System.Text.Json` built-in.

## Constitution Check
| Principle | Gate | Note |
|---|---|---|
| 4 — AR/EN editorial | PASS | AR tokenizer handles alef/ya/ta-marbuta + diacritics; both index & query paths apply same normalization (FR-005). |
| 5 — Market-configurable | PASS | One index per (market_code, locale) — cross-market leakage physically impossible at query layer (SC-007). |
| 6 — Multi-vendor-ready | PASS | ProductSearchProjection carries `vendor_id` (nullable); filter reserved but no-op at launch. |
| 8 — Restricted visibility | PASS | Restricted docs indexed with `restricted=true`; FR-014 requires visibility, never hidden. |
| 12 — Search is core | PASS | This spec *is* Principle 12. |
| 22 — Fixed tech | PASS | .NET / Postgres / Meilisearch all per ADRs. |
| 23 — Modular monolith | PASS | New `Modules/Search/` slice; no new deployable. |
| 26 — Search-behind-interface | PASS | `ISearchEngine` boundary enforced (FR-016). |
| 27 — UX quality | PASS | p95 targets (300/50/100 ms) codified in SCs. |
| 28 — AI-build standard | PASS | Each endpoint is a Request/Validator/Handler/Endpoint slice. |

**Constitution gate status**: PASS for all applicable principles.

## Phase A — Primitives
- `Modules/Search/Primitives/ISearchEngine.cs` — interface: `UpsertAsync`, `DeleteAsync`, `SearchAsync`, `AutocompleteAsync`, `EnsureIndexAsync`, `HealthAsync`.
- `Modules/Search/Primitives/MeilisearchSearchEngine.cs` — the only impl; no callers outside this module.
- `Modules/Search/Primitives/Normalization/ArabicNormalizer.cs` — alef/ya/ta-marbuta folding + NFKC + diacritics strip + stopwords.
- `Modules/Search/Primitives/ProductSearchProjection.cs` — the Meilisearch document shape; derived from `ProductPublishedEvent` payload.
- `Modules/Search/Primitives/IndexNames.cs` — canonical names for `products-{market}-{locale}`.

## Phase B — Persistence
- Entity `SearchIndexerCursor` + EF config + migration `Search_Initial`.
- No other tables; Meilisearch owns the index, Postgres owns the cursor only.

## Phase C — Outbox subscriber
- `Workers/SearchIndexerWorker.cs` — `BackgroundService`, polls `catalog.catalog_outbox` where `dispatched_at IS NULL` at ≤ 2 s cadence, dispatches one batch per index, marks `dispatched_at = now()` only after Meilisearch ack.
- Idempotency: re-delivery is safe (upserts by `id`; deletes tolerate 404).

## Phase D — Synonyms bootstrap
- `Modules/Search/Synonyms/synonyms.ar.yaml` + `synonyms.en.yaml` skeleton (domain curators extend later).
- `SynonymsSeeder` runs at app startup; writes synonym list into each index via SDK.

## Phase E — Customer slices (P1 user stories 1–3)
- `Customer/SearchProducts/{Request,Handler,Endpoint}.cs`
- `Customer/Autocomplete/{Request,Handler,Endpoint}.cs`
- `Customer/LookupBySkuOrBarcode/` exact-match shortcut (short-circuits engine when regex matches SKU/barcode pattern).

## Phase F — Admin slices (P2 user stories 5–6)
- `Admin/Reindex/{Request,Handler,Endpoint}.cs` with SSE progress channel and single-job guard.
- `Admin/Health/{Request,Handler,Endpoint}.cs` for lag + doc counts.

## Phase G — Empty-query featured fallback
- Customer search with empty `query`: short-circuits to the spec 005 featured-product projection (no engine call) — SC-driven perf optimization.

## Phase H — Testing
- Unit: normalizer fold cases (40+ AR input pairs), cursor advance semantics, YAML synonym loader.
- Integration: Testcontainers Meilisearch + Postgres; exercises indexer lag SC-004, cross-market SC-007, reindex idempotency.
- Contract: every FR + every acceptance scenario → ≥ 1 contract test.
- Gold-standard: 500 AR query/result pairs dataset (SC-006), curated by product leadership, maintained in `tests/Search.Tests/Resources/ar-gold.jsonl`.

## Phase I — Observability
- Structured log per query: `query_hash` (sha256 of normalized query), `marketCode`, `locale`, `resultCount`, `latencyMs`, `filters` (FR-020, SC-008).
- Metric: `search_indexer_lag_seconds` gauge per index.
- Metric: `search_query_latency_ms` histogram tagged by `(locale, hasFilters)`.

## Phase J — Polish / AR editorial
- AR-editorial pass on `search.ar.icu` (reason codes: `search.engine_unavailable`, `search.reindex.in_progress`).
- OpenAPI regen (Guardrail #2).
- Fingerprint + DoD walk-through.

## Complexity Tracking
| Item | Why it stays | Mitigation |
|---|---|---|
| 4 launch indexes (not 1 + filter) | Simpler re-indexing per market rollout; physical isolation = Principle 5 guarantee. | Shared projection code; one config table lists indexes. |
| Per-index synonyms | AR and EN synonym sets differ linguistically. | YAML per locale + per-index loader. |
| Outbox polling rather than push | Consistent with spec 005; no new infra. | ≤ 2 s poll keeps lag budget inside 5 s. |
| Single-node Meilisearch | HA deferred to Phase 1F spec 029. | Document in Assumptions; 503 behavior spec'd. |

## Critical files created / edited
- Create: everything under `services/backend_api/Modules/Search/**`, `tests/Search.Tests/**`, `services/backend_api/Modules/Search/Synonyms/synonyms.{ar,en}.yaml`.
- Edit: `Program.cs` to register `AddSearchModule()`.
- Reuse: `catalog.catalog_outbox` (spec 005), admin JWT + RBAC (spec 004), MessageFormat.NET (spec 003), `infra/local/docker-compose.yml` Meilisearch service (A1).

## Post-design constitution re-check: PASS.
