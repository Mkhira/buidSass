# Feature Specification: Search

**Feature Branch**: `006-search`
**Created**: 2026-04-20
**Status**: Draft
**Input**: User description: "Phase 1B spec 006 · search — keyword + facet + autocomplete + Arabic-normalized product search behind a service boundary (per docs/implementation-plan.md §Phase 1B)"

**Phase**: 1B — Core Commerce
**Depends on**: 005 (catalog — source of truth for products, variants, attributes, brands, categories, restriction flags; emits reindex events). Also Phase 1A (003 shared-foundations for error envelope, correlation-id, shared-contracts pipeline).
**Enables**: 009 (cart autocomplete-from-search context), 010 (checkout last-used-search context), 014 (customer app storefront search UI), 016 (admin catalog search filter), 022 (reviews surfaced by product id, not searched directly).
**Constitution anchors**: Principles 2 (real operational depth), 4 (AR + EN search parity, editorial-grade Arabic normalization), 8 (restricted products appear in results with price visible), 12 (search is a core capability from day one, behind a service boundary), 26 (search architecture decoupled so engine can evolve), 27 (every UX state — loading, empty, error, no-results-with-suggestion), 28 (AI-build standard), 29 (required spec output standard).

---

## Clarifications

### Session 2026-04-20

- Q: Should Arabic and English queries share a single search index or be routed to two per-locale indexes? → A: Single multilingual index with dual language-analyzer fields (`name_ar`, `name_en`, etc.). A query tokenized against both and the locale-specific analyzer boosts the caller's locale. Rationale: matches Meilisearch (ADR-005) best practice, keeps one reindex pipeline, enables fallback when Arabic content is missing.
- Q: What does the search service return as price in the hit payload? → A: The `priceToken` from spec 005, never a resolved price. Listing UI calls spec 007-a to resolve; hit payload carries only the token + currency code for the caller's market. Keeps 007-a authoritative and avoids stale cached prices.
- Q: What is the reindex model when catalog emits an update event? → A: Incremental upsert keyed by product id, fan-out within 2 s (SC-001 propagation budget). No bulk periodic reindex at launch; a manual full-reindex command is available for admins as an operational safety valve. Rationale: aligns with catalog FR-019 event contract, keeps infra costs proportional to change rate.
- Q: When a customer query returns zero results, what does the service return beyond `items=[]`? → A: `{ items: [], suggestions: [...], didYouMean: "string|null", filtersApplied: [...], suggestedFilterRelaxations: [...] }`. Rationale: Principle 27 requires every UX state be captured; empty-state UX needs guidance (spelling correction, filter relaxation, top categories to pivot to).
- Q: Are search events (queries, clicks, zero-result queries) persisted for analytics and relevance tuning? → A: Log queries + result counts + zero-result flag + market + locale to the shared observability pipeline (Serilog structured logs + OpenTelemetry). No click tracking in Phase 1B — that lands in spec 028 Phase 1.5. PII (`customer_id`) is hashed; raw query string is stored because it has no direct PII risk in a dental product catalog.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Customer types a keyword and finds matching products (Priority: P1)

An unauthenticated or authenticated visitor types a query like `قفاز` (Arabic: glove) or `dental scaler` into the search box. The service tokenizes, normalizes Arabic (alef variants, ya variants, ta-marbuta, diacritics), applies typo tolerance, searches across product name, description, brand, category, and attributes, and returns a paged result set scored by relevance. Each hit includes all fields needed to render a product card (id, localized name, primary image, availability, restriction flag, `priceToken`). Restricted products remain in results with prices visible per Principle 8.

**Why this priority**: Search is the primary product-discovery path. Without it, the storefront MVP is unusable at scale. This is the only P1 story that can be cut and still leave a working catalog listing — but launch readiness requires search.

**Independent Test**: Seed the search index from a seeded catalog of 20 products (mix AR + EN, 1 restricted). Call the search endpoint with three queries — an AR keyword that requires alef normalization (`الف` ↔ `أ` ↔ `إ`), an EN keyword with a single-character typo, and an SKU — and verify each returns the expected product(s) in sensible order with restricted products visible and priced. Fully testable without any authenticated action.

