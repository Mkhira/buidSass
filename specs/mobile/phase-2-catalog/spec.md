# Spec — Phase 2: Customer Mobile Catalog (Home, Categories, Brands, PDP)

> **Phase:** 2 of 8 · **Owner:** mobile + catalog · **Last updated:** 2026-05-19
> **OpenAPI sources:** [`openapi.catalog.json`](../../../services/backend_api/openapi.catalog.json), [`openapi.pricing.json`](../../../services/backend_api/openapi.pricing.json), [`openapi.inventory.json`](../../../services/backend_api/openapi.inventory.json), [`openapi.reviews.json`](../../../services/backend_api/openapi.reviews.json)
> **Endpoint count:** 4 catalog + 1 pricing (preview) + 1 inventory + 2 public-reviews = 8 customer-callable
> **Index:** [`docs/mobile-app-screen-api-plan.md`](../../../docs/mobile-app-screen-api-plan.md)
> **Depends on:** Phase 1 foundation.

---

## 1. Goal

Deliver the catalog browsing surface: home, categories, brands, product lists, and product detail (PDP). PDP includes ratings (read-only public aggregates), stock badge, restricted-product UX, and a PDP-level price preview that runs through the centralized pricing engine.

After Phase 2, an unauthenticated visitor can browse the catalog freely; an authenticated customer additionally sees personalized pricing where the engine returns business-tier rates.

## 2. User roles

| Role | Description | Phase 2 scope |
|---|---|---|
| Unauthenticated visitor | May browse home, categories, brands, lists, PDP. Sees prices. Add-to-cart routes them to Login (Phase 1) before completing. | All read screens. |
| Authenticated consumer | As above + add-to-cart succeeds; sees consumer pricing. | All. |
| Authenticated B2B buyer | As above + sees business-tier pricing where applicable (Principle 10 — pricing engine returns the tier). | All. |
| Restricted-product gated user | Sees the price + a disabled add-to-cart with "requires verification" explainer (Principle 8). | PDP, list cards. |

## 3. Business rules

| BR | Rule | Reference |
|---|---|---|
| BR-1 | Restricted products MUST remain visible. Prices MUST remain visible. Add-to-cart is gated and surfaces a route to Phase 7 Verification. | Principle 8 |
| BR-2 | Prices on PDP are rendered by calling `POST /customer/pricing/price-cart` with a single-item cart (preview mode) — UI never computes totals locally. | Principle 10 |
| BR-3 | Stock badge sources from `GET /v1/customer/inventory/availability` (batched per product list page; per product on PDP). | Principle 11 |
| BR-4 | Rating block on PDP sources from `GET /v1/public/reviews/aggregates/{product_id}`. List cards source from the batch endpoint `GET /v1/public/reviews/aggregates?product_ids=…` (preloaded in a single call per page). | Principle 15 |
| BR-5 | Categories, brands, product lists, and PDP are cached with 5-minute TTL by default. Cache invalidates on locale or market change. | Principle 12 (search has its own caching) |
| BR-6 | Arabic queries on list filter (e.g., size, color names) MUST be normalized server-side; client passes raw value as the user typed it. | Principle 4 |
| BR-7 | Restricted-product detection is driven by the `restricted` flag in the catalog response — never hardcoded by SKU/category in mobile. | Principles 8, 23 |
| BR-8 | All catalog reads are unauthenticated-safe. Anonymous users see all data the server permits. | Principle 3 |
| BR-9 | Product list "sort" options come from server-provided enum (e.g., `relevance | priceAsc | priceDesc | new | rating`); mobile never invents sort keys. | Principle 12 |
| BR-10 | Stale price detection on PDP: if a cached PDP is older than its TTL, refresh in background and show a subtle "Updated just now" badge if the price moved. | Principle 10 |

## 4. Screens

