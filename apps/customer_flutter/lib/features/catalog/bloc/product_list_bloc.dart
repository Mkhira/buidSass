import 'package:bloc_concurrency/bloc_concurrency.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/error/failure.dart';
import '../../inventory/data/inventory_gateway.dart';
import '../../inventory/data/models/inventory_models.dart';
import '../../reviews/data/models/reviews_aggregate_models.dart';
import '../../reviews/data/reviews_aggregates_gateway.dart';
import '../data/catalog_gateway.dart';
import '../data/models/catalog_models.dart';

/// Value object passed to [ProductListBloc] that fully describes a list
/// query. Shared between category detail (S-2.3) and brand product list
/// (S-2.5) — only the entry filter differs.
@immutable
class ProductListQuery {
  const ProductListQuery({
    required this.categorySlug,
    required this.market,
    this.brandSlug,
    this.sort = CatalogSort.relevance,
    this.priceMin,
    this.priceMax,
    this.restricted,
    this.pageSize = 20,
  });

  /// Category to read products from. Brand-entry callers use the seed
  /// category supplied by the server (or `'all'`) + the [brandSlug]
  /// filter.
  final String categorySlug;
  final String market;
  final String? brandSlug;
  final CatalogSort sort;
  final int? priceMin;
  final int? priceMax;
  final CatalogRestrictedFilter? restricted;
  final int pageSize;

  ProductListQuery copyWith({
    String? categorySlug,
    String? market,
    String? brandSlug,
    CatalogSort? sort,
    int? priceMin,
    int? priceMax,
    CatalogRestrictedFilter? restricted,
    int? pageSize,
    bool clearBrand = false,
  }) {
    return ProductListQuery(
      categorySlug: categorySlug ?? this.categorySlug,
      market: market ?? this.market,
      brandSlug: clearBrand ? null : (brandSlug ?? this.brandSlug),
      sort: sort ?? this.sort,
      priceMin: priceMin ?? this.priceMin,
      priceMax: priceMax ?? this.priceMax,
      restricted: restricted ?? this.restricted,
      pageSize: pageSize ?? this.pageSize,
    );
  }
}

@immutable
class ProductListState {
  const ProductListState({
    required this.query,
    this.items = const [],
    this.availability = const {},
    this.aggregates = const {},
    this.page = 1,
    this.hasMore = false,
    this.loadingInitial = true,
    this.loadingMore = false,
    this.failure,
  });

  final ProductListQuery query;
  final List<CatalogProduct> items;
  final Map<String, InventoryAvailability> availability;
  final Map<String, ReviewsAggregate> aggregates;
  final int page;
  final bool hasMore;
  final bool loadingInitial;
  final bool loadingMore;
  final Failure? failure;

  bool get isEmpty => !loadingInitial && items.isEmpty && failure == null;