**Acceptance Scenarios**:

1. **Given** an active indexed product with AR name `قفازات لاتكس`, **When** a visitor queries `قفاز` with `Accept-Language: ar`, **Then** the product appears in the top 5 results with all card fields present and the AR name rendered.
2. **Given** an active indexed product with EN name `Latex Gloves`, **When** a visitor queries `latx gloves` (1-edit typo), **Then** the product appears in the top 5 results (typo tolerance FR-007).
3. **Given** an active indexed product with SKU `GLOVE-L-100`, **When** a visitor queries that exact SKU, **Then** the product is the first result (FR-008 — exact SKU/barcode match takes precedence).
4. **Given** a restricted product matches a query, **When** a visitor (authenticated or not, verified or not) queries, **Then** the product appears with price visible and `restricted: true` in the hit; the result is NOT filtered out.
5. **Given** an archived or unpublished product, **When** a visitor queries, **Then** the product is absent from all customer search responses.
6. **Given** a query with zero matches, **When** the visitor submits it, **Then** the response returns `items: []`, a `didYouMean` suggestion where available, `suggestedFilterRelaxations` listing which applied filter is most likely to relax the result set, and the top 5 categories as `suggestions` pivot targets.

---

### User Story 2 — Customer refines results with facets and sort (Priority: P1)

A customer applies facet filters (category, brand, price range bucket, availability, restricted-only toggle) and chooses a sort (relevance, price ascending, price descending, newest). Results update with the correct hit count and the facet counts reflect the intersection of other applied facets (so e.g., choosing category X shows the brand counts that exist within X).

**Why this priority**: Faceted navigation is table stakes for a product catalog of the target size (~10 k products). Constitution Principle 12 mandates facets from day one.

**Independent Test**: Seed 20 products across 3 brands and 3 categories with mixed restriction and availability. Apply category=X + brand=Y + availability=in-stock and confirm (a) hit count matches direct DB query, (b) facet counts for the un-filtered facet axes intersect correctly, (c) each sort mode changes order deterministically.

**Acceptance Scenarios**:

1. **Given** an indexed catalog, **When** a visitor requests `GET /search?q=&category=X`, **Then** the response includes `facets: { category: [...], brand: [...], price_range: [...], availability: [...], restricted: [...] }` with counts that exclude the `category` facet self-intersection but include intersections on the other axes.
2. **Given** a result set, **When** the visitor switches `sort=price_asc`, **Then** the hits are reordered by the resolved price token ascending — price resolution is deferred to spec 007-a at presentation time, and sort ordering is stable across identical prices via secondary sort on product id.
3. **Given** a `restricted=true` filter, **When** applied, **Then** only products marked restricted appear in results (used by admins and power users; default facet shows both).
4. **Given** `sort=newest`, **When** applied, **Then** results sort by `published_at` descending.

---

### User Story 3 — Customer sees fast autocomplete suggestions as they type (Priority: P1)

As a visitor types into the search box, the service returns a light-weight suggestion payload: top matching product names (up to 8), matching brands (up to 3), matching categories (up to 3). The response is optimized for latency over depth. Queries as short as 2 characters return suggestions.

**Why this priority**: Autocomplete materially lifts search conversion and is mandated by Principle 12. Without it, the storefront feels 2010-era.

**Independent Test**: Seed the index; call `GET /search/autocomplete?q=قف&locale=ar` and `?q=la&locale=en`; confirm responses arrive within the p95 latency budget and the payload is limited to the documented top-N per group.

**Acceptance Scenarios**:

1. **Given** an indexed catalog, **When** a visitor types `قف`, **Then** the service returns within the autocomplete latency budget (SC-002) with up to 8 product matches, 3 brand matches, 3 category matches — each including `id`, localized label, and slug.
2. **Given** a prefix that matches nothing, **When** queried, **Then** the response is `{ products: [], brands: [], categories: [] }` (never an error).
3. **Given** an authenticated or unauthenticated caller, **When** the same query is made, **Then** the result set is identical (autocomplete does not leak per-user signal at launch; personalization lands in spec 1.5-b).

