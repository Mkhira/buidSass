import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/error/failure.dart';
import '../../inventory/data/inventory_gateway.dart';
import '../../inventory/data/models/inventory_models.dart';
import '../../reviews/data/models/reviews_aggregate_models.dart';
import '../../reviews/data/reviews_aggregates_gateway.dart';
import '../data/catalog_gateway.dart';
import '../data/models/catalog_models.dart';

/// Phase 2 Home orchestrator. Drives the four sub-calls listed in
/// `specs/mobile/phase-2-catalog/spec.md#s-2-1`:
///
///   1. categories  — `CatalogGateway.listCategories`
///   2. brands      — `CatalogGateway.listBrands`
///   3. featured    — first page of products under the first category
///                    (`CatalogGateway.listCategoryProducts`)
///   4. enrichment  — availability + rating aggregates for the featured
///                    set (`InventoryGateway` + `ReviewsAggregatesGateway`)
///
/// State is emitted progressively (plan.md "Strategy: emit HomeLoaded
/// progressively"). Categories + brands arrive first; featured cards
/// render as soon as their products land and refresh in place as
/// availability + aggregates resolve. Failures on the enrichment sub-calls
/// are silenced (Phase 2 risk #4 — aggregates may be rate-limited; skip
/// silently, do not block Home).
@immutable
class CatalogHomeState {
  const CatalogHomeState({
    this.loading = true,
    this.categories = const [],
    this.brands = const [],
    this.featured = const [],
    this.availability = const {},
    this.aggregates = const {},
    this.failure,
  });

  final bool loading;
  final List<CatalogCategory> categories;
  final List<CatalogBrand> brands;
  final List<CatalogProduct> featured;

  /// Keyed by productId so cards can look up their slot without scanning
  /// the featured list.
  final Map<String, InventoryAvailability> availability;
  final Map<String, ReviewsAggregate> aggregates;

  /// Set when the categories/brands sub-calls fail — Home renders an
  /// error banner. Enrichment failures never populate this field.
  final Failure? failure;

  CatalogHomeState copyWith({
    bool? loading,
    List<CatalogCategory>? categories,
    List<CatalogBrand>? brands,
    List<CatalogProduct>? featured,
    Map<String, InventoryAvailability>? availability,
    Map<String, ReviewsAggregate>? aggregates,
    Failure? failure,
    bool clearFailure = false,
  }) {
    return CatalogHomeState(
      loading: loading ?? this.loading,
      categories: categories ?? this.categories,
      brands: brands ?? this.brands,
      featured: featured ?? this.featured,
      availability: availability ?? this.availability,
      aggregates: aggregates ?? this.aggregates,
      failure: clearFailure ? null : (failure ?? this.failure),
    );
  }

  bool get isEmpty =>
      !loading && categories.isEmpty && brands.isEmpty && featured.isEmpty;
}

@immutable
sealed class CatalogHomeEvent {
  const CatalogHomeEvent();
}

class CatalogHomeRequested extends CatalogHomeEvent {
  const CatalogHomeRequested({this.market});

  /// Optional override for tests + market switcher. When null the bloc
  /// reuses the last requested market (defaulting to `ksa` on first run).
  final String? market;
}

class CatalogHomeRefreshRequested extends CatalogHomeEvent {
  const CatalogHomeRefreshRequested();
}

class CatalogHomeBloc extends Bloc<CatalogHomeEvent, CatalogHomeState> {
  CatalogHomeBloc({
    required CatalogGateway catalog,
    required InventoryGateway inventory,
    required ReviewsAggregatesGateway reviews,
    String defaultMarket = 'ksa',
  })  : _catalog = catalog,
        _inventory = inventory,
        _reviews = reviews,
        _market = defaultMarket,
        super(const CatalogHomeState()) {
    on<CatalogHomeRequested>(_onLoad);
    on<CatalogHomeRefreshRequested>((_, emit) => _onLoad(
          const CatalogHomeRequested(),
          emit,
        ));
  }

  final CatalogGateway _catalog;
  final InventoryGateway _inventory;
  final ReviewsAggregatesGateway _reviews;
  String _market;

  Future<void> _onLoad(
    CatalogHomeRequested event,
    Emitter<CatalogHomeState> emit,
  ) async {
    if (event.market != null) _market = event.market!;
    // Snapshot the market at the start of this logical flow so a second
    // in-flight `CatalogHomeRequested` (with a different market) cannot
    // mutate it mid-await. Every gateway call below uses [market], never
    // the field. This satisfies the bloc's strict unidirectional event-
    // to-state mapping.
    final market = _market;
    emit(state.copyWith(loading: true, clearFailure: true));

    // Step 1 + 2 in parallel — both block the screen until they return.
    List<CatalogCategory> categories;
    List<CatalogBrand> brands;
    try {
      final results = await Future.wait<Object>([
        _catalog.listCategories(market: market),
        _catalog.listBrands(market: market),
      ]);
      categories = results[0] as List<CatalogCategory>;
      brands = results[1] as List<CatalogBrand>;
    } on Failure catch (f) {
      emit(state.copyWith(loading: false, failure: f));
      return;
    }
    emit(state.copyWith(
      loading: false,
      categories: categories,
      brands: brands,
      featured: const [],
      availability: const {},
      aggregates: const {},
    ));

    if (categories.isEmpty) return;

    // Step 3 — featured strip pulls the first category's first page.
    List<CatalogProduct> featured;
    try {
      final page = await _catalog.listCategoryProducts(
        slug: categories.first.slug,
        market: market,
        pageSize: 8,
      );
      featured = page.items;
    } on Failure {
      // Featured strip is not load-bearing for Home; skip silently
      // (Phase 2 risk #4).
      featured = const [];
    }
    if (featured.isEmpty) return;
    emit(state.copyWith(featured: featured));

    // Step 4 — enrichment. Run in parallel; failures are swallowed.
    final productIds = featured.map((p) => p.id).toList(growable: false);
    final results = await Future.wait<Object?>([
      _safeAvailability(productIds, market),
      _safeAggregates(productIds, market),
    ]);
    final availability =
        (results[0] as Map<String, InventoryAvailability>?) ?? const {};
    final aggregates =
        (results[1] as Map<String, ReviewsAggregate>?) ?? const {};
    if (emit.isDone) return;
    emit(state.copyWith(
      availability: availability,
      aggregates: aggregates,
    ));
  }

  Future<Map<String, InventoryAvailability>?> _safeAvailability(
    List<String> productIds,
    String market,
  ) async {
    try {
      final list = await _inventory.getAvailability(
          productIds: productIds, market: market);
      return {for (final av in list) av.productId: av};
    } on Failure {
      return null;
    }
  }

  Future<Map<String, ReviewsAggregate>?> _safeAggregates(
    List<String> productIds,
    String market,
  ) async {
    try {
      final list = await _reviews.getAggregatesBatch(
        productIds: productIds,
        marketCode: market.toUpperCase(),
      );
      return {for (final a in list) a.productId: a};
    } on Failure {
      return null;
    }
  }
}
