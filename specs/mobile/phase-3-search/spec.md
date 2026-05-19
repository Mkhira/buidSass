# Spec — Phase 3: Customer Mobile Search

> **Phase:** 3 of 8 · **Owner:** mobile + search · **Last updated:** 2026-05-19
> **OpenAPI source:** [`openapi.search.json`](../../../services/backend_api/openapi.search.json)
> **Endpoint count:** 3 customer-tagged
> **Depends on:** Phase 1 (foundation), Phase 2 (product card widget + catalog gateway).

---

## 1. Goal

Deliver search end-to-end: entry, autocomplete with synonyms + Arabic normalization, results with facets/sort/pagination, lookup by SKU/barcode. Backed by Meilisearch (ADR-005) via the backend search module.

After Phase 3, a customer can type in AR or EN, get instant suggestions, browse results, and resolve a barcode/SKU to a PDP.

## 2. User roles

| Role | Phase 3 scope |
|---|---|
| Unauthenticated visitor | All 3 endpoints — search is open. |
| Authenticated customer / B2B buyer | Same surface; results may include personalized pricing in the matrix-friendly card render (Phase 2 contract). |

## 3. Business rules

| BR | Rule | Reference |
|---|---|---|
| BR-1 | Search debounce: 250 ms after last keystroke before firing autocomplete. | Principle 12 |
| BR-2 | Arabic normalization is server-side; client sends the raw query string. | Principle 12 |
| BR-3 | Recent searches persist locally (max 10), per-account where signed in, anonymous otherwise. Cleared by user on demand. | UX standard |
| BR-4 | Search results use the same `ProductCard` widget as Phase 2 lists (one source of truth for restricted UX + rating + stock + price). | Principle 12 |
| BR-5 | Lookup screen accepts manual SKU entry and barcode scan (mobile camera). On exact match, route directly to PDP. On no match, show empty state with link to search. | Principle 12 |
| BR-6 | Sort options + facet names are server-driven via the response payload — never hardcoded in the client beyond a default fallback. | Principle 12 |
| BR-7 | Empty-query autocomplete returns recent + popular categories from server; client also surfaces local recent searches above server suggestions. | UX standard |

## 4. Screens

### S-3.1 Search entry