---

### User Story 4 — Admin triggers full reindex (Priority: P2)

An admin holding `search.reindex` permission runs a manual full reindex — resetting the index from the authoritative catalog store. This is the operational safety valve when event-driven incremental updates drift (e.g., after a failed deploy or a catalog backfill). The reindex runs in the background, reports progress, and does not interrupt live queries (old index stays live until swap).

**Why this priority**: Ops need a reset button. Incremental reindex via catalog events is the normal path, but without a manual full-reindex the only recovery is developer intervention — unacceptable for a launch-ready system.

**Independent Test**: Corrupt the index (delete N documents directly); run the full-reindex command; observe the job completes, the index document count matches the catalog published+active product count, and search queries continue serving throughout.

**Acceptance Scenarios**:

1. **Given** an admin with `search.reindex`, **When** they trigger a full reindex, **Then** a background job is enqueued and a job-id is returned.
2. **Given** a running full reindex, **When** a visitor issues queries, **Then** queries continue succeeding against the previously active index with unchanged latency.
3. **Given** a completed full reindex, **When** the swap completes, **Then** the new index is authoritative and an audit event `search.reindex.completed` is emitted with actor, job id, document count, duration.
4. **Given** an admin without `search.reindex`, **When** they attempt the command, **Then** the request is rejected with 403 and the attempt is audited.

---

### User Story 5 — Catalog change reflects in search within seconds (Priority: P1)

When the catalog module emits a `ProductPublished`, `ProductUpdated`, `ProductArchived`, or `ProductVariantChanged` event (per spec 005 §contracts/events.md), the search service applies the change to its index within 2 seconds at p95. Removal events (archive, delete) MUST actually remove the document from the index.

**Why this priority**: Without propagation, search becomes stale and untrustworthy. Principle 12's "behind a service boundary" requires that the service boundary respect the event contract already established in spec 005.

**Independent Test**: Publish a new product via the catalog admin surface; poll the search index/endpoint; confirm the product appears within 2 s p95. Archive a product; confirm it disappears within 2 s.

**Acceptance Scenarios**:

1. **Given** a newly-published product event arrives, **When** 2 seconds elapse (p95), **Then** the product is searchable.
2. **Given** an archive event arrives, **When** 2 seconds elapse, **Then** the product no longer appears in customer results.
3. **Given** a variant SKU change event arrives, **When** 2 seconds elapse, **Then** a query for the new SKU returns the product and a query for the old (archived) SKU does not.
4. **Given** a reindex event dispatcher is temporarily offline, **When** the catalog emits events, **Then** events are retried with bounded backoff; if the backlog exceeds a documented threshold, an alert fires and the operational runbook flags a manual full-reindex as the recovery path.

---

### User Story 6 — Provider is swappable without rewriting callers (Priority: P2)

The search surface is defined by a stable contract (endpoints, request/response shape). The provider (Meilisearch at launch per ADR-005) sits behind an internal port. A future swap to a different engine (OpenSearch, Typesense, vendor-hosted) MUST not require callers to change their integration.

**Why this priority**: Constitution Principle 26 mandates evolvability. Without a clean port, the launch choice locks the platform into a specific vendor.

**Independent Test**: A stub `StaticSearchProvider` returns deterministic results; swap it in via DI, run the search test suite, and confirm behavior delegates through the port without contract-level changes. This proves the boundary is honest.

**Acceptance Scenarios**:

1. **Given** the search service, **When** the configured provider is swapped via configuration, **Then** the public endpoints behave identically for the same inputs (modulo provider-specific ranking differences).
2. **Given** the port interface, **When** a new provider adapter is authored, **Then** no caller code outside the search module changes.

---

### Edge Cases

