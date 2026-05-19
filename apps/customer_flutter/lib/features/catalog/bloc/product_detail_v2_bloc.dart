import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/error/failure.dart';
import '../../inventory/data/inventory_gateway.dart';
import '../../inventory/data/models/inventory_models.dart';
import '../../pricing/data/models/pricing_models.dart';
import '../../pricing/data/pricing_gateway.dart';
import '../../reviews/data/models/reviews_aggregate_models.dart';
import '../../reviews/data/reviews_aggregates_gateway.dart';
import '../data/catalog_gateway.dart';
import '../data/models/catalog_models.dart';

/// SM for PDP per Phase 2 plan.md §"ProductDetailBloc orchestrates four
/// sub-calls":
///
///   1. `product/{slug}` (blocks the screen).
///   2. In parallel after step 1: pricing preview + availability + aggregates.
///   3. Render PDP with placeholders for the still-loading sub-blocks.
///
/// The state model carries each sub-block independently so the screen
/// can render the product shell immediately and fade in stock / price /
/// rating as they resolve. `*Error` fields per sub-block let the UI
/// degrade gracefully — a failed aggregate call should not blank the
/// price.
///
/// Named `*V2` to coexist with the legacy `ProductDetailBloc` (in
/// `product_detail_bloc.dart`) that runs against the old
/// `CatalogRepository`. Block C/D will switch routes to this version.
@immutable
class ProductDetailV2State {
  const ProductDetailV2State({
    required this.slug,
    required this.market,
    this.product,
    this.priceQuote,
    this.availability,
    this.aggregate,
    this.productLoading = true,
    this.productFailure,
    this.priceLoading = false,
    this.priceFailure,
    this.availabilityLoading = false,
    this.aggregateLoading = false,
    this.priceDrift = false,
  });

  final String slug;
  final String market;

  final CatalogProductDetail? product;
  final PriceQuote? priceQuote;
  final InventoryAvailability? availability;
  final ReviewsAggregate? aggregate;

  final bool productLoading;
  final Failure? productFailure;

  final bool priceLoading;

  /// Pricing engine failure is non-fatal — UI falls back to
  /// [product.priceHint] when [priceQuote] is null and [priceFailure] is set.
  final Failure? priceFailure;

  final bool availabilityLoading;
  final bool aggregateLoading;

  /// BR-10 — set when the engine result differs from the catalog price
  /// hint. UI shows a subtle "Updated just now" badge.
  final bool priceDrift;

  ProductDetailV2State copyWith({
    CatalogProductDetail? product,
    PriceQuote? priceQuote,
    InventoryAvailability? availability,
    ReviewsAggregate? aggregate,
    bool? productLoading,
    Failure? productFailure,
    bool? priceLoading,
    Failure? priceFailure,
    bool? availabilityLoading,
    bool? aggregateLoading,
    bool? priceDrift,
    bool clearProductFailure = false,
    bool clearPriceFailure = false,
  }) {
    return ProductDetailV2State(
      slug: slug,
      market: market,
      product: product ?? this.product,
      priceQuote: priceQuote ?? this.priceQuote,
      availability: availability ?? this.availability,
      aggregate: aggregate ?? this.aggregate,
      productLoading: productLoading ?? this.productLoading,
      productFailure:
          clearProductFailure ? null : (productFailure ?? this.productFailure),
      priceLoading: priceLoading ?? this.priceLoading,
      priceFailure:
          clearPriceFailure ? null : (priceFailure ?? this.priceFailure),
      availabilityLoading: availabilityLoading ?? this.availabilityLoading,
      aggregateLoading: aggregateLoading ?? this.aggregateLoading,
      priceDrift: priceDrift ?? this.priceDrift,
    );
  }

  /// The price to render. Prefers the engine result; falls back to the
  /// catalog-supplied hint when the engine hasn't returned yet or
  /// failed.
  CatalogMoney? get displayPrice {
    final fromEngine = priceQuote?.lines.isNotEmpty == true
        ? priceQuote!.lines.first.lineTotal
        : null;
    return fromEngine ?? product?.priceHint;
  }
}

@immutable
sealed class ProductDetailV2Event {
  const ProductDetailV2Event();
}

class ProductDetailV2Requested extends ProductDetailV2Event {
  const ProductDetailV2Requested();
}

class ProductDetailV2PriceRequested extends ProductDetailV2Event {
  const ProductDetailV2PriceRequested({this.qty = 1});
  final int qty;
}

