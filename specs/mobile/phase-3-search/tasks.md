# Tasks — Phase 3: Search

## Block A — Data

### T-3.1 · SearchGateway
- **Files:** `features/search/data/{search_gateway,search_gateway_impl}.dart`, `models/*`.
- **DoD:** unit tests for the 3 endpoints.

### T-3.2 · RecentSearchesStore
- **Files:** `features/search/data/recent_searches_store.dart`.
- **Steps:** `shared_preferences` key per-account if signed in (account-id namespace), else anonymous bucket. Cap 10, LRU.
- **DoD:** unit tests.

## Block B — Search Bloc + screen

### T-3.3 · SearchBloc
- **Files:** `features/search/bloc/search_bloc.dart`.
- **Steps:** debounced autocomplete; switchMap cancellation; recent persistence; results pagination.
- **DoD:** bloc_test coverage of all transitions.

### T-3.4 · SearchScreen (S-3.1 + S-3.2 + S-3.3)
- **Files:** `features/search/screens/search_screen.dart`.
- **DoD:** widget tests for every state × AR/EN.

## Block C — Lookup

### T-3.5 · LookupBloc
- **Files:** `features/search/bloc/lookup_bloc.dart`.
- **DoD:** bloc_test.

### T-3.6 · LookupScreen (S-3.4)
- **Files:** `features/search/screens/lookup_screen.dart`.
- **Steps:** integrate `mobile_scanner` (or equivalent); permission flow; manual input.
- **DoD:** S-3.4 acceptance criteria green.

## Block D — Wiring + exit

### T-3.7 · Home search bar route
- **Goal:** Phase-2 Home `SearchBar` taps route to `/search` with focus.
- **DoD:** integration test.

### T-3.8 · Analyze + tests
- **DoD:** zero warnings; tests green.

### T-3.9 · Update overview doc status row
- **DoD:** Phase 3 → **Done** in `docs/mobile-app-screen-api-plan.md` §8.

## Screen ↔ task map

| Screen | Tasks |
|---|---|
| S-3.1 / S-3.2 / S-3.3 | T-3.3, T-3.4 |
| S-3.4 Lookup | T-3.5, T-3.6 |
| Wiring | T-3.7 |
| Exit | T-3.8, T-3.9 |