- Query contains only stopwords (e.g., `في`, `and`, `the`): the service returns a zero-result response with the `suggestions` pivot to top categories rather than matching every product.
- Query exceeds 200 characters: the service clamps to 200 and returns a `clamped_query` flag; no error.
- Arabic query mixes English tokens (e.g., `قفاز medium`): both analyzers contribute; hits that match either locale rank proportionally.
- A product's AR content is missing but EN is present: the product is indexed and searchable by EN tokens only; AR queries never surface it (no fallback, because returning a product whose AR label is empty would break rendering).
- Two products share identical normalized names: stable tie-break by product id ascending (deterministic ordering for pagination).
- The price range facet bucket boundaries must be computed per market (EG prices in EGP, KSA in SAR are on different scales): buckets are declared per market in configuration, not hard-coded.
- A search query arrives during a reindex swap: service serves from the previously active index; never returns a half-swapped result.
- Provider is unreachable (network/infra incident): the endpoint returns 503 with a localized error envelope and a correlation id; callers render an error state; a health-check ticks against the provider separately.
- A query matches >1000 hits: pagination clamps to a maximum depth of 200 pages (pageSize × page ≤ 20 000) — beyond that, the response includes a `max_depth_reached` flag prompting the UI to encourage refinement.
- An admin reindex is triggered while a previous reindex is already running: the second request is rejected with 409 and a message indicating the running job id.
- An SKU query containing hyphens or case differences (`glove-l-100`): case-folded and hyphen-tolerant; SKU search treats `-`, ` `, `_` as equivalents.

---

## Requirements *(mandatory)*

### Functional Requirements

**Core search**

- **FR-001**: System MUST expose a public search endpoint returning hits, total, page, pageSize, facets, and a `didYouMean` suggestion (may be null). Hits MUST include fields sufficient to render a product card: id, localized name, primary image, availability, `restricted` flag, `priceToken` (delegated to spec 007-a for resolution).
- **FR-002**: System MUST index all `published` and `active` products from spec 005. Archived, draft, and soft-deleted products MUST NOT appear in customer search responses.
- **FR-003**: System MUST keep restricted products visible in results with the `restricted` flag and `priceToken` present, per Principle 8. A `restricted` filter toggle is available but off by default.

**Arabic normalization**

- **FR-004**: System MUST normalize Arabic text on both index and query: alef variants (`ا`/`أ`/`إ`/`آ`) folded, ya variants (`ي`/`ى`) folded, ta-marbuta (`ة`) folded to `ه`, diacritics (tashkeel) stripped, tatweel (`ـ`) stripped, and a seeded Arabic stopword list applied. EN queries apply standard lowercase + ASCII folding + English stopwords.
- **FR-005**: System MUST support mixed AR + EN queries by tokenizing through both analyzers and scoring the union of matches; the locale hint boosts matches in that language.

**Typo tolerance & ranking**

- **FR-006**: System MUST apply typo tolerance on natural-language queries: ≥ 4 characters allow 1 edit, ≥ 8 characters allow 2 edits; queries < 4 characters require exact prefix match.
- **FR-007**: System MUST rank results by relevance at launch, computed from: exact token match (highest), prefix match, typo match, field weight (name > brand > category > description > attribute), and a mild freshness boost (`published_at` recency). Admin-tunable ranking lives in Phase 1.5 (`1.5-d`).
- **FR-008**: System MUST short-circuit to exact SKU/barcode match when the query matches an SKU or barcode on any active variant; the matched product becomes the first result regardless of other relevance scores.

**Facets & sort**

- **FR-009**: System MUST support facets: `category` (id + localized name + count), `brand` (id + localized name + count), `price_range` (per-market bucket definitions), `availability` (in-stock / out-of-stock + count), `restricted` (true/false + count). Facet counts MUST reflect the intersection of all other applied filters (standard faceted-search semantics).
- **FR-010**: System MUST support sort modes: `relevance` (default when `q` non-empty), `newest` (default when `q` empty), `price_asc`, `price_desc`. `price_*` sorts rely on a numeric price hint indexed per document per market; that hint is populated from spec 007-a at indexing time.

**Autocomplete**

- **FR-011**: System MUST expose a lightweight autocomplete endpoint returning up to 8 product suggestions, 3 brand suggestions, 3 category suggestions, each with id + localized label + slug. Queries as short as 2 characters trigger autocomplete.
- **FR-012**: System MUST meet the autocomplete latency target at p95 (SC-002) by keeping the autocomplete response payload distinct from the full search-hit payload (no media URLs, no price tokens).

