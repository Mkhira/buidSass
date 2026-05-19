# Implementation Plan — Phase 3: Search

## Module layout

```text
apps/customer_flutter/lib/features/search/
├── data/
│   ├── search_gateway.dart                # autocomplete, products, lookup
│   ├── search_gateway_impl.dart
│   ├── recent_searches_store.dart         # shared_preferences-backed
│   └── models/
├── bloc/
│   ├── search_bloc.dart                   # entry → autocomplete → results
│   └── lookup_bloc.dart
└── screens/
    ├── search_screen.dart                 # S-3.1 + S-3.2 + S-3.3 single screen with state-driven content
    └── lookup_screen.dart                 # S-3.4
```

## Bloc structure

`SearchBloc` is a single Bloc with multiple states (`SearchIdle`, `SearchAutocompleting`, `SearchAutocompleted`, `SearchResults`, `SearchEmpty`, `SearchFailure`). One Bloc avoids state thrash when the user types into the input and the screen toggles between sub-states.

Debouncing handled inside the Bloc with `EventTransformer<SearchQueryChanged>`. `debounceTime` and `switchMap` are **RxDart** operators, so this phase introduces `rxdart` as a `dependencies:` entry in `apps/customer_flutter/pubspec.yaml` (if not already present). Import where the transformer is defined:

```dart
import 'package:rxdart/rxdart.dart';

EventTransformer<E> debounce<E>(Duration d) =>
    (events, mapper) => events.debounceTime(d).switchMap(mapper);
```

Cancel-in-flight is achieved by `switchMap` semantics — newer query cancels older.

## Routing additions

```text
/search                       → SearchScreen (idle)
/search?q={q}                 → SearchScreen (results, restorable from back stack)
/search/lookup                → LookupScreen
```

## Build sequence

1. SearchGateway + RecentSearchesStore (T-3.1, T-3.2).
2. SearchBloc + screen (T-3.3, T-3.4).
3. LookupBloc + screen + camera permission flow (T-3.5, T-3.6).
4. Wire Home search bar → `/search` entry (T-3.7).
5. Tests & exit (T-3.8, T-3.9).

## Camera permission

Use `permission_handler` for camera permission. Defer the request to the moment the user taps Scan — never on screen mount.

If the system permission is permanently denied, surface a "Open settings" CTA via `openAppSettings()`.

## Risks specific to Phase 3

| # | Risk | Mitigation |
|---|---|---|
| 1 | Debounce vs cancel: race where a slower previous request lands after a newer one. | `switchMap` cancels older. Document in code. |
| 2 | Recent searches grow unbounded over time. | Cap at 10; LRU eviction. |
| 3 | Camera permission rejection silently breaks the lookup screen. | Explicit permission-denied UI state. |
| 4 | Arabic normalization regressions on the server affect results. | Include at least one Arabic editorial query in integration tests; flag failures loudly. |

## Definition of Done

See `checklists/dod.md`.
