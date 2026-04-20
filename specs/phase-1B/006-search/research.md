# Phase 0 Research: Search (006)

**Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

All entries resolve a "NEEDS CLARIFICATION" or technology-choice question raised during planning. Each records the **Decision**, **Rationale**, and **Alternatives Considered**.

---

## R1 — Provider port shape (`ISearchProvider`)

- **Decision**: A minimal async port with four operations: `QueryAsync(SearchQuery) → SearchResult`, `AutocompleteAsync(AutocompleteQuery) → AutocompleteResult`, `UpsertDocumentsAsync(Market, IReadOnlyList<ProductDoc>)`, `DeleteDocumentsAsync(Market, IReadOnlyList<string> productIds)`, plus `EnsureIndexAsync(IndexSchemaVersion)` for bootstrap. All calls scoped by `market_code`. No Meilisearch types leak across the boundary — DTOs live in `Features/Search/Shared/`.
- **Rationale**: FR-020 / Principle 26 demand swappability without caller changes. Four-verb surface is the minimum that covers query, autocomplete, indexing, and schema bootstrap. Keeping DTOs pure lets the `StaticSearchProvider` stub satisfy contract tests and lets a future OpenSearch/Elastic adapter slot in.
- **Alternatives Considered**: (a) Exposing provider-native query builders — rejected; leaks ADR-005 choice into handlers. (b) Splitting read/write into two ports — rejected as premature; both sides already share the same DTO vocabulary and the read side is dominant.

## R2 — Arabic normalization strategy

- **Decision**: Dual layer. At **index time**, configure Meilisearch with custom stopwords (`Seeds/stopwords.ar.yaml`) and synonym lists (`Seeds/synonyms.yaml`); rely on the engine's built-in Arabic tokenizer for base segmentation. At **query time**, run a pre-normalizer wrapper (`ArabicQueryNormalizer`) that folds alef variants (أ إ آ → ا), ya/alef-maqsura (ى → ي), ta-marbuta (ة → ه), strips tatweel (ـ), and removes combining diacritics before sending to the provider. The same wrapper runs inside the `StaticSearchProvider` stub so parity is observable in tests.
- **Rationale**: Meilisearch's native Arabic handling covers segmentation and basic folding, but the corpus test (SC-005 at 95% accuracy) shows it under-normalizes alef/ya/ta-marbuta variants common in dental terminology. A query-side wrapper makes the behavior engine-independent (Principle 26) and lets us validate it deterministically in unit tests without a live Meilisearch.
- **Alternatives Considered**: (a) Index-only normalization — rejected; the moment we swap providers, parity breaks. (b) ICU-based custom analyzer library — rejected as premature complexity; our folding rules are small and stable.

## R3 — Single multilingual index vs. per-language index

- **Decision**: Single logical index per market (`products_eg`, `products_ksa`) with dual fields: `name_ar`/`name_en`, `description_ar`/`description_en`, etc. Query handlers pick searchable-attribute weights based on `accept-language` / explicit `lang` parameter. A shared `text_all` field concatenates both languages for cross-language fallback ranking.
- **Rationale**: Matches Clarification Q1 auto-answer. Collapses operational surface (two indexes instead of four), keeps facets consistent across languages, and supports mixed-language queries (common in KSA professional search). Avoids re-ingesting a product into two indexes on every update.
- **Alternatives Considered**: (a) Per-language index — rejected; doubles reindex load and complicates facet count reconciliation. (b) Single field auto-detecting language — rejected; Arabic and English share Latin-digit SKUs and would cross-pollute relevance.

## R4 — Index document schema version

- **Decision**: Index documents carry a `schema_version` integer (starts at `1`). `EnsureIndexAsync` compares configured vs. live schema; mismatch triggers admin-gated full reindex. Version bumps are code-owned (no runtime migration).
- **Rationale**: Meilisearch does not support in-place schema migration for searchable-attribute / filterable-attribute changes; full reindex is the only safe path. Explicit version prevents silent drift when developers add fields.
- **Alternatives Considered**: Blue/green alias swap — deferred to spec 1.5-d; single-region Phase 1B does not justify the operational complexity yet.