  ProductListState copyWith({
    ProductListQuery? query,
    List<CatalogProduct>? items,
    Map<String, InventoryAvailability>? availability,
    Map<String, ReviewsAggregate>? aggregates,
    int? page,
    bool? hasMore,
    bool? loadingInitial,
    bool? loadingMore,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return ProductListState(
      query: query ?? this.query,
      items: items ?? this.items,
      availability: availability ?? this.availability,
      aggregates: aggregates ?? this.aggregates,
      page: page ?? this.page,
      hasMore: hasMore ?? this.hasMore,
      loadingInitial: loadingInitial ?? this.loadingInitial,
      loadingMore: loadingMore ?? this.loadingMore,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }
}

@immutable
sealed class ProductListEvent {
  const ProductListEvent();
}

class ProductListStarted extends ProductListEvent {
  const ProductListStarted(this.query);
  final ProductListQuery query;
}

class ProductListRefreshed extends ProductListEvent {
  const ProductListRefreshed();
}

class ProductListLoadMore extends ProductListEvent {
  const ProductListLoadMore();
}

class ProductListSortChanged extends ProductListEvent {
  const ProductListSortChanged(this.sort);
  final CatalogSort sort;
}

class ProductListBrandChanged extends ProductListEvent {
  const ProductListBrandChanged(this.brandSlug);

  /// Pass null to clear the brand filter.
  final String? brandSlug;
}

class ProductListBloc extends Bloc<ProductListEvent, ProductListState> {
  ProductListBloc({
    required CatalogGateway catalog,
    required InventoryGateway inventory,
    required ReviewsAggregatesGateway reviews,
    required ProductListQuery initialQuery,
  })  : _catalog = catalog,
        _inventory = inventory,
        _reviews = reviews,
        super(ProductListState(query: initialQuery)) {
    // Started / Refreshed / SortChanged / BrandChanged all reset the
    // list to page 1 — use `restartable()` so a quick succession of
    // sort+brand taps cancels the in-flight fetch before kicking off
    // the next one. This prevents enrichment from a stale query
    // landing on top of the latest items.
    on<ProductListStarted>(_onStarted, transformer: restartable());
    on<ProductListRefreshed>(_onRefreshed, transformer: restartable());
    on<ProductListSortChanged>(
      (e, emit) => _replay(state.query.copyWith(sort: e.sort), emit),
      transformer: restartable(),
    );
    on<ProductListBrandChanged>(
      (e, emit) => _replay(
        state.query.copyWith(
          brandSlug: e.brandSlug,
          clearBrand: e.brandSlug == null,
        ),
        emit,
      ),
      transformer: restartable(),
    );
    // Pagination must serialize — drop duplicate LoadMore events while
    // one is in-flight so we don't double-fetch or stagger pages out of
    // order.
    on<ProductListLoadMore>(_onLoadMore, transformer: droppable());
  }

  final CatalogGateway _catalog;
  final InventoryGateway _inventory;
  final ReviewsAggregatesGateway _reviews;

  Future<void> _onStarted(
    ProductListStarted event,
    Emitter<ProductListState> emit,
  ) =>
      _replay(event.query, emit);

  Future<void> _onRefreshed(
    ProductListRefreshed event,
    Emitter<ProductListState> emit,
  ) =>
      _replay(state.query, emit);

  Future<void> _replay(
    ProductListQuery query,
    Emitter<ProductListState> emit,
  ) async {
    emit(state.copyWith(
      query: query,
      items: const [],
      availability: const {},
      aggregates: const {},
      page: 1,
      hasMore: false,
      loadingInitial: true,
      loadingMore: false,
      clearFailure: true,
    ));
    try {
      final page = await _catalog.listCategoryProducts(
        slug: query.categorySlug,
        market: query.market,
        page: 1,
        pageSize: query.pageSize,
        sort: query.sort,
        brand: query.brandSlug,
        priceMin: query.priceMin,
        priceMax: query.priceMax,
        restricted: query.restricted,
      );
      if (emit.isDone) return;
      emit(state.copyWith(
        items: page.items,
        page: page.page,
        hasMore: page.hasMore,
        loadingInitial: false,
      ));
      await _enrich(page.items, emit);
    } on Failure catch (f) {
      if (emit.isDone) return;
      emit(state.copyWith(loadingInitial: false, failure: f));
    }
  }

  Future<void> _onLoadMore(
    ProductListLoadMore event,
    Emitter<ProductListState> emit,
  ) async {
    if (!state.hasMore || state.loadingMore || state.loadingInitial) return;
    emit(state.copyWith(loadingMore: true));
    try {
      final nextPage = state.page + 1;
      final page = await _catalog.listCategoryProducts(
        slug: state.query.categorySlug,
        market: state.query.market,
        page: nextPage,
        pageSize: state.query.pageSize,
        sort: state.query.sort,
        brand: state.query.brandSlug,
        priceMin: state.query.priceMin,
        priceMax: state.query.priceMax,
        restricted: state.query.restricted,
      );
      if (emit.isDone) return;
      emit(state.copyWith(
        items: [...state.items, ...page.items],
        page: page.page,
        hasMore: page.hasMore,
        loadingMore: false,
      ));
      await _enrich(page.items, emit);
    } on Failure catch (f) {
      if (emit.isDone) return;
      emit(state.copyWith(loadingMore: false, failure: f));
    }
  }

  Future<void> _enrich(
    List<CatalogProduct> items,
    Emitter<ProductListState> emit,
  ) async {
    if (items.isEmpty) return;
    final ids = items.map((p) => p.id).toList(growable: false);
    final results = await Future.wait<Object?>([
      _safe(() => _inventory.getAvailability(
            productIds: ids,
            market: state.query.market,
          )),
      _safe(() => _reviews.getAggregatesBatch(
            productIds: ids,
            marketCode: state.query.market.toUpperCase(),
          )),
    ]);
    if (emit.isDone) return;
    final mergedAvail =
        Map<String, InventoryAvailability>.from(state.availability);
    if (results[0] is List<InventoryAvailability>) {
      for (final av in results[0] as List<InventoryAvailability>) {
        mergedAvail[av.productId] = av;
      }
    }
    final mergedAgg = Map<String, ReviewsAggregate>.from(state.aggregates);
    if (results[1] is List<ReviewsAggregate>) {
      for (final a in results[1] as List<ReviewsAggregate>) {
        mergedAgg[a.productId] = a;
      }
    }
    emit(state.copyWith(availability: mergedAvail, aggregates: mergedAgg));
  }

  Future<Object?> _safe(Future<Object?> Function() loader) async {
    try {
      return await loader();
    } on Failure {
      return null;
    }
  }
}
