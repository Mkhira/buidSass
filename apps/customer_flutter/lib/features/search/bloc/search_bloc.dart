import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:stream_transform/stream_transform.dart';

import '../../../core/error/failure.dart';
import '../data/models/search_models.dart';
import '../data/recent_searches_store.dart';
import '../data/search_gateway.dart';

// ===== State =====

@immutable
sealed class SearchState {
  const SearchState();
}

/// S-3.1 entry — focus the input, render recent + popular sections.
@immutable
class SearchIdle extends SearchState {
  const SearchIdle({this.recent = const [], this.popular = const []});
  final List<String> recent;
  final List<SearchSuggestion> popular;
}

/// S-3.2 inflight — typing within debounce, spinner inline.
@immutable
class SearchAutocompleting extends SearchState {
  const SearchAutocompleting(this.query);
  final String query;
}

/// S-3.2 ready — suggestion list + top-matches strip.
@immutable
class SearchAutocompleted extends SearchState {
  const SearchAutocompleted({
    required this.query,
    required this.suggestions,
    required this.topMatches,
  });
  final String query;
  final List<SearchSuggestion> suggestions;
  final List<SearchTopMatch> topMatches;
}

/// S-3.3 results — grid + facets + sort + pagination.
@immutable
class SearchResults extends SearchState {
  const SearchResults({
    required this.query,
    required this.items,
    required this.facets,
    required this.sortOptions,
    required this.selectedFacets,
    this.selectedSort,
    required this.page,
    required this.pageSize,
    required this.totalCount,
    this.isLoadingMore = false,
    this.suggestions = const [],
  });

  final String query;
  final List<SearchProductItem> items;
  final List<SearchFacet> facets;
  final List<SearchSortOption> sortOptions;
  final Map<String, Set<String>> selectedFacets;
  final String? selectedSort;
  final int page;
  final int pageSize;
  final int totalCount;
  final bool isLoadingMore;

  /// "Did you mean" — populated when results came back empty.
  final List<String> suggestions;

  bool get hasMore => page * pageSize < totalCount;

  SearchResults copyWith({
    List<SearchProductItem>? items,
    List<SearchFacet>? facets,
    List<SearchSortOption>? sortOptions,
    Map<String, Set<String>>? selectedFacets,
    String? selectedSort,
    int? page,
    int? pageSize,
    int? totalCount,
    bool? isLoadingMore,
    List<String>? suggestions,
  }) {
    return SearchResults(
      query: query,
      items: items ?? this.items,
      facets: facets ?? this.facets,
      sortOptions: sortOptions ?? this.sortOptions,
      selectedFacets: selectedFacets ?? this.selectedFacets,
      selectedSort: selectedSort ?? this.selectedSort,
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
      totalCount: totalCount ?? this.totalCount,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
      suggestions: suggestions ?? this.suggestions,
    );
  }
}

@immutable
class SearchEmpty extends SearchState {
  const SearchEmpty({required this.query, this.suggestions = const []});
  final String query;
  final List<String> suggestions;
}

@immutable
class SearchFailure extends SearchState {
  const SearchFailure({required this.reason, this.correlationId});
  final String reason;
  final String? correlationId;
}

// ===== Events =====

@immutable
sealed class SearchEvent {
  const SearchEvent();
}

/// Initial entry — load recent + popular suggestions from local store.
class SearchEntered extends SearchEvent {
  const SearchEntered();
}

/// Each keystroke. Debounced + switch-mapped inside the bloc.
class SearchQueryChanged extends SearchEvent {
  const SearchQueryChanged(this.query);
  final String query;
}

/// User pressed enter / tapped a suggestion / tapped a recent chip —
/// commits the query to the recent store and loads results.
class SearchSubmitted extends SearchEvent {
  const SearchSubmitted(this.query);
  final String query;
}

class SearchFacetToggled extends SearchEvent {
  const SearchFacetToggled({required this.kind, required this.value});
  final String kind;
  final String value;
}

class SearchSortChanged extends SearchEvent {
  const SearchSortChanged(this.sortKey);
  final String sortKey;
}

class SearchPageRequested extends SearchEvent {
  const SearchPageRequested();
}

class SearchRecentCleared extends SearchEvent {
  const SearchRecentCleared();
}

class SearchRecentTapped extends SearchEvent {
  const SearchRecentTapped(this.query);
  final String query;
}

// ===== Bloc =====

class SearchBloc extends Bloc<SearchEvent, SearchState> {
  SearchBloc({
    required SearchGateway gateway,
    required RecentSearchesStore recentStore,
    required String Function() marketProvider,
    required String Function() localeProvider,
    Duration debounce = const Duration(milliseconds: 250),
    int pageSize = 24,
  })  : _gateway = gateway,
        _recent = recentStore,
        _market = marketProvider,
        _locale = localeProvider,
        _pageSize = pageSize,
        super(const SearchIdle()) {
    on<SearchEntered>(_onEntered);
    on<SearchQueryChanged>(
      _onQueryChanged,
      transformer: (events, mapper) =>
          events.debounce(debounce).switchMap(mapper),
    );
    on<SearchSubmitted>(_onSubmitted);
    on<SearchFacetToggled>(_onFacetToggled);
    on<SearchSortChanged>(_onSortChanged);
    on<SearchPageRequested>(_onPageRequested);
    on<SearchRecentCleared>(_onRecentCleared);
    on<SearchRecentTapped>(_onRecentTapped);
  }