Template defined in [`docs/mobile-app-screen-api-plan.md` §5](../../../docs/mobile-app-screen-api-plan.md#5-per-screen-template-mandatory-schema).

---

### S-2.1 Home

**Status:** **Done** — `apps/customer_flutter/lib/features/home/screens/home_screen.dart` exists; verify against this spec.
**Route:** `/home` · **Bottom nav:** visible (Home tab)
**OpenAPI source:** `openapi.catalog.json` + `openapi.reviews.json`
**Wireframe:** [`#phase-2-home`](../../../docs/mobile-screens-wireframes.md#phase-2-home--s-21-home)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/catalog/categories | on mount + pull-to-refresh | safe | cached 5 min |
| GET | /v1/customer/catalog/brands | on mount | safe | cached 5 min |
| GET | /v1/public/reviews/aggregates | when featured product cards load | safe | batch by `product_ids` |
| GET | /v1/customer/inventory/availability | when featured product cards load | safe | batch by `productIds` |

#### Response data shape
```json
// categories
[
  { "id": "uuid", "slug": "bathroom-tiles", "name": { "ar": "...", "en": "..." }, "iconUrl": "https://..." }
]

// brands
[
  { "id": "uuid", "slug": "brand-x", "name": "string", "logoUrl": "https://..." }
]

// aggregates batch
[
  { "productId": "uuid", "ratingAverage": 4.7, "ratingCount": 125 }
]

// availability
[
  { "productId": "uuid", "inStock": true, "earliestDeliveryDate": "2026-05-20" }
]
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| loading | mount | skeleton tiles for categories + brands + featured strip |
| loaded | 2xx all | full home |
| empty | 2xx with all empty arrays | "Catalog coming soon" + retry |
| error-5xx | 5xx on any call | retry banner + correlation-id |
| offline | network | last-cached + offline badge |

#### Bloc scaffold
- `HomeBloc` orchestrates 4 parallel calls.
- Events: `HomeStarted`, `HomeRefreshed`.
- States: `HomeLoading`, `HomeLoaded({categories, brands, featured: [{product, rating, availability}]})`, `HomeEmpty`, `HomeFailure(reason, correlationId)`.

#### Acceptance criteria
- [ ] All four calls fired in parallel; UI renders progressively (e.g., categories appear as soon as their call returns, even if aggregates are still loading).
- [ ] Featured strip respects restricted-product UX (locked-cart icon on the card if the product is restricted and the viewer can't add).
- [ ] AR + EN editorial.
- [ ] Tests (Bloc + widget per state per locale).

#### Edge cases
- Empty categories array ⇒ Home shows only the search bar + a friendly empty state. Brands/featured strip hidden.
- Network drops between two of the four calls ⇒ partial render, retry banner only over the affected section.

---

### S-2.2 Categories list

**Status:** Planned (no dedicated screen yet; home renders inline categories)
**Route:** `/categories` · **Bottom nav:** visible (Categories tab)
**OpenAPI source:** `openapi.catalog.json`
**Wireframe:** [`#phase-2-categories`](../../../docs/mobile-screens-wireframes.md#phase-2-categories--s-22-categories-list)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/catalog/categories | mount + pull-to-refresh | safe | reuses Home cache |

#### Response data shape
See S-2.1.

#### UI states
Loading skeleton → grid; empty (no categories) → friendly empty state; error/offline as standard.

#### Bloc scaffold
- `CategoriesListBloc` (or reuse `HomeBloc`'s categories state via a selector if appropriate).
- Events: `CategoriesListStarted`, `CategoriesListRefreshed`.
- States: standard set.

#### Acceptance criteria
- [ ] Two-column grid on phones, three-column on tablets.
- [ ] Tile tap routes to `/categories/{slug}` (S-2.3).
- [ ] AR mirrors; localized name resolves per current locale.
- [ ] Tests.

#### Edge cases
- Long category names ⇒ ellipsize after 2 lines.

---

### S-2.3 Category detail

**Status:** **Done** — `apps/customer_flutter/lib/features/catalog/screens/listing_screen.dart` already handles category browsing; verify against this spec.
**Route:** `/categories/{slug}` · **Bottom nav:** visible
**OpenAPI source:** `openapi.catalog.json` + `openapi.reviews.json` + `openapi.inventory.json`
**Wireframe:** [`#phase-2-category-detail`](../../../docs/mobile-screens-wireframes.md#phase-2-category-detail--s-23-category-detail)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/catalog/categories/{slug}/products | mount, scroll-to-end, filter change | safe | filters: market, page, pageSize, sort, brand, priceMin, priceMax, restricted |
| GET | /v1/public/reviews/aggregates?product_ids=… | after products page returns | safe | batched |
| GET | /v1/customer/inventory/availability?productIds=… | after products page returns | safe | batched |

#### Response data shape
```json
// products page
{
  "items": [
    {
      "id": "uuid",
      "slug": "string",
      "name": "string",
      "imageUrl": "https://...",
      "price": { "amount": "120.00", "currency": "SAR", "tierLabel": "consumer | business" },
      "restricted": false,
      "brandSlug": "string"
    }
  ],
  "page": 1,
  "pageSize": 24,
  "totalCount": 312,
  "facets": {
    "brands": [{ "slug": "...", "count": 12 }],
    "priceRange": { "min": "10.00", "max": "999.00" }
  }
}
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| loading | first page | skeleton card grid |
| loaded | 2xx | card grid + filter chips + sort dropdown |
| loaded-paging | scroll near end | inline spinner at bottom |
| empty | 2xx empty | "No products match" + Clear filters |
| error-5xx | 5xx | retry banner |
| offline | network | cached + offline badge |

#### Bloc scaffold
- `CategoryDetailBloc`.
- Events: `CategoryStarted(slug)`, `CategoryFilterChanged(filter)`, `CategorySortChanged(sortKey)`, `CategoryPageRequested`, `CategoryRefreshed`.
- States: `CategoryLoading`, `CategoryLoaded(filters, page, items, hasMore, aggregatesById, availabilityById)`, `CategoryEmpty`, `CategoryFailure(reason, correlationId)`.

#### Acceptance criteria
- [ ] Filter chips reflect server-returned facets, not hardcoded.
- [ ] Sort dropdown values come from a server-supplied enum (current MVP: hardcode the enum to match server contract; document the eventual API).
- [ ] Restricted-product UX on each card (badge + disabled CTA).
- [ ] Pagination via append, not replace.
- [ ] Pull-to-refresh resets to page 1 + clears cache for this category.
- [ ] AR mirrors price separator and currency placement.
- [ ] Tests.

#### Edge cases
- A product was in stock at list-fetch time but not at the user's market ⇒ availability call returns `inStock=false` ⇒ card greys out.
- Filter combo yields 0 results ⇒ empty state with "Clear filters" CTA.

---

### S-2.4 Brand list

**Status:** Planned
**Route:** `/brands` · **Bottom nav:** visible (More tab → Brands link OR Home tab → "All brands")
**OpenAPI source:** `openapi.catalog.json`
**Wireframe:** [`#phase-2-brands`](../../../docs/mobile-screens-wireframes.md#phase-2-brands--s-24-brand-list)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/catalog/brands | mount | safe | cached 5 min |

#### Response data shape
See S-2.1 `brands`.

#### UI states
Loading skeleton; loaded grid; empty; error; offline.

#### Bloc scaffold
`BrandsListBloc` — events `BrandsStarted`, `BrandsRefreshed`; states standard.

#### Acceptance criteria
- [ ] Brand tile tap routes to `/brands/{slug}/products` (S-2.5 via category-products query with `brand=slug`).
- [ ] Logos lazy-load with `cached_network_image`.
- [ ] Tests.

#### Edge cases
- Missing logo ⇒ render initials in a colored chip.

---

### S-2.5 Product list (by brand or other entry)

**Status:** Planned (the existing `listing_screen.dart` covers category listing; brand-list entry needs verification or a wrapper)
**Route:** `/brands/{slug}/products` · **Bottom nav:** visible
**OpenAPI source:** `openapi.catalog.json` (same products endpoint, `brand=slug` query)
**Wireframe:** [`#phase-2-product-list`](../../../docs/mobile-screens-wireframes.md#phase-2-product-list--s-25-product-list-by-categorybrand)

> Same Bloc/UI as S-2.3, parameterized by the entry filter (brand vs category). Implementation may share most code via a `ProductListBloc` that accepts a `ProductListQuery` value object.

#### Acceptance criteria
- [ ] One shared list widget used by S-2.3 and S-2.5.
- [ ] Brand badge shown on cards when entry is a category (so users see brand cross-context); hidden when entry is a brand list (no need to repeat the same brand on every card).
- [ ] Tests.

---

### S-2.6 Product detail (PDP)

**Status:** **Done** — `apps/customer_flutter/lib/features/catalog/screens/product_detail_screen.dart` exists; verify and complete.
**Route:** `/products/{slug}` · **Bottom nav:** visible
**OpenAPI source:** `openapi.catalog.json` + `openapi.pricing.json` + `openapi.inventory.json` + `openapi.reviews.json`
**Wireframe:** [`#phase-2-product-detail`](../../../docs/mobile-screens-wireframes.md#phase-2-product-detail--s-26-product-detail-pdp)

#### Endpoints used
| Method | Path | When | Idempotent | Notes |
|---|---|---|---|---|
| GET | /v1/customer/catalog/products/{slug} | mount | safe | optional market query |
| POST | /customer/pricing/price-cart | after product loads (preview with qty=1) | safe* | engine call; explainable totals |
| GET | /v1/customer/inventory/availability?productIds={id} | after product loads | safe | |
| GET | /v1/public/reviews/aggregates/{product_id} | after product loads | safe | unauth ok |

#### Response data shape
```json
// product detail
{
  "id": "uuid",
  "slug": "string",
  "name": "string",
  "description": "markdown string",
  "imageUrls": ["https://..."],
  "videoUrl": "string?",
  "specs": [{ "key": "weight", "value": "5kg" }],
  "documents": [{ "url": "...", "label": "Datasheet" }],
  "categorySlug": "string",
  "brandSlug": "string",
  "restricted": false,
  "restrictionReason": "string?",     // present only when restricted
  "priceHint": { "amount": "120.00", "currency": "SAR" }   // hint only — engine is authoritative
}

// pricing preview
{
  "total": { "amount": "120.00", "currency": "SAR" },
  "lines": [{ "productId": "uuid", "unitPrice": "120.00", "qty": 1, "discount": "0.00" }],
  "appliedPromotions": []
}
```

#### UI states
| State | Trigger | What renders |
|---|---|---|
| loading | mount | shimmer over gallery + metadata |
| loaded | 2xx all | gallery, name, price, rating, stock, description, specs, docs, qty stepper, CTA |
| loaded-restricted | product.restricted=true | locked CTA + verification CTA |
| price-drift | preview total differs from `priceHint` after first render | subtle "Updated just now" badge next to price |
| error-5xx | 5xx | retry banner |
| offline | network | cached + offline badge |
| not-found | 404 | empty state "Product not available" + back |

#### Bloc scaffold
- `ProductDetailBloc`.
- Events: `ProductStarted(slug)`, `ProductQtyChanged(qty)`, `ProductAddToCartRequested`, `ProductRefreshed`.
- States: `ProductLoading`, `ProductLoaded(product, priceQuote, availability, rating, qty)`, `ProductRestricted(product, reason)`, `ProductNotFound`, `ProductFailure(reason, correlationId)`.
- `Add to Cart` interacts with Phase 4 `CartBloc` via shared `CartRepository` — emits an outbound event, not direct cart manipulation.

#### Acceptance criteria
- [ ] Gallery supports swipe, pinch-zoom, full-screen.
- [ ] Description renders markdown safely (no inline HTML).
- [ ] Rating block consumes `aggregates/{product_id}` (S-2.7).
- [ ] Stock badge consumes availability (S-2.8).
- [ ] Restricted product shows price + reason + verification CTA (deep-link to Phase 7 verification submit).
- [ ] PDP qty stepper bounded by available stock (when known).
- [ ] AR mirrors gallery dots and chevrons; description is LTR for English content even in AR locale where applicable (mixed-direction handling).
- [ ] Tests.

#### Edge cases
- Product flagged restricted but user is verified ⇒ CTA enabled normally (verification status from Phase 1 `me` + Phase 7 active-verification gate; in Phase 2 the gate is informational only — Phase 4 add-to-cart will enforce on the server).
- Out-of-stock ⇒ CTA replaced with "Notify when available" (out of Phase 2 scope; stub the entry).
- Price moved between PDP open and add-to-cart tap ⇒ Phase 4 handles drift via 409.

---

### S-2.7 Rating block (PDP sub-component)

**Status:** Planned (component, not a route)
**OpenAPI source:** `openapi.reviews.json`
**Wireframe:** [`#phase-2-rating-summary`](../../../docs/mobile-screens-wireframes.md#phase-2-rating-summary--s-27-rating-summary-block)

Sourced from `GET /v1/public/reviews/aggregates/{product_id}`. Renders:
- average ★ + numeric
- review count
- 5-bar histogram (server returns per-star counts)

Component lives at `lib/features/catalog/widgets/rating_block.dart`. No Bloc; receives data via PDP Bloc state. Localized "(125 reviews)" → "(١٢٥ تقييم)".

---

### S-2.8 Stock badge (PDP + list sub-component)

**Status:** Planned (component)
**OpenAPI source:** `openapi.inventory.json`
**Wireframe:** [`#phase-2-stock-badge`](../../../docs/mobile-screens-wireframes.md#phase-2-stock-badge--s-28-stock-badge)

Sourced from `GET /v1/customer/inventory/availability`. Renders one of:
- "In stock — delivery by {date}" (green)
- "Limited stock" (amber) — when server flags `lowStock=true`
- "Out of stock" (grey)

Component: `lib/features/catalog/widgets/stock_badge.dart`. Localized + market-aware date formatting.

---

## 5. Edge cases (cross-screen)

- **Restricted-product UX everywhere:** every card and PDP respects `restricted` flag; never assume a category-wide rule. The verification CTA on PDP (S-2.6) is the only place the user is funneled into Phase 7.
- **Catalog cached across locale change:** invalidate cache on locale change so localized names re-fetch.
- **Slug containing non-ASCII characters:** Flutter `Uri.parse` handles UTF-8; verify on AR slugs (rare; backend may slugify to ASCII).
- **Image CDN outage:** placeholder image + the actual error doesn't block the screen.
- **PDP refresh while user has typed a qty:** preserve the qty across the refresh.

## 6. Acceptance criteria — phase-wide

- [ ] All 6 screens + 2 sub-components above pass per-entry DoD.
- [ ] PDP price always comes from `POST /customer/pricing/price-cart` preview (BR-2).
- [ ] No hardcoded SKU lists, no hardcoded restricted-product lists (BR-7).
- [ ] One shared `ProductListBloc` for S-2.3 and S-2.5.
- [ ] `flutter analyze` + `flutter test` green.
- [ ] Smoke test from `quickstart.md` passes.
- [ ] `docs/mobile-app-screen-api-plan.md` §8 row for Phase 2 flipped to **Done**.

## 7. Dependencies

- **Upstream:** Phase 1 foundation (gateway pattern, session store, theming, router); backend Phase 1B specs 005 (catalog), 006 (search), 007-a (pricing), 008 (inventory), Phase 1D spec 022 (reviews public aggregates).
- **Downstream:** Phase 3 (Search) reuses the product card widget; Phase 4 (Cart/Checkout) consumes `priceQuote` from PDP and shares `RestrictionGate`.

## 8. Out of scope

- Wishlist / save-for-later — not in launch.
- Compare products — not in launch.
- "Notify when available" — stub only.
- Personalized recommendations — Phase 1.5.
- Catalog SEO routes — irrelevant on mobile.

## 9. Phase assignment & references

- **Phase:** 2 of 8.
- **Constitution references:** Principles 3, 4, 7, 8, 10, 11, 12, 15, 27.
- **ADR references:** ADR-002 (Bloc), ADR-005 (Meilisearch — search engine; consumed in Phase 3, not here).
