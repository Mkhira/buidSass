# Quickstart — Phase 2: Catalog

## Prerequisites

- Phase 1 foundation running (gateway pattern + session store + theme + router).
- Backend seeded with at least: 3 categories, 2 brands, 12 products across categories, with a mix of `restricted=true/false`. The catalog module's seed script under `services/backend_api/Modules/Catalog/Seed/` is sufficient.
- Optional: configure the price engine seed (`services/backend_api/Modules/Pricing/Seed/`) so PDP shows realistic numbers.

## Run

```sh
cd apps/customer_flutter
flutter pub get
flutter run
```

## Manual smoke (Phase 2 exit gate)

Execute on iOS + Android.

1. **Home renders all four sections.** Cold open → spinner → categories tiles, brands strip, featured strip with rating + stock badges.
2. **Categories tab → category detail.** Tap a category → see product grid with facets (Brand chips, price range).
3. **Filter + sort.** Pick a brand chip → list reloads. Change sort to "Price low to high" → list reorders.
4. **Pagination.** Scroll to the bottom; expect inline spinner; next page appends.
5. **PDP open.** Tap a product card → PDP renders gallery, name, price (from engine), rating, stock badge, description, qty stepper.
6. **PDP price drift badge.** Set the same product's price up on the backend (or run `dotnet run --project tools/dev/PriceBumper`); reopen PDP from cache → "Updated just now" badge appears.
7. **Restricted product.** Open a `restricted=true` product → CTA shows "Verification required" with a deep-link to the Phase 7 verification CTA.
8. **Locale switch.** From `/more/locale`, switch to AR → return to Home → all text RTL, prices formatted per locale, category names localized.
9. **Market switch.** Switch market SA → EG → prices and currency update; some categories may differ if seed is market-specific.
10. **Offline.** Toggle airplane mode after a warm Home → Home renders cached data with offline badge; PDP behaves similarly.

## Automated tests

```sh
cd apps/customer_flutter
flutter analyze
flutter test test/features/catalog/
flutter test test/features/pricing/
flutter test test/features/inventory/
```

## Troubleshooting

- **PDP price label flashes:** make sure `PriceLabel` consumes `Bloc.state.priceQuote` (not `priceHint`) once available.
- **Restricted gate doesn't show:** product seed may have `restricted: false`. Bump it via admin and refresh.
- **Cache stale after locale switch:** verify `CatalogGatewayImpl` subscribes to `SessionStore.stateStream` and evicts on change.