  final SearchGateway _gateway;
  final RecentSearchesStore _recent;
  final String Function() _market;
  final String Function() _locale;
  final int _pageSize;

  Future<void> _onEntered(
      SearchEntered event, Emitter<SearchState> emit) async {
    final recent = await _recent.load();
    emit(SearchIdle(recent: recent, popular: const []));
  }

  Future<void> _onQueryChanged(
      SearchQueryChanged event, Emitter<SearchState> emit) async {
    final q = event.query.trim();
    if (q.isEmpty) {
      final recent = await _recent.load();
      emit(SearchIdle(recent: recent));
      return;
    }
    emit(SearchAutocompleting(q));
    try {
      final result = await _gateway.autocomplete(AutocompleteRequest(
        query: q,
        marketCode: _market(),
        locale: _locale(),
      ));
      emit(SearchAutocompleted(
        query: q,
        suggestions: result.suggestions,
        topMatches: result.topMatches,
      ));
    } on Failure catch (f) {
      emit(SearchFailure(reason: f.code, correlationId: f.correlationId));
    } on Object catch (e) {
      emit(SearchFailure(reason: e.toString()));
    }
  }

  Future<void> _onSubmitted(
      SearchSubmitted event, Emitter<SearchState> emit) async {
    final q = event.query.trim();
    if (q.isEmpty) return;
    await _recent.push(q);
    await _runSearch(emit,
        query: q, selectedFacets: const {}, sort: null, page: 1);
  }

  Future<void> _onFacetToggled(
      SearchFacetToggled event, Emitter<SearchState> emit) async {
    final s = state;
    if (s is! SearchResults) return;
    final next = <String, Set<String>>{
      for (final entry in s.selectedFacets.entries)
        entry.key: Set<String>.from(entry.value),
    };
    final bucket = next[event.kind] ?? <String>{};
    if (!bucket.add(event.value)) bucket.remove(event.value);
    if (bucket.isEmpty) {
      next.remove(event.kind);
    } else {
      next[event.kind] = bucket;
    }
    await _runSearch(emit,
        query: s.query,
        selectedFacets: next,
        sort: s.selectedSort,
        page: 1);
  }

  Future<void> _onSortChanged(
      SearchSortChanged event, Emitter<SearchState> emit) async {
    final s = state;
    if (s is! SearchResults) return;
    await _runSearch(emit,
        query: s.query,
        selectedFacets: s.selectedFacets,
        sort: event.sortKey,
        page: 1);
  }

  Future<void> _onPageRequested(
      SearchPageRequested event, Emitter<SearchState> emit) async {
    final s = state;
    if (s is! SearchResults || !s.hasMore || s.isLoadingMore) return;
    emit(s.copyWith(isLoadingMore: true));
    try {
      final result = await _gateway.searchProducts(SearchProductsRequest(
        query: s.query,
        marketCode: _market(),
        locale: _locale(),
        page: s.page + 1,
        pageSize: _pageSize,
        sort: s.selectedSort,
        facets: _facetsToWire(s.selectedFacets),
      ));
      emit(s.copyWith(
        items: [...s.items, ...result.items],
        page: result.page,
        totalCount: result.totalCount,
        // Keep the original facets list — the server may return narrowed
        // option counts on later pages, but the UI panel should stay
        // anchored to the page-1 axes.
        facets: result.facets.isNotEmpty ? result.facets : s.facets,
        isLoadingMore: false,
      ));
    } on Failure catch (f) {
      emit(SearchFailure(reason: f.code, correlationId: f.correlationId));
    } on Object catch (e) {
      emit(SearchFailure(reason: e.toString()));
    }
  }

  Future<void> _onRecentCleared(
      SearchRecentCleared event, Emitter<SearchState> emit) async {
    await _recent.clear();
    final s = state;
    if (s is SearchIdle) {
      emit(SearchIdle(recent: const [], popular: s.popular));
    } else {
      emit(const SearchIdle());
    }
  }

  Future<void> _onRecentTapped(
      SearchRecentTapped event, Emitter<SearchState> emit) async {
    add(SearchSubmitted(event.query));
  }

  // ---- helpers ----

  Future<void> _runSearch(
    Emitter<SearchState> emit, {
    required String query,
    required Map<String, Set<String>> selectedFacets,
    required String? sort,
    required int page,
  }) async {
    try {
      final result = await _gateway.searchProducts(SearchProductsRequest(
        query: query,
        marketCode: _market(),
        locale: _locale(),
        page: page,
        pageSize: _pageSize,
        sort: sort,
        facets: _facetsToWire(selectedFacets),
      ));
      if (result.items.isEmpty) {
        emit(SearchEmpty(query: query, suggestions: result.suggestions));
        return;
      }
      emit(SearchResults(
        query: query,
        items: result.items,
        facets: result.facets,
        sortOptions: result.sortOptions,
        selectedFacets: selectedFacets,
        selectedSort: sort,
        page: result.page,
        pageSize: result.pageSize,
        totalCount: result.totalCount,
        suggestions: result.suggestions,
      ));
    } on Failure catch (f) {
      emit(SearchFailure(reason: f.code, correlationId: f.correlationId));
    } on Object catch (e) {
      emit(SearchFailure(reason: e.toString()));
    }
  }

  Map<String, Object?> _facetsToWire(Map<String, Set<String>> selected) {
    final out = <String, Object?>{};
    for (final entry in selected.entries) {
      out[entry.key] = entry.value.toList(growable: false);
    }
    return out;
  }
}
