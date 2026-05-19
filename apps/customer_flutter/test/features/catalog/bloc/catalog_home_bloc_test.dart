import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/catalog/bloc/catalog_home_bloc.dart';
import 'package:customer_flutter/features/catalog/data/catalog_gateway.dart';
import 'package:customer_flutter/features/catalog/data/models/catalog_models.dart';
import 'package:customer_flutter/features/inventory/data/inventory_gateway.dart';
import 'package:customer_flutter/features/inventory/data/models/inventory_models.dart';
import 'package:customer_flutter/features/reviews/data/models/reviews_aggregate_models.dart';
import 'package:customer_flutter/features/reviews/data/reviews_aggregates_gateway.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockCatalog extends Mock implements CatalogGateway {}

class _MockInventory extends Mock implements InventoryGateway {}

class _MockReviews extends Mock implements ReviewsAggregatesGateway {}

const _cat1 = CatalogCategory(
  id: 'c-1',
  slug: 'bathroom',
  name: LocalizedText(en: 'Bathroom'),
);

const _brand1 = CatalogBrand(
  id: 'b-1',
  slug: 'brand-x',
  name: LocalizedText(en: 'Brand X'),
);

const _prod1 = CatalogProduct(
  id: 'p-1',
  slug: 'tile-a',
  name: LocalizedText(en: 'Tile A'),
  thumbnailUrl: '',
  priceHint: CatalogMoney(amountMinor: 12000, currency: 'SAR'),
  isRestricted: false,
);