**Status:** Planned
**Route:** `/search` · **Bottom nav:** hidden (modal-style entry from Home search bar)
**Wireframe:** [`#phase-3-search-entry`](../../../docs/mobile-screens-wireframes.md#phase-3-search-entry--s-31-search-entry)

#### Endpoints used
None on mount.

#### UI states
initial (focus input, show recent + popular), typing → routes to S-3.2 inline as the same screen.

#### Bloc scaffold
- `SearchBloc` covers S-3.1 + S-3.2 + S-3.3 as a single Bloc with multiple states (entry → autocomplete → results).
- Events: `SearchEntered`, `SearchQueryChanged(q)`, `SearchSubmitted`, `SearchFacetToggled(facet)`, `SearchSortChanged(sortKey)`, `SearchPageRequested`, `SearchRecentCleared`, `SearchRecentTapped(q)`.
- States: `SearchIdle(recent, popular)`, `SearchAutocompleting(q)`, `SearchAutocompleted(suggestions, topMatches)`, `SearchResults(q, facets, sort, page, items, hasMore)`, `SearchEmpty(q)`, `SearchFailure(reason, correlationId)`.

#### Acceptance criteria
- [ ] Focus input on entry.
- [ ] Recent searches list ≤ 10; tap → executes query immediately.
- [ ] Clear-recent action confirmable.
- [ ] AR + EN.
- [ ] Tests.

#### Edge cases
- First-ever visit, no recent → render only popular section (server-driven).

---

### S-3.2 Autocomplete

**Route:** same as S-3.1 (`/search` with typing)
**OpenAPI source:** `openapi.search.json`
**Wireframe:** [`#phase-3-autocomplete`](../../../docs/mobile-screens-wireframes.md#phase-3-autocomplete--s-32-autocomplete)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/search/autocomplete | 250 ms debounce after keystroke | safe* | request carries `query` + market |

#### Response data shape
```json
{
  "suggestions": [{ "label": "string", "kind": "term | category | brand", "linkSlug": "string?" }],
  "topMatches": [{ "productId": "uuid", "slug": "string", "name": "string", "imageUrl": "string", "priceHint": { "amount": "120.00", "currency": "SAR" } }]
}
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| autocompleting | typing within debounce | spinner inline |
| autocompleted | 2xx | suggestion list + top-matches strip |
| empty | empty results | "No matches — try lookup" CTA |
| error-5xx | 5xx | inline retry on the suggestions row |
| offline | network | recent searches only |

#### Acceptance criteria
- [ ] Debounce 250 ms.
- [ ] Cancel in-flight requests when the query changes again before response.
- [ ] Top-matches strip taps route directly to PDP.
- [ ] AR normalization tested end-to-end (e.g., "صابون" matches products with "سَابون").
- [ ] Tests.

---

### S-3.3 Search results

**Route:** `/search?q=...` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.search.json`
**Wireframe:** [`#phase-3-results`](../../../docs/mobile-screens-wireframes.md#phase-3-results--s-33-search-results)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/search/products | submit / facet/sort change / next-page | safe* | request carries query + facets + sort + paging |

#### Response data shape
```json
{
  "items": [ /* product card shape — same as Phase 2 catalog product list */ ],
  "page": 1,
  "pageSize": 24,
  "totalCount": 312,
  "facets": [
    {
      "key": "brand",
      "label": "Brand",
      "type": "checkbox | range | radio",
      "options": [{ "value": "brand-x", "label": "Brand X", "count": 12 }]
    }
  ],
  "sortOptions": [{ "key": "relevance", "label": "Relevance" }, { "key": "priceAsc", "label": "Price low to high" }]
}
```

#### UI states
loading skeleton → results grid + facet panel; empty (no results) → suggestion + lookup CTA; error/offline standard.

#### Acceptance criteria
- [ ] Facets + sort options driven by server response (BR-6).
- [ ] Reusing Phase 2 `ProductCard` + `RestrictionGate` + `RatingBlock` + `StockBadge`.
- [ ] Pagination via append.
- [ ] Persist last query in URL for back-stack restore.
- [ ] Tests.

#### Edge cases
- 0 results → suggest related queries from `suggestions` field in payload (if present).
- Facet combo zero results → keep facet panel visible so user can dial back.

---

### S-3.4 Lookup (SKU/barcode)

**Status:** Planned
**Route:** `/search/lookup` · **Bottom nav:** hidden
**OpenAPI source:** `openapi.search.json`
**Wireframe:** [`#phase-3-lookup`](../../../docs/mobile-screens-wireframes.md#phase-3-lookup--s-34-lookup-skubarcode)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| POST | /v1/customer/search/lookup | submit | safe* | request carries `sku` OR `barcode` |

#### Response data shape
```json
{
  "match": {
    "productId": "uuid?",
    "slug": "string?",
    "name": "string?",
    "kind": "sku | barcode"
  },
  "matched": true
}
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| initial | mount | scan + manual entry inputs |
| scanning | camera open | overlay |
| looking-up | submit | spinner |
| matched | 2xx + match | "Found product X" + Open CTA → PDP |
| no-match | 2xx + matched=false | "No product — try search" + Search CTA |
| error-5xx | 5xx | retry banner |
| offline | network | offline badge |
| permission-denied | camera denied | settings deep-link CTA |

#### Bloc scaffold
- `LookupBloc`.
- Events: `LookupStarted`, `LookupScanRequested`, `LookupSubmitted(value, kind)`, `LookupScanResult(value)`.
- States: `LookupForm`, `LookupScanning`, `LookupLooking`, `LookupMatched(slug, name)`, `LookupNoMatch`, `LookupFailure(reason, correlationId)`.

#### Acceptance criteria
- [ ] Camera permission requested only on Scan tap.
- [ ] Auto-submit on barcode scan success.
- [ ] Manual SKU entry accepts trimmed input.
- [ ] Match routes to `/products/{slug}` (PDP).
- [ ] AR + EN.
- [ ] Tests.

#### Edge cases
- Barcode scanned twice in quick succession ⇒ debounce 1s.
- Long manual SKU strings ⇒ no client length cap; server validates.

---

## 5. Acceptance criteria — phase-wide

- [ ] All 4 screens pass per-screen DoD.
- [ ] Single `SearchBloc` orchestrates entry → autocomplete → results.
- [ ] `LookupBloc` separate.
- [ ] Recent searches persisted locally (max 10) and cleared on demand.
- [ ] AR normalization verified end-to-end with at least one editorial test query.
- [ ] `flutter analyze` + `flutter test` green.
- [ ] Overview doc §8 row → **Done**.

## 6. Dependencies

- **Upstream:** Phase 1 foundation, Phase 2 widgets (`ProductCard`, `RestrictionGate`).
- **Downstream:** Phase 4 cart can deep-link to search; Phase 5 orders can deep-link to search for reorder discovery.

## 7. Out of scope

- Voice search.
- Search advertising / promoted results.
- Search analytics dashboards (admin).

## 8. References

- Principles 4, 7, 12, 27, 28.
- ADR-005 (Meilisearch backend).
