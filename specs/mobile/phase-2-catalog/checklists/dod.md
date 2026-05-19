# DoD Checklist — Phase 2: Catalog

## Gateways

- [ ] `CatalogGateway` + impl + cache (TTL + invalidation on locale/market change).
- [ ] `PricingGateway.priceCart()` returns typed quote + applied promotions.
- [ ] `InventoryGateway.availability()` batches by productIds.
- [ ] `ReviewsAggregatesGateway` for public batch + single endpoints.

## Shared widgets

- [ ] `ProductCard` renders restricted gate + rating + stock badge + price label.
- [ ] `RatingBlock` (S-2.7).
- [ ] `StockBadge` (S-2.8).
- [ ] `RestrictionGate` reused on cards + PDP.
- [ ] `PriceLabel` formats per locale + market currency.
- [ ] `FilterBar` reads facets from server response.

## Screens

- [ ] S-2.1 Home: 4-call orchestration; progressive render; restricted-card gate.
- [ ] S-2.2 Categories list.
- [ ] S-2.3 Category detail: filters, sort, pagination, pull-to-refresh.
- [ ] S-2.4 Brand list.
- [ ] S-2.5 Product list (brand) shares `ProductListBloc` with S-2.3.
- [ ] S-2.6 PDP: 4-call orchestration; price drift badge; markdown description; gallery; qty stepper; restricted gate.
- [ ] All screens render every UI state in AR + EN.

## Business rules

- [ ] PDP price always from price engine (BR-2).
- [ ] No hardcoded restricted lists (BR-7).
- [ ] Stock badge from inventory availability (BR-3).
- [ ] Rating block from public aggregates (BR-4).
- [ ] Cache invalidates on locale/market change (BR-5).

## Phase exit

- [ ] `flutter analyze` zero warnings.
- [ ] `flutter test` green.
- [ ] Smoke test from `quickstart.md` recorded on iOS + Android.
- [ ] §8 row in `docs/mobile-app-screen-api-plan.md` flipped to **Done**.