void main() {
  late _MockCatalog catalog;
  late _MockInventory inventory;
  late _MockReviews reviews;

  setUp(() {
    catalog = _MockCatalog();
    inventory = _MockInventory();
    reviews = _MockReviews();
    registerFallbackValue(<String>[]);
    when(() => inventory.getAvailability(
            productIds: any(named: 'productIds'),
            market: any(named: 'market')))
        .thenAnswer((_) async => const []);
    when(() => reviews.getAggregatesBatch(
            productIds: any(named: 'productIds'),
            marketCode: any(named: 'marketCode')))
        .thenAnswer((_) async => const []);
  });

  CatalogHomeBloc build() => CatalogHomeBloc(
        catalog: catalog,
        inventory: inventory,
        reviews: reviews,
      );

  blocTest<CatalogHomeBloc, CatalogHomeState>(
    'progressively emits categories+brands, then featured, then enrichment',
    build: () {
      when(() => catalog.listCategories(market: any(named: 'market')))
          .thenAnswer((_) async => const [_cat1]);
      when(() => catalog.listBrands(market: any(named: 'market')))
          .thenAnswer((_) async => const [_brand1]);
      when(() => catalog.listCategoryProducts(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
            pageSize: any(named: 'pageSize'),
          )).thenAnswer((_) async => const CatalogProductPage(
                items: [_prod1],
                page: 1,
                pageSize: 8,
                totalItems: 1,
              ));
      when(() => inventory.getAvailability(
            productIds: any(named: 'productIds'),
            market: any(named: 'market'),
          )).thenAnswer((_) async => const [
                InventoryAvailability(
                    productId: 'p-1', inStock: true, lowStock: false),
              ]);
      when(() => reviews.getAggregatesBatch(
            productIds: any(named: 'productIds'),
            marketCode: any(named: 'marketCode'),
          )).thenAnswer((_) async => const [
                ReviewsAggregate(
                  productId: 'p-1',
                  ratingAverage: 4.5,
                  ratingCount: 10,
                  starHistogram: [],
                ),
              ]);
      return build();
    },
    act: (b) => b.add(const CatalogHomeRequested()),
    expect: () => [
      // loading=true, clearFailure
      predicate<CatalogHomeState>((s) => s.loading == true),
      // categories+brands landed; not loading; no featured yet
      predicate<CatalogHomeState>((s) =>
          !s.loading &&
          s.categories.length == 1 &&
          s.brands.length == 1 &&
          s.featured.isEmpty),
      // featured arrived
      predicate<CatalogHomeState>((s) => s.featured.length == 1),
      // enrichment merged in
      predicate<CatalogHomeState>((s) =>
          s.availability['p-1']?.inStock == true &&
          s.aggregates['p-1']?.ratingCount == 10),
    ],
  );

  blocTest<CatalogHomeBloc, CatalogHomeState>(
    'failure on categories/brands surfaces as state.failure',
    build: () {
      when(() => catalog.listCategories(market: any(named: 'market')))
          .thenThrow(const OfflineFailure(
              code: 'network.offline',
              message: 'x',
              correlationId: 'c-1'));
      when(() => catalog.listBrands(market: any(named: 'market')))
          .thenAnswer((_) async => const []);
      return build();
    },
    act: (b) => b.add(const CatalogHomeRequested()),
    expect: () => [
      predicate<CatalogHomeState>((s) => s.loading == true),
      predicate<CatalogHomeState>(
          (s) => !s.loading && s.failure is OfflineFailure),
    ],
  );

  blocTest<CatalogHomeBloc, CatalogHomeState>(
    'enrichment failures are silenced; categories/brands still surface',
    build: () {
      when(() => catalog.listCategories(market: any(named: 'market')))
          .thenAnswer((_) async => const [_cat1]);
      when(() => catalog.listBrands(market: any(named: 'market')))
          .thenAnswer((_) async => const [_brand1]);
      when(() => catalog.listCategoryProducts(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
            pageSize: any(named: 'pageSize'),
          )).thenAnswer((_) async => const CatalogProductPage(
                items: [_prod1],
                page: 1,
                pageSize: 8,
                totalItems: 1,
              ));
      when(() => inventory.getAvailability(
            productIds: any(named: 'productIds'),
            market: any(named: 'market'),
          )).thenThrow(const ValidationFailure(
              code: 'rate.limited',
              message: 'x',
              correlationId: 'c-2',
              retryAfterSeconds: 60));
      when(() => reviews.getAggregatesBatch(
            productIds: any(named: 'productIds'),
            marketCode: any(named: 'marketCode'),
          )).thenThrow(const ValidationFailure(
              code: 'rate.limited',
              message: 'x',
              correlationId: 'c-3'));
      return build();
    },
    act: (b) => b.add(const CatalogHomeRequested()),
    verify: (b) {
      // After everything settles, state has categories + brands + featured
      // but empty enrichment, and no top-level failure.
      expect(b.state.failure, isNull);
      expect(b.state.categories, hasLength(1));
      expect(b.state.featured, hasLength(1));
      expect(b.state.availability, isEmpty);
      expect(b.state.aggregates, isEmpty);
    },
  );

  blocTest<CatalogHomeBloc, CatalogHomeState>(
    'empty categories short-circuits before featured fetch',
    build: () {
      when(() => catalog.listCategories(market: any(named: 'market')))
          .thenAnswer((_) async => const []);
      when(() => catalog.listBrands(market: any(named: 'market')))
          .thenAnswer((_) async => const []);
      return build();
    },
    act: (b) => b.add(const CatalogHomeRequested()),
    verify: (b) {
      expect(b.state.isEmpty, isTrue);
      verifyNever(() => catalog.listCategoryProducts(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
            pageSize: any(named: 'pageSize'),
          ));
    },
  );

  blocTest<CatalogHomeBloc, CatalogHomeState>(
    'market override on Requested propagates to gateway calls',
    build: () {
      when(() => catalog.listCategories(market: 'eg'))
          .thenAnswer((_) async => const []);
      when(() => catalog.listBrands(market: 'eg'))
          .thenAnswer((_) async => const []);
      return build();
    },
    act: (b) => b.add(const CatalogHomeRequested(market: 'eg')),
    verify: (_) {
      verify(() => catalog.listCategories(market: 'eg')).called(1);
      verify(() => catalog.listBrands(market: 'eg')).called(1);
    },
  );
}
