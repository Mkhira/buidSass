# Quickstart — Phase 3: Search

## Prerequisites

- Phase 1 + Phase 2 complete.
- Backend search module ready; Meilisearch instance indexed with the catalog seed data.

## Run

```sh
cd apps/customer_flutter
flutter pub get
flutter run
```

## Manual smoke (Phase 3 exit gate)

1. **Entry.** Tap the Home search bar → keyboard appears, focus on search input, recent + popular suggestions render.
2. **Autocomplete EN.** Type "til" → suggestions including "tile" + top-matches strip.
3. **Autocomplete AR normalization.** Switch locale to AR; type "صابون" → results include products that index as "سَابون" (normalized).
4. **Submit query.** Tap a suggestion or press search → results screen with facets + sort.
5. **Facets.** Toggle a brand chip → list reloads; result count updates.
6. **Sort.** Change sort to "Price low to high" → list reorders.
7. **Pagination.** Scroll to bottom → inline spinner → next page appends.
8. **Empty.** Search for "qwertyuiop" → empty state with "Try a different search" CTA.
9. **Lookup manual.** Open `/search/lookup` (link from More or empty state). Enter a known SKU → "Found product X" → tap Open → PDP.
10. **Lookup scan.** Tap Scan → grant camera → scan a known barcode → auto-routes to PDP.
11. **Lookup permission denied.** Reject camera → CTA "Open settings" works.
12. **Recent searches.** Recent strip shows last 10 queries; clear-recent works.

## Automated

```sh
flutter analyze
flutter test test/features/search/
```

## Troubleshooting

- **Autocomplete fires too aggressively:** confirm debounce is 250 ms and `switchMap` is in place.
- **AR normalization fails:** server-side issue — verify Meilisearch index settings.
- **Barcode scan freezes:** make sure the screen disposes the scanner controller on `dispose()`.