**Synonyms & stopwords**

- **FR-013**: System MUST load a seeded AR + EN synonym set at launch (e.g., `glove ↔ gloves ↔ قفاز ↔ قفازات`) and a stopword set per locale. Synonym/stopword authoring surfaces are deferred to spec 1.5-d; Phase 1B supplies the seed files only.

**Reindex & propagation**

- **FR-014**: System MUST subscribe to catalog reindex events (`ProductCreated`, `ProductUpdated`, `ProductPublished`, `ProductArchived`, `ProductVariantChanged`, `ProductMediaChanged`, `CategoryTreeChanged`, `BrandChanged`) and apply each change as an incremental upsert or delete within 2 seconds at p95 (SC-001).
- **FR-015**: System MUST expose an admin-only full-reindex command (gated by `search.reindex` permission from spec 004) that rebuilds the index from the catalog store in the background and swaps atomically — live queries never observe a partial index.
- **FR-016**: System MUST retry a failed incremental index update with bounded exponential backoff; if the backlog exceeds a documented threshold, the service MUST emit an alert event and surface the backlog depth on a health endpoint.

**Empty-state UX**

- **FR-017**: System MUST return an empty-state payload on zero-result queries with: `items: []`, `didYouMean` (single spelling correction or null), `suggestedFilterRelaxations` (list of applied filters most likely to unblock results, ranked), and `suggestions` (up to 5 top-level categories).

**Error & limits**

- **FR-018**: System MUST clamp query strings at 200 characters and paginated depth at pageSize × page ≤ 20 000; both clamps surface in the response payload as flags rather than errors.
- **FR-019**: System MUST return 503 with a localized error envelope and correlation id when the underlying provider is unreachable; the envelope MUST NOT expose provider-specific errors.

**Provider abstraction**

- **FR-020**: System MUST route all search/autocomplete/reindex operations through an internal provider port; adapters MUST be swappable without caller code changes (Principle 26).

**Observability**

- **FR-021**: System MUST log every query with: query string, locale, market, applied filters, sort, hit count, zero-result flag, p95-relevant timing, correlation id, and a hashed caller id (salted per market). No raw customer id, no payment data, no PII fields beyond the opaque hash.
- **FR-022**: System MUST expose a health endpoint returning: provider reachability, last incremental event applied timestamp, current backlog depth, and active full-reindex job id (if any).

**Multi-market**

- **FR-023**: System MUST scope every query to a single `market_code` (EG or KSA). Products not available in the caller's market MUST NOT appear in results. A product's `market_code` comes from spec 005 per ADR-010.

**Auditing**

- **FR-024**: System MUST emit audit events for every admin-initiated action (`search.reindex.started`, `search.reindex.completed`, `search.reindex.failed`) with actor, correlation id, job id, document count, duration per Principle 25. Customer queries are NOT audited (they go to observability logs per FR-021, not the audit log).

### Key Entities

