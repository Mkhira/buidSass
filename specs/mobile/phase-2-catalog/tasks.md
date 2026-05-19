# Tasks — Phase 2: Catalog

> Ordered. Each task = one PR-sized change. Boxes for status tracking.

## Block A — Gateways & shared widgets

### T-2.1 · CatalogGateway
- **Goal:** interface + impl for the 4 catalog endpoints.
- **Files:** `features/catalog/data/{catalog_gateway,catalog_gateway_impl}.dart`, `models/*`.
- **Steps:** Method per endpoint; in-memory cache keyed by `(locale, market, query)`; subscribe to `SessionStore.stateStream` for invalidation.
- **DoD:** unit tests for each method + cache TTL test using fake clock.

### T-2.2 · PricingGateway (preview only)
- **Goal:** `POST /customer/pricing/price-cart`.
- **Files:** `features/pricing/data/{pricing_gateway,pricing_gateway_impl}.dart`.
- **DoD:** unit tests; preview with empty cart returns 422 mapped to `ValidationFailure`.

### T-2.3 · InventoryGateway
- **Goal:** `GET /v1/customer/inventory/availability`.
- **Files:** `features/inventory/data/{inventory_gateway,inventory_gateway_impl}.dart`.
- **DoD:** unit tests; batch by productIds.

### T-2.4 · ReviewsAggregatesGateway (public)
- **Goal:** the two `/v1/public/reviews/aggregates*` endpoints. Phase 7 will extend.
- **Files:** `features/reviews/data/reviews_aggregates_gateway*.dart`.
- **DoD:** unit tests.

### T-2.5 · Shared widgets
- **Files:** `features/catalog/widgets/{product_card,rating_block,stock_badge,restriction_gate,price_label,filter_bar}.dart`.
- **DoD:** golden tests per widget in AR + EN.

## Block B — Screens

### T-2.6 · Home (S-2.1) — verify existing screen
- **Files:** `features/home/screens/home_screen.dart` + new `HomeBloc`.
- **DoD:** S-2.1 acceptance criteria all green.

### T-2.7 · Categories list (S-2.2)
- **Files:** new `categories_list_screen.dart` + `CategoriesListBloc`.
- **DoD:** S-2.2 criteria green.

### T-2.8 · Brands list (S-2.4)
- **Files:** new `brands_list_screen.dart` + `BrandsListBloc`.
- **DoD:** S-2.4 criteria green.

### T-2.9 · ProductListBloc + shared screen (S-2.5)
- **Files:** new `product_list_bloc.dart`, `product_list_screen.dart`.
- **DoD:** unit + widget tests; reusable by S-2.3.

### T-2.10 · Category detail (S-2.3) — migrate existing
- **Files:** `features/catalog/screens/listing_screen.dart` adapts to `ProductListBloc` with `categorySlug` query.
- **DoD:** S-2.3 criteria green; old tests updated.

### T-2.11 · PDP (S-2.6) — verify + complete
- **Files:** `features/catalog/screens/product_detail_screen.dart` + new `ProductDetailBloc`.
- **Steps:** four-call orchestration (product → pricing + availability + aggregates); restriction gate; price-drift badge.
- **DoD:** S-2.6 criteria green.

## Block C — Cross-cutting

### T-2.12 · RestrictionGate wired on cards
- **Goal:** apply gate to product cards used in lists + home strip.
- **DoD:** widget test asserts disabled CTA + verification deep-link.

### T-2.13 · Locale/market cache invalidation integration test
- **DoD:** test switches locale and asserts catalog cache is cleared.

## Block D — Phase exit

### T-2.14 · Analyze + tests
- **DoD:** `flutter analyze` zero warnings; `flutter test` green.

### T-2.15 · Update overview doc status row
- **DoD:** Phase 2 row in `docs/mobile-app-screen-api-plan.md` §8 → **Done**.

## Screen ↔ task map

| Screen | Tasks |
|---|---|
| S-2.1 Home | T-2.6 |
| S-2.2 Categories list | T-2.7 |
| S-2.3 Category detail | T-2.10 |
| S-2.4 Brand list | T-2.8 |
| S-2.5 Product list | T-2.9 |
| S-2.6 PDP | T-2.11 |
| S-2.7 Rating block (component) | T-2.5 |
| S-2.8 Stock badge (component) | T-2.5 |
| Gateways | T-2.1 … T-2.4 |
| Cross-cutting | T-2.12, T-2.13 |
| Exit | T-2.14, T-2.15 |