class ProductDetailV2Bloc
    extends Bloc<ProductDetailV2Event, ProductDetailV2State> {
  ProductDetailV2Bloc({
    required CatalogGateway catalog,
    required PricingGateway pricing,
    required InventoryGateway inventory,
    required ReviewsAggregatesGateway reviews,
    required String slug,
    required String market,
    PricingBuyerKind buyerKind = PricingBuyerKind.consumer,
  })  : _catalog = catalog,
        _pricing = pricing,
        _inventory = inventory,
        _reviews = reviews,
        _buyerKind = buyerKind,
        super(ProductDetailV2State(slug: slug, market: market)) {
    on<ProductDetailV2Requested>(_onLoad);
    on<ProductDetailV2PriceRequested>(_onPrice);
  }

  final CatalogGateway _catalog;
  final PricingGateway _pricing;
  final InventoryGateway _inventory;
  final ReviewsAggregatesGateway _reviews;
  final PricingBuyerKind _buyerKind;

  Future<void> _onLoad(
    ProductDetailV2Requested event,
    Emitter<ProductDetailV2State> emit,
  ) async {
    emit(state.copyWith(
      productLoading: true,
      clearProductFailure: true,
    ));
    CatalogProductDetail product;
    try {
      product = await _catalog.getProductBySlug(
        slug: state.slug,
        market: state.market,
      );
    } on Failure catch (f) {
      emit(state.copyWith(productLoading: false, productFailure: f));
      return;
    }
    if (emit.isDone) return;
    emit(state.copyWith(
      product: product,
      productLoading: false,
      priceLoading: true,
      availabilityLoading: true,
      aggregateLoading: true,
    ));

    // Fan out the three sub-calls — none blocks the screen.
    final results = await Future.wait<Object?>([
      _safePrice(product, qty: 1),
      _safeAvailability(product.id),
      _safeAggregate(product.id),
    ]);
    if (emit.isDone) return;
    final priceResult = results[0];
    final availability = results[1] as InventoryAvailability?;
    final aggregate = results[2] as ReviewsAggregate?;

    var nextState = state.copyWith(
      availability: availability,
      aggregate: aggregate,
      availabilityLoading: false,
      aggregateLoading: false,
      priceLoading: false,
    );
    if (priceResult is PriceQuote) {
      nextState = nextState.copyWith(
        priceQuote: priceResult,
        priceDrift: _detectDrift(product, priceResult),
        clearPriceFailure: true,
      );
    } else if (priceResult is Failure) {
      nextState = nextState.copyWith(priceFailure: priceResult);
    }
    emit(nextState);
  }

  Future<void> _onPrice(
    ProductDetailV2PriceRequested event,
    Emitter<ProductDetailV2State> emit,
  ) async {
    final product = state.product;
    if (product == null) return;
    emit(state.copyWith(priceLoading: true, clearPriceFailure: true));
    final result = await _safePrice(product, qty: event.qty);
    if (emit.isDone) return;
    if (result is PriceQuote) {
      emit(state.copyWith(
        priceQuote: result,
        priceLoading: false,
        priceDrift: _detectDrift(product, result),
      ));
    } else if (result is Failure) {
      emit(state.copyWith(priceLoading: false, priceFailure: result));
    }
  }

  Future<Object?> _safePrice(CatalogProductDetail product,
      {required int qty}) async {
    try {
      return await _pricing.preview(PricingRequest(
        lines: [PricingLineRequest(productId: product.id, qty: qty)],
        marketCode: state.market.toUpperCase(),
        buyerKind: _buyerKind,
      ));
    } on Failure catch (f) {
      return f;
    }
  }

  Future<InventoryAvailability?> _safeAvailability(String productId) async {
    try {
      final list = await _inventory.getAvailability(
        productIds: [productId],
        market: state.market,
      );
      return list.isEmpty ? null : list.first;
    } on Failure {
      return null;
    }
  }

  Future<ReviewsAggregate?> _safeAggregate(String productId) async {
    try {
      return await _reviews.getAggregate(
        productId: productId,
        marketCode: state.market.toUpperCase(),
      );
    } on Failure {
      return null;
    }
  }

  bool _detectDrift(CatalogProductDetail product, PriceQuote quote) {
    if (quote.lines.isEmpty) return false;
    return quote.lines.first.unitPrice.amountMinor !=
        product.priceHint.amountMinor;
  }
}