- **IndexedProduct**: id (product id from spec 005), `market_code`, `name_ar`, `name_en`, `description_ar`, `description_en`, `brand_id`, `brand_name_ar`, `brand_name_en`, `category_ids[]`, `category_breadcrumb_ar`, `category_breadcrumb_en`, `variants[] { id, sku, barcode, axis_values }`, `attributes_flat[] { key, display_label_ar, display_label_en, value_rendered }`, `primary_image_ref`, `available`, `restricted`, `restriction_reason_code` (null if not restricted), `price_hint_numeric` (per market), `price_token`, `published_at`, `updated_at`.
- **SearchHit**: id, localized name, primary image ref, availability, restricted, priceToken, matched-field highlights (for UI emphasis).
- **FacetBucket**: axis (`category` | `brand` | `price_range` | `availability` | `restricted`), value, localized label, count.
- **AutocompleteResponse**: `products: [{id, label, slug}]`, `brands: [{id, label, slug}]`, `categories: [{id, label, slug}]`.
- **ReindexJob**: id, actor_id, started_at, completed_at, document_count, duration_ms, status (`running` | `completed` | `failed`), correlation_id.
- **SynonymGroup** (seeded): locale, terms[].
- **StopwordSet** (seeded): locale, terms[].
- **Audit Event** (emitted, not owned): consumed by spec 003 audit-log module.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 95% of catalog reindex events (product publish/update/archive/variant/media change) propagate to the search index within 2 seconds of event emission — verified by an event-latency integration test.
- **SC-002**: 95% of search queries returning up to 24 hits respond within 500 ms end-to-end; 95% of autocomplete queries respond within 150 ms — measured in a k6 load test at nominal launch traffic.
- **SC-003**: 100% of published-and-active products in the catalog are retrievable by (a) exact SKU match, (b) exact barcode match where barcode is set, (c) at least one keyword from their AR name, and (d) at least one keyword from their EN name — verified by a coverage test that iterates every seeded product.
- **SC-004**: 100% of restricted products appear in search results with the `restricted` flag and `priceToken` populated — verified by a snapshot test per restriction reason code.
- **SC-005**: Arabic normalization correctness: a test corpus of 100 curated AR query/expected-result pairs achieves ≥ 95% top-5 match rate; alef, ya, ta-marbuta, diacritics, and tatweel variants are all proven equivalent.
- **SC-006**: Zero-result queries return a structured empty-state payload with `didYouMean` or `suggestedFilterRelaxations` or `suggestions` populated in ≥ 80% of cases on a curated zero-result query corpus.
- **SC-007**: A full reindex of 10 k products completes within 5 minutes end-to-end on the baseline deployment and does not cause any query p95 regression > 10% during the run.
- **SC-008**: Provider swap is demonstrated: a stub provider replacement requires zero changes to any file outside `services/backend_api/Features/Search/` — verified by a code-review checklist and an integration test using the stub adapter.

---

## Assumptions

- The launch search engine is Meilisearch per ADR-005, hosted in Azure Saudi Arabia Central alongside the database per ADR-010. A plan-level provider port keeps the engine swappable.
- Price resolution is delegated to spec 007-a. Search indexes a numeric `price_hint` per market (for sort) and returns a `priceToken` in hits. The UI calls spec 007-a to render the canonical price.
- Availability is delegated to spec 008. Search indexes the current `available` boolean from spec 008 and refreshes it on inventory events (bridge event from spec 008 to search lands when spec 008 ships; until then, availability is computed from the catalog `active` flag).
- Click tracking, query analytics dashboards, and search-funnel conversion analysis are deferred to spec 028 (Phase 1.5 — analytics-audit-monitoring). Phase 1B supplies structured query logs only.
- Admin synonym/stopword management UI is deferred to spec 1.5-d. Phase 1B ships seeded synonym + stopword sets checked into the repo as editable YAML.
- Personalization (per-user boost, recently-viewed, recommended-for-you) is out of scope; lands in spec 1.5-b.
- The reindex event transport is MediatR in-process (per ADR-003). Future bus swap is out of scope for this spec.
- Full-reindex is a single-writer operation; concurrent full-reindex requests are rejected with 409.
- Cross-market search is not offered; every query targets exactly one market. A future cross-market admin search could be added in Phase 1.5 without contract break (extra query parameter).
- "Relevance" ranking at launch uses the provider's default tuning plus the field weights declared in FR-007. Fine-grained ranking rules are deferred.

---

## Dependencies

- **003 · shared-foundations** — error envelope, correlation-id middleware, shared-contracts pipeline, structured logging/observability kernel.
- **005 · catalog** — source of truth for product, variant, brand, category, attribute, restriction, media data; emits the reindex events this spec consumes.

**Consumed by (forward-looking; informational only)**:

- **014 · customer-app-shell** — Flutter customer storefront consumes search + autocomplete endpoints.
- **016 · admin-dashboard-shell** — admin UI for the reindex command and health page.
- **028 · analytics-audit-monitoring** (Phase 1.5) — consumes query logs for analytics.
- **1.5-d · search-synonyms-ops-console** (Phase 1.5) — admin console for synonym/stopword/ranking tuning.
