import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:stream_transform/stream_transform.dart';

import '../data/catalog_repository.dart';
import '../data/catalog_view_models.dart';

@immutable
class ListingFilter {
  const ListingFilter({
    this.query = '',
    this.categoryId,
    this.sortKey,
    this.selectedFacets = const {},
  });

  final String query;
  final String? categoryId;
  final String? sortKey;
  final Map<String, Set<String>> selectedFacets;

  ListingFilter copyWith({
    String? query,
    Object? categoryId = _sentinel,
    Object? sortKey = _sentinel,
    Map<String, Set<String>>? selectedFacets,
  }) {
    return ListingFilter(
      query: query ?? this.query,
      categoryId: identical(categoryId, _sentinel)
          ? this.categoryId
          : categoryId as String?,
      sortKey:
          identical(sortKey, _sentinel) ? this.sortKey : sortKey as String?,
      selectedFacets: selectedFacets ?? this.selectedFacets,
    );
  }
}

const _sentinel = Object();

@immutable
sealed class ListingState {
  const ListingState({required this.filter});
  final ListingFilter filter;
}

class ListingIdle extends ListingState {
  const ListingIdle({required super.filter});
}

class ListingLoading extends ListingState {
  const ListingLoading({required super.filter});
}

class ListingLoaded extends ListingState {
  const ListingLoaded({
    required super.filter,
    required this.items,
    required this.facets,
    required this.nextCursor,
    this.isLoadingMore = false,
  });
  final List<ProductListingItem> items;
  final List<Facet> facets;
  final String? nextCursor;
  final bool isLoadingMore;

  bool get hasMore => nextCursor != null;
}

class ListingEmpty extends ListingState {
  const ListingEmpty({required super.filter});
}

class ListingError extends ListingState {
  const ListingError({required super.filter, required this.reason});
  final String reason;
}

@immutable
sealed class ListingEvent {
  const ListingEvent();
}

class QueryChanged extends ListingEvent {
  const QueryChanged(this.query);
  final String query;
}

class FacetToggled extends ListingEvent {
  const FacetToggled({required this.kind, required this.value});
  final String kind;
  final String value;
}

class SortChanged extends ListingEvent {
  const SortChanged(this.sortKey);
  final String sortKey;
}

class CategorySet extends ListingEvent {
  const CategorySet(this.categoryId);
  final String? categoryId;
}

class PageRequested extends ListingEvent {
  const PageRequested();
}

class _Refreshed extends ListingEvent {
  const _Refreshed();
}

class ListingBloc extends Bloc<ListingEvent, ListingState> {
  ListingBloc({required CatalogRepository repository})
      : _repository = repository,
        super(const ListingIdle(filter: ListingFilter())) {
    on<QueryChanged>(_onQueryChanged,
        transformer: (events, mapper) => events
            .debounce(const Duration(milliseconds: 250))
            .switchMap(mapper));
    on<FacetToggled>(_onFacetToggled);
    on<SortChanged>(_onSortChanged);
    on<CategorySet>(_onCategorySet);
    on<PageRequested>(_onPageRequested);
    on<_Refreshed>(_onRefreshed);
  }

  final CatalogRepository _repository;

  Future<void> _onQueryChanged(
    QueryChanged event,
    Emitter<ListingState> emit,
  ) async {
    final filter = state.filter.copyWith(query: event.query);
    await _refresh(filter, emit);
  }

  Future<void> _onFacetToggled(
    FacetToggled event,
    Emitter<ListingState> emit,
  ) async {
    final next = Map<String, Set<String>>.from(state.filter.selectedFacets);
    final bucket = Set<String>.from(next[event.kind] ?? const <String>{});
    if (!bucket.add(event.value)) bucket.remove(event.value);
    if (bucket.isEmpty) {
      next.remove(event.kind);
    } else {
      next[event.kind] = bucket;
    }
    await _refresh(state.filter.copyWith(selectedFacets: next), emit);
  }

  Future<void> _onSortChanged(
    SortChanged event,
    Emitter<ListingState> emit,
  ) async {
    await _refresh(state.filter.copyWith(sortKey: event.sortKey), emit);
  }

  Future<void> _onCategorySet(
    CategorySet event,
    Emitter<ListingState> emit,
  ) async {
    await _refresh(state.filter.copyWith(categoryId: event.categoryId), emit);
  }

  Future<void> _onRefreshed(
      _Refreshed event, Emitter<ListingState> emit) async {
    await _refresh(state.filter, emit);
  }

  Future<void> _refresh(
      ListingFilter filter, Emitter<ListingState> emit) async {
    emit(ListingLoading(filter: filter));
    try {
      final page = await _repository.fetchListing(
        query: filter.query.isEmpty ? null : filter.query,
        categoryId: filter.categoryId,
        sortKey: filter.sortKey,
        selectedFacets: filter.selectedFacets,
      );
      if (page.items.isEmpty) {
        emit(ListingEmpty(filter: filter));
      } else {
        emit(ListingLoaded(
          filter: filter,
          items: page.items,
          facets: page.facets,
          nextCursor: page.nextCursor,
        ));
      }
    } on Object catch (e) {
      emit(ListingError(filter: filter, reason: e.toString()));
    }
  }

  Future<void> _onPageRequested(
    PageRequested event,
    Emitter<ListingState> emit,
  ) async {
    final s = state;
    if (s is! ListingLoaded || !s.hasMore || s.isLoadingMore) return;
    emit(ListingLoaded(
      filter: s.filter,
      items: s.items,
      facets: s.facets,
      nextCursor: s.nextCursor,
      isLoadingMore: true,
    ));
    try {
      final page = await _repository.fetchListing(
        query: s.filter.query.isEmpty ? null : s.filter.query,
        categoryId: s.filter.categoryId,
        sortKey: s.filter.sortKey,
        selectedFacets: s.filter.selectedFacets,
        cursor: s.nextCursor,
      );
      emit(ListingLoaded(
        filter: s.filter,
        items: [...s.items, ...page.items],
        facets: page.facets.isNotEmpty ? page.facets : s.facets,
        nextCursor: page.nextCursor,
      ));
    } on Object catch (e) {
      emit(ListingError(filter: s.filter, reason: e.toString()));
    }
  }
}
