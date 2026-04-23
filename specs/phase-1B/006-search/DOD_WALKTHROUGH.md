# DoD Walkthrough — Search v1

**Spec**: `006-search` · **Phase**: 1B · **Milestone**: 2 · **Lane**: A

This walkthrough maps the delivered artifacts to the Definition of Done and spec guardrails.

## 1. Constitution + ADR Alignment

| Gate | Evidence |
|---|---|
| P4 (AR/RTL editorial) | `services/backend_api/Modules/Search/Messages/search.{ar,en}.icu` parity complete; pending human sign-off tracked in `AR_EDITORIAL_REVIEW.md`. |
| P12 / P26 (search seam) | `ISearchEngine` boundary in `Modules/Search/Primitives/ISearchEngine.cs`; handlers depend on interface, not SDK types. |
| ADR-005 (Meilisearch) | `MeilisearchSearchEngine` + bootstrap settings/synonyms wired in `SearchBootstrapHostedService`/`SynonymsSeeder`. |
| P25 (audit/ops readiness) | Reindex jobs tracked in `search.reindex_jobs`; indexer cursor durability in `search.search_indexer_cursor`; observability metrics/logging added in Phase 7. |
| P27 (UX quality) | Localized reason codes + stable error responses through customer/admin response factories. |

## 2. Phase 7 Evidence

- **T042 Structured query log emitter**: `Modules/Search/Primitives/QueryLogger.cs` emits `query_hash`, `marketCode`, `locale`, `resultCount`, `latencyMs`, `filters`.
- **T043 Metrics**:
  - `search_indexer_lag_seconds` (Observable Gauge) in `Modules/Observability/SearchMetrics.cs`.
  - `search_query_latency_ms` (Histogram) in `Modules/Observability/SearchMetrics.cs`.
  - Registered via `Modules/Shared/ModuleRegistrationExtensions.cs`.
- **T044 AR gold dataset**:
  - `Tests/Search.Tests/Resources/ar-gold.jsonl` with **520** rows.
  - `Tests/Search.Tests/Unit/ArabicCoverageTests.cs` enforces dataset presence, size `>= 500`, and normalization coverage `>= 99%`.
- **T045 AR editorial pass**:
  - Updated `search.ar.icu` copy for clarity and parity.
  - Editorial follow-up tracked in `AR_EDITORIAL_REVIEW.md` with `needs-ar-editorial-review: true`.
- **T046 OpenAPI + contract diff guardrail**:
  - Added `services/backend_api/openapi.search.json` (spec 006 surface).
  - JSON validated locally (`python3 -m json.tool`).
  - Local `contract-diff` CLI path is unavailable in this environment (`docker`/`oasdiff` not installed); CI remains the enforcing gate.
- **T047 Fingerprint + walkthrough**:
  - Fingerprint computed and recorded below.
  - Query-log spot check captured below.

## 3. Query Log Spot Check (FR-020 / SC-008)

From `SearchProducts_ArabicQuery_FoldsDiacritics` run:

```text
search.query query_hash="e599c1efa55b83640e7aeda3e9ab89ff4d7616d58b8d3a6b9e62f7d652328ae4" marketCode="ksa" locale="ar" resultCount=1 latencyMs=19 filters=False
```

All six required fields are present (`query_hash`, `marketCode`, `locale`, `resultCount`, `latencyMs`, `filters`).

## 4. Validation Runs

- `dotnet build services/backend_api/` → **0 errors** (known NU1902 warning on `SixLabors.ImageSharp`).
- `dotnet test services/backend_api/Tests/Search.Tests/Search.Tests.csproj` → **60/60 pass**.
- `dotnet test services/backend_api/Tests/Identity.Tests/Identity.Tests.csproj` → **127/127 pass**.
- `dotnet test services/backend_api/Tests/Catalog.Tests/Catalog.Tests.csproj` → **42/42 pass**.

## 5. Fingerprint

`789f39325c0f0e8d7d646fc493718867540f9da41f1eed71c31bf15b53e8fb62`

Computed via:

```bash
bash scripts/compute-fingerprint.sh
```
