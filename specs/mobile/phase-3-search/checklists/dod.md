# DoD Checklist — Phase 3: Search

## Data

- [ ] `SearchGateway` with the 3 endpoints + typed responses.
- [ ] `RecentSearchesStore` with LRU cap of 10, account-namespaced.

## Bloc

- [ ] `SearchBloc` debounces query (250 ms) and cancels in-flight on newer queries.
- [ ] `LookupBloc` orchestrates manual + scan flows.

## Screens

- [ ] S-3.1 Entry: focus, recent, popular.
- [ ] S-3.2 Autocomplete: suggestions + top-matches strip.
- [ ] S-3.3 Results: facets + sort + pagination + restricted UX via shared widget.
- [ ] S-3.4 Lookup: scan + manual + permission flow.
- [ ] Every UI state in AR + EN.

## Wiring

- [ ] Home search bar routes to `/search` with focus.
- [ ] Lookup match routes to PDP.

## Phase exit

- [ ] `flutter analyze` clean.
- [ ] `flutter test` green.
- [ ] Smoke test recorded.
- [ ] §8 row → **Done**.