## R5 — Reindex queue implementation

- **Decision**: In-process bounded channel (`System.Threading.Channels.Channel<ReindexCommand>`) backed by an `IHostedService` worker. Incremental events drain continuously; admin full-reindex submits a single job token that streams pages of products from the catalog read store. Failures retry with Polly exponential backoff (100ms → 1.6s, max 5 attempts), then land in a `reindex_dead_letter` table with the original command payload for admin replay.
- **Rationale**: Phase 1B scale (peak ~5 events/s, 10k products) fits comfortably in-process. A distributed queue (Hangfire / Azure Service Bus) is deferred per plan's explicit non-goal. The dead-letter table preserves at-least-once semantics without adding infra.
- **Alternatives Considered**: (a) Direct synchronous upsert in the notification handler — rejected; couples catalog write latency to Meilisearch availability and breaks FR-016. (b) Outbox pattern against the catalog DB — deferred; spec 005 already publishes MediatR notifications and Phase 1B does not require cross-process durability.

## R6 — Ranking defaults

- **Decision**: Meilisearch ranking rules, in order: `words → typo → proximity → attribute → sort → exactness → custom:popularity_desc`. `popularity_desc` is a numeric field sourced from catalog (fallback `0` until spec 028 click tracking lands). Searchable attributes ordered: `sku`, `barcode`, `name_ar`, `name_en`, `brand_name`, `category_names`, `description_ar`, `description_en`. SKU/barcode ranked first ensures exact-match short-circuit (FR-008).
- **Rationale**: Attribute-order weighting plus the default `exactness` rule naturally satisfies FR-008 without a custom short-circuit handler. `popularity_desc` slot is pre-wired so spec 028 can populate it without a reindex.
- **Alternatives Considered**: Custom scoring with boost expressions — rejected; adds complexity with no SC-002 latency headroom left to spend.

## R7 — Typo tolerance tiers

- **Decision**: Use Meilisearch's `typoTolerance` config: min word size for 1-typo = 4, for 2-typos = 8. SKU and barcode added to `disableOnAttributes` (FR-008 requires exact match). Numeric-only queries ≥ 3 chars trigger a query rewrite that routes to exact-match filter, bypassing typo rules entirely.
- **Rationale**: Dental SKU patterns (e.g., `3M-68543`) must not be "fixed" to nearby product codes. Numeric short-circuit prevents catalog-wide fuzzy hits on partial barcode prefixes.
- **Alternatives Considered**: Disabling typo globally for AR — rejected; Arabic has legitimate typo cases (diacritic-less typing) that the corpus test validates.

## R8 — Facet computation

- **Decision**: Facets (`brand`, `category`, `priceBucket`, `availability`, `attributes.*`) computed by Meilisearch on the filtered result set. Counts reflect **intersection** semantics: selecting `brand=3M` narrows `category` counts to 3M products only (FR-009). `priceBucket` is a pre-computed field written at index time (buckets: `0-50`, `50-200`, `200-500`, `500+` in market currency) — actual price stays gated behind `priceToken`.
- **Rationale**: Intersection semantics match user expectation in commerce search. Pre-bucketing price avoids exposing raw amounts while still supporting coarse filtering; the `priceToken` contract (spec 007-a) handles display-time resolution.
- **Alternatives Considered**: Disjunctive facets (AND within a facet, OR across) — deferred to spec 1.5-d; Phase 1B keeps semantics simple and consistent.

## R9 — Autocomplete payload

- **Decision**: Autocomplete returns a composite DTO: up to 8 products (id + name + thumbnail + priceToken), 3 brands (id + name), 3 categories (id + name + localized path). Single provider call uses Meilisearch's multi-search (`/multi-search`) with three parallel queries against the same index, different `limit` and `attributesToRetrieve`.
- **Rationale**: Matches FR-011 shape and SC-003 (p95 ≤ 150ms). Multi-search amortizes network/TLS cost of the three logical queries.
- **Alternatives Considered**: Separate `/brands` and `/categories` indexes — rejected; adds reindex load for data that rarely changes and is small enough to filter from the product index.

## R10 — Synonym & stopword seed format

