# Implementation Plan — Phase 2: Catalog

> Companion to [`spec.md`](./spec.md).

## Module layout

```
apps/customer_flutter/lib/features/
├── catalog/
│   ├── data/
│   │   ├── catalog_gateway.dart           # interface (categories, brands, products, product detail)
│   │   ├── catalog_gateway_impl.dart      # Dio-backed
│   │   └── models/
│   ├── bloc/
│   │   ├── home_bloc.dart                 # orchestrates 4 calls (categories, brands, aggregates, availability for featured strip)
│   │   ├── categories_list_bloc.dart
│   │   ├── product_list_bloc.dart         # shared by S-2.3 (category) and S-2.5 (brand)
│   │   ├── brands_list_bloc.dart
│   │   └── product_detail_bloc.dart
│   ├── screens/
│   │   ├── home_screen.dart               # existing — relocate from features/home/ OR keep cross-feature
│   │   ├── categories_list_screen.dart
│   │   ├── listing_screen.dart            # existing — wraps product_list with category query
│   │   ├── brands_list_screen.dart
│   │   ├── product_list_screen.dart       # generic, parameterized
│   │   └── product_detail_screen.dart     # existing — verify
│   └── widgets/
│       ├── product_card.dart              # used by lists + home featured strip
│       ├── rating_block.dart              # S-2.7
│       ├── stock_badge.dart               # S-2.8
│       ├── restriction_gate.dart          # reused by Phase 4 add-to-cart enforcement
│       ├── price_label.dart               # renders priceQuote consistently
│       └── filter_bar.dart                # facets + sort
├── pricing/
│   └── data/
│       ├── pricing_gateway.dart           # POST /customer/pricing/price-cart
│       ├── pricing_gateway_impl.dart
│       └── models/
└── inventory/
    └── data/
        ├── inventory_gateway.dart         # GET /v1/customer/inventory/availability
        ├── inventory_gateway_impl.dart
        └── models/
```

`features/pricing/` and `features/inventory/` are introduced here because Phase 2 is the first consumer. Phase 4 reuses them.

`features/reviews/` is **not** scaffolded here; Phase 7 owns the customer review surface. Phase 2 only consumes the public aggregates endpoints via a thin `ReviewsAggregatesGateway` placed under `features/reviews/data/` so Phase 7 can extend it without restructuring.

## Bloc structure

Same shape as Phase 1 (sealed states/events, `Failure` from `core/error/failure.dart`).

`HomeBloc` orchestrates four sub-calls. Strategy: emit `HomeLoaded` progressively — start by emitting categories + brands (or skeletons) and complete with aggregates + availability as their calls return. Avoid a single-shot `await Future.wait` because it forces the slowest call to block the whole screen.

`ProductDetailBloc` orchestrates four sub-calls. PDP must surface stock and rating quickly; price preview is allowed to lag slightly. Sequence:
1. `product/{slug}` (blocks the screen).
2. In parallel after step 1: pricing preview + availability + aggregates.
3. Render PDP with placeholders for the still-loading sub-blocks.

## Routing additions

```
/home                            (existing)
/categories                      → CategoriesListScreen (new)
/categories/{slug}               → ListingScreen (existing wraps ProductList with categorySlug)
/brands                          → BrandsListScreen (new)
/brands/{slug}/products          → ProductListScreen (with brandSlug query)
/products/{slug}                 → ProductDetailScreen (existing — verify)
```

## Caching

`CatalogGatewayImpl` uses an in-memory cache keyed by `(locale, market, endpoint, query)` with 5-minute TTL. Cleared on locale or market change (subscribe to `SessionStore.stateStream`). The cache layer is a simple `Map<String, _CachedEntry>` in the impl — no third-party dependency.

PDP additionally pins the latest pricing preview by `(productId, qty)` so qty changes that pass through the price engine within 60 s reuse the cached result.

## Build sequence

See [`tasks.md`](./tasks.md). High-level:

1. **Gateways:** Catalog, Pricing, Inventory, Reviews-aggregates.
2. **Shared widgets:** `ProductCard`, `RatingBlock`, `StockBadge`, `RestrictionGate`, `PriceLabel`.
3. **Home** (S-2.1) — existing screen verified + retrofitted.
4. **Categories list** (S-2.2) — new screen.
5. **Brands list** (S-2.4) + **Product list** (S-2.5) — both consume shared `ProductListBloc`.
6. **Category detail** (S-2.3) — existing `listing_screen.dart` migrates to `ProductListBloc`.
7. **PDP** (S-2.6) — existing screen verified + retrofitted to the four-call orchestration.
8. **Restricted-product UX** — wire `RestrictionGate` on PDP + cards.
9. **Tests & analyze.**

## Testing strategy

- Unit (Bloc): happy + failure branches for each Bloc.
- Repo: each gateway tested with `MockClient`; cache TTL tested with fake clock.
- Widget: each screen renders all UI states in both locales.
- Integration: cold-open `/home`, browse to `/categories/bathroom-tiles`, open a PDP, restricted-product UX.
- Golden: PDP layout in both locales (covers AR mixed-direction).

## Risks specific to Phase 2

| # | Risk | Mitigation |
|---|---|---|
| 1 | PDP price preview lag makes price feel laggy on cold open. | Show `priceHint` from catalog immediately; replace with engine result + "Updated just now" badge if drift detected (BR-10). |
| 2 | Restricted-product UX duplication across cards + PDP. | Single widget `RestrictionGate` consumed in both places. |
| 3 | Cache invalidation on market switch is easy to miss. | Subscribe in `CatalogGatewayImpl` constructor; integration test asserts a market switch evicts. |
| 4 | Featured strip on Home depends on aggregates that may be unauthenticated-rate-limited. | Skip the strip silently on rate-limit; do not block Home. |

## Definition of Done

See `checklists/dod.md`.
