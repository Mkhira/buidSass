import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/catalog/bloc/product_detail_v2_bloc.dart';
import 'package:customer_flutter/features/catalog/data/catalog_gateway.dart';
import 'package:customer_flutter/features/catalog/data/models/catalog_models.dart';
import 'package:customer_flutter/features/inventory/data/inventory_gateway.dart';
import 'package:customer_flutter/features/inventory/data/models/inventory_models.dart';
import 'package:customer_flutter/features/pricing/data/models/pricing_models.dart';
import 'package:customer_flutter/features/pricing/data/pricing_gateway.dart';
import 'package:customer_flutter/features/reviews/data/models/reviews_aggregate_models.dart';
import 'package:customer_flutter/features/reviews/data/reviews_aggregates_gateway.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockCatalog extends Mock implements CatalogGateway {}

class _MockPricing extends Mock implements PricingGateway {}

class _MockInventory extends Mock implements InventoryGateway {}

class _MockReviews extends Mock implements ReviewsAggregatesGateway {}

const _detail = CatalogProductDetail(
  id: 'p-1',
  slug: 'tile-a',
  sku: 'SKU-1',
  name: LocalizedText(en: 'Tile A'),
  description: LocalizedText(en: 'desc'),
  mediaUrls: [],
  attributes: [],
  priceHint: CatalogMoney(amountMinor: 12000, currency: 'SAR'),
  isRestricted: false,
);

void main() {
  late _MockCatalog catalog;
  late _MockPricing pricing;
  late _MockInventory inventory;
  late _MockReviews reviews;

  setUp(() {
    catalog = _MockCatalog();
    pricing = _MockPricing();
    inventory = _MockInventory();
    reviews = _MockReviews();
    registerFallbackValue(const PricingRequest(
      lines: [],
      marketCode: 'SA',
      buyerKind: PricingBuyerKind.consumer,
    ));
  });

  ProductDetailV2Bloc build() => ProductDetailV2Bloc(
        catalog: catalog,
        pricing: pricing,
        inventory: inventory,
        reviews: reviews,
        slug: 'tile-a',
        market: 'ksa',
      );

  blocTest<ProductDetailV2Bloc, ProductDetailV2State>(
    'product resolves first, then enrichment fans out',
    build: () {
      when(() => catalog.getProductBySlug(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
          )).thenAnswer((_) async => _detail);
      when(() => pricing.preview(any())).thenAnswer((_) async =>
          const PriceQuote(
            total: CatalogMoney(amountMinor: 12000, currency: 'SAR'),
            lines: [
              PricedLine(
                productId: 'p-1',
                qty: 1,
                unitPrice: CatalogMoney(amountMinor: 12000, currency: 'SAR'),
                discount: CatalogMoney(amountMinor: 0, currency: 'SAR'),
                lineTotal: CatalogMoney(amountMinor: 12000, currency: 'SAR'),
                tierLabel: 'consumer',
              ),
            ],
            appliedPromotions: [],
            explanationToken: 'tok',
          ));
      when(() => inventory.getAvailability(
            productIds: any(named: 'productIds'),
            market: any(named: 'market'),
          )).thenAnswer((_) async => const [
            InventoryAvailability(
                productId: 'p-1', inStock: true, lowStock: false),
          ]);
      when(() => reviews.getAggregate(
            productId: any(named: 'productId'),
            marketCode: any(named: 'marketCode'),
          )).thenAnswer((_) async => const ReviewsAggregate(
            productId: 'p-1',
            ratingAverage: 4.5,
            ratingCount: 30,
            starHistogram: [],
          ));
      return build();
    },
    act: (b) => b.add(const ProductDetailV2Requested()),
    verify: (b) {
      expect(b.state.product?.sku, 'SKU-1');
      expect(b.state.priceQuote?.lines.first.tierLabel, 'consumer');
      expect(b.state.availability?.inStock, isTrue);
      expect(b.state.aggregate?.ratingCount, 30);
      expect(b.state.priceLoading, isFalse);
      expect(b.state.availabilityLoading, isFalse);
      expect(b.state.aggregateLoading, isFalse);
    },
  );

  blocTest<ProductDetailV2Bloc, ProductDetailV2State>(
    'product failure halts the screen',
    build: () {
      when(() => catalog.getProductBySlug(
                slug: any(named: 'slug'),
                market: any(named: 'market'),
              ))
          .thenThrow(const NotFoundFailure(
              code: 'product.not_found', message: 'x', correlationId: 'c-1'));
      return build();
    },
    act: (b) => b.add(const ProductDetailV2Requested()),
    verify: (b) {
      expect(b.state.productFailure, isA<NotFoundFailure>());
      expect(b.state.product, isNull);
      verifyNever(() => pricing.preview(any()));
    },
  );

  blocTest<ProductDetailV2Bloc, ProductDetailV2State>(
    'pricing failure is non-fatal; displayPrice falls back to hint',
    build: () {
      when(() => catalog.getProductBySlug(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
          )).thenAnswer((_) async => _detail);
      when(() => pricing.preview(any())).thenThrow(const OfflineFailure(
          code: 'network.offline', message: 'x', correlationId: 'c-2'));
      when(() => inventory.getAvailability(
            productIds: any(named: 'productIds'),
            market: any(named: 'market'),
          )).thenAnswer((_) async => const []);
      when(() => reviews.getAggregate(
            productId: any(named: 'productId'),
            marketCode: any(named: 'marketCode'),
          )).thenAnswer((_) async => null);
      return build();
    },
    act: (b) => b.add(const ProductDetailV2Requested()),
    verify: (b) {
      expect(b.state.product, isNotNull);
      expect(b.state.priceQuote, isNull);
      expect(b.state.priceFailure, isA<OfflineFailure>());
      expect(b.state.displayPrice?.amountMinor, 12000);
      expect(b.state.priceDrift, isFalse);
    },
  );

  blocTest<ProductDetailV2Bloc, ProductDetailV2State>(
    'price drift detected when engine differs from hint',
    build: () {
      when(() => catalog.getProductBySlug(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
          )).thenAnswer((_) async => _detail);
      when(() => pricing.preview(any())).thenAnswer((_) async =>
          const PriceQuote(
            total: CatalogMoney(amountMinor: 11500, currency: 'SAR'),
            lines: [
              PricedLine(
                productId: 'p-1',
                qty: 1,
                unitPrice: CatalogMoney(amountMinor: 11500, currency: 'SAR'),
                discount: CatalogMoney(amountMinor: 500, currency: 'SAR'),
                lineTotal: CatalogMoney(amountMinor: 11500, currency: 'SAR'),
                tierLabel: 'business',
              ),
            ],
            appliedPromotions: [],
            explanationToken: 'tok',
          ));
      when(() => inventory.getAvailability(
            productIds: any(named: 'productIds'),
            market: any(named: 'market'),
          )).thenAnswer((_) async => const []);
      when(() => reviews.getAggregate(
            productId: any(named: 'productId'),
            marketCode: any(named: 'marketCode'),
          )).thenAnswer((_) async => null);
      return build();
    },
    act: (b) => b.add(const ProductDetailV2Requested()),
    verify: (b) {
      expect(b.state.priceDrift, isTrue);
      expect(b.state.displayPrice?.amountMinor, 11500);
    },
  );
}