- **Decision**: Two YAML files shipped in `Features/Search/Seeds/`:
  - `synonyms.yaml` — list of `{ canonical: string, variants: string[] }` entries. Applied bidirectionally at index config time.
  - `stopwords.ar.yaml`, `stopwords.en.yaml` — flat list of lowercase tokens.
  - Loader validates at startup; schema errors fail-fast (refuse to boot).
- **Rationale**: YAML is reviewable in PRs, diff-friendly, and human-editable by domain experts pending the admin UI (spec 1.5-d). Fail-fast prevents silent fallback to empty synonym lists.
- **Alternatives Considered**: JSON — rejected for PR review ergonomics. Database-backed table — deferred to spec 1.5-d.

## R11 — Observability schema

- **Decision**: Query log line fields (Serilog structured + OpenTelemetry span attributes): `search.query_hash` (SHA-256 of normalized query), `search.query_len`, `search.lang`, `search.market`, `search.hit_count`, `search.latency_ms`, `search.clamp_flags` (bitmask), `search.caller_hash` (HMAC-SHA256 of user-id or IP, keyed by a rotating secret), `search.provider` (`meilisearch|static`). No raw query text is logged. Empty-state queries additionally log `search.empty_state_reason`.
- **Rationale**: FR-021 requires observability without PII exposure. HMAC with rotating key prevents long-term caller correlation. Hit-count + latency give SRE enough signal to honor SC-002/SC-003.
- **Alternatives Considered**: Raw query logging — rejected; Arabic queries often include clinic names and caller context. Separate click-tracking table — deferred to spec 028.

## R12 — Pagination and depth clamp

- **Decision**: `page` defaults to 1, max 834 (so `pageSize × page ≤ 20 000`). `pageSize` defaults to 24, max 96. Requests exceeding the depth cap return HTTP 200 with `clampFlags.pagination=true` and the clamped window — never a 4xx. Deep pagination beyond the cap is blocked with structured error envelope (FR-019) suggesting filters.
- **Rationale**: Meilisearch `offset+limit` degrades past ~20k; matches SC-002 latency target. Soft clamp preserves UX continuity.

## R13 — Error envelope

- **Decision**: All errors return `{ code, message_en, message_ar, details?, correlationId }`. Error codes namespaced `search.*`: `search.query_too_long`, `search.pagination_too_deep`, `search.provider_unavailable`, `search.reindex_in_progress`, `search.rate_limited`. HTTP status mapping: 400 for caller issues, 429 for rate limit, 503 for provider unavailable (with `Retry-After`).
- **Rationale**: FR-019 parity with catalog (spec 005) and identity (spec 004) error envelopes. Bilingual messages per Principle 4.

## R14 — Admin reindex RBAC

- **Decision**: Single policy key `search.reindex` enforced via spec 004's ASP.NET Core authorization policy plumbing. Granted to roles `Admin` and `CatalogOps`. Admin endpoints: `POST /admin/search/reindex` (starts job), `GET /admin/search/reindex/{jobId}` (status), `GET /admin/search/health` (provider + queue depth).
- **Rationale**: Principle 25 audit + Principle 20 admin dashboard requirements; reuses spec 004 RBAC instead of introducing a parallel gate.

## R15 — Restricted product visibility in results

- **Decision**: Restricted products are **always indexed** with a `restricted: true` flag and `restriction_reason_code`. Customer search returns them with `priceToken` populated (spec 007-a resolves whether to show price) and an `eligibility` object surfaced by calling spec 005's `/products/{id}/eligibility` lazily only on detail view (not per hit). Autocomplete excludes `restricted: true` items for unauthenticated sessions but includes them for authenticated sessions — the caller role is passed in the query context.
- **Rationale**: Principle 8 requires visibility + price; the eligibility check is deferred to detail view to meet SC-002 p95. Autocomplete exclusion for anon is a usability call — anon users cannot act on restricted hints.
- **Alternatives Considered**: Hide restricted entirely in anon search — rejected; violates Principle 8 visibility intent.

---

## Outstanding Items

None. All Technical Context unknowns resolved. Plan Phase 1 design artifacts may proceed.
