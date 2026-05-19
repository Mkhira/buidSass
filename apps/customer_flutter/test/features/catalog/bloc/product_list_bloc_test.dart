import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/catalog/bloc/product_list_bloc.dart';
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

const _q = ProductListQuery(categorySlug: 'cat', market: 'ksa');

CatalogProduct _prod(String id) => CatalogProduct(
      id: id,
      slug: id,
      name: const LocalizedText(en: 'p'),
      thumbnailUrl: '',
      priceHint: const CatalogMoney(amountMinor: 100, currency: 'SAR'),
      isRestricted: false,
    );

CatalogProductPage _page(List<String> ids,
        {int page = 1, int totalItems = 0}) =>
    CatalogProductPage(
      items: ids.map(_prod).toList(growable: false),
      page: page,
      pageSize: ids.isEmpty ? 0 : ids.length,
      totalItems: totalItems == 0 ? ids.length : totalItems,
    );

void main() {
  late _MockCatalog catalog;
  late _MockInventory inventory;
  late _MockReviews reviews;

  setUp(() {
    catalog = _MockCatalog();
    inventory = _MockInventory();
    reviews = _MockReviews();
    when(() => inventory.getAvailability(
        productIds: any(named: 'productIds'),
        market: any(named: 'market'))).thenAnswer((_) async => const []);
    when(() => reviews.getAggregatesBatch(
            productIds: any(named: 'productIds'),
            marketCode: any(named: 'marketCode')))
        .thenAnswer((_) async => const []);
  });

  ProductListBloc build() => ProductListBloc(
        catalog: catalog,
        inventory: inventory,
        reviews: reviews,
        initialQuery: _q,
      );

  blocTest<ProductListBloc, ProductListState>(
    'Started loads first page',
    build: () {
      when(() => catalog.listCategoryProducts(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
            page: any(named: 'page'),
            pageSize: any(named: 'pageSize'),
            sort: any(named: 'sort'),
            brand: any(named: 'brand'),
            priceMin: any(named: 'priceMin'),
            priceMax: any(named: 'priceMax'),
            restricted: any(named: 'restricted'),
          )).thenAnswer((_) async => _page(['p-1', 'p-2']));
      return build();
    },
    act: (b) => b.add(const ProductListStarted(_q)),
    verify: (b) {
      expect(b.state.items.map((p) => p.id), ['p-1', 'p-2']);
      expect(b.state.loadingInitial, isFalse);
    },
  );

  blocTest<ProductListBloc, ProductListState>(
    'LoadMore appends next page',
    build: () {
      when(() => catalog.listCategoryProducts(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
            page: any(named: 'page'),
            pageSize: any(named: 'pageSize'),
            sort: any(named: 'sort'),
            brand: any(named: 'brand'),
            priceMin: any(named: 'priceMin'),
            priceMax: any(named: 'priceMax'),
            restricted: any(named: 'restricted'),
          )).thenAnswer((inv) async {
        final page = inv.namedArguments[#page] as int;
        if (page == 1) return _page(['p-1', 'p-2'], page: 1, totalItems: 4);
        return _page(['p-3', 'p-4'], page: 2, totalItems: 4);
      });
      return build();
    },
    act: (b) async {
      b.add(const ProductListStarted(_q));
      await Future<void>.delayed(Duration.zero);
      await Future<void>.delayed(Duration.zero);
      b.add(const ProductListLoadMore());
    },
    verify: (b) {
      expect(b.state.items.map((p) => p.id), ['p-1', 'p-2', 'p-3', 'p-4']);
      expect(b.state.page, 2);
      expect(b.state.hasMore, isFalse);
    },
  );

  blocTest<ProductListBloc, ProductListState>(
    'SortChanged refetches page 1',
    build: () {
      when(() => catalog.listCategoryProducts(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
            page: any(named: 'page'),
            pageSize: any(named: 'pageSize'),
            sort: any(named: 'sort'),
            brand: any(named: 'brand'),
            priceMin: any(named: 'priceMin'),
            priceMax: any(named: 'priceMax'),
            restricted: any(named: 'restricted'),
          )).thenAnswer((inv) async => _page(['p-1']));
      return build();
    },
    act: (b) async {
      b.add(const ProductListStarted(_q));
      await Future<void>.delayed(Duration.zero);
      b.add(const ProductListSortChanged(CatalogSort.priceDesc));
    },
    verify: (b) {
      expect(b.state.query.sort, CatalogSort.priceDesc);
      verify(() => catalog.listCategoryProducts(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
            page: any(named: 'page'),
            pageSize: any(named: 'pageSize'),
            sort: CatalogSort.priceDesc,
            brand: any(named: 'brand'),
            priceMin: any(named: 'priceMin'),
            priceMax: any(named: 'priceMax'),
            restricted: any(named: 'restricted'),
          )).called(1);
    },
  );

  blocTest<ProductListBloc, ProductListState>(
    'Brand filter clears with null',
    build: () {
      when(() => catalog.listCategoryProducts(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
            page: any(named: 'page'),
            pageSize: any(named: 'pageSize'),
            sort: any(named: 'sort'),
            brand: any(named: 'brand'),
            priceMin: any(named: 'priceMin'),
            priceMax: any(named: 'priceMax'),
            restricted: any(named: 'restricted'),
          )).thenAnswer((_) async => _page([]));
      return build();
    },
    act: (b) async {
      b.add(const ProductListStarted(_q));
      await Future<void>.delayed(Duration.zero);
      b.add(const ProductListBrandChanged('brand-x'));
      await Future<void>.delayed(Duration.zero);
      b.add(const ProductListBrandChanged(null));
    },
    verify: (b) {
      expect(b.state.query.brandSlug, isNull);
    },
  );

  blocTest<ProductListBloc, ProductListState>(
    'Error on initial load surfaces in state.failure',
    build: () {
      when(() => catalog.listCategoryProducts(
                slug: any(named: 'slug'),
                market: any(named: 'market'),
                page: any(named: 'page'),
                pageSize: any(named: 'pageSize'),
                sort: any(named: 'sort'),
                brand: any(named: 'brand'),
                priceMin: any(named: 'priceMin'),
                priceMax: any(named: 'priceMax'),
                restricted: any(named: 'restricted'),
              ))
          .thenThrow(const OfflineFailure(
              code: 'network.offline', message: 'x', correlationId: 'c-1'));
      return build();
    },
    act: (b) => b.add(const ProductListStarted(_q)),
    verify: (b) {
      expect(b.state.failure, isA<OfflineFailure>());
      expect(b.state.loadingInitial, isFalse);
    },
  );

  blocTest<ProductListBloc, ProductListState>(
    'Enrichment populates availability + aggregates',
    build: () {
      when(() => catalog.listCategoryProducts(
            slug: any(named: 'slug'),
            market: any(named: 'market'),
            page: any(named: 'page'),
            pageSize: any(named: 'pageSize'),
            sort: any(named: 'sort'),
            brand: any(named: 'brand'),
            priceMin: any(named: 'priceMin'),
            priceMax: any(named: 'priceMax'),
            restricted: any(named: 'restricted'),
          )).thenAnswer((_) async => _page(['p-1']));
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
              ratingAverage: 4.0,
              ratingCount: 5,
              starHistogram: [],
            ),
          ]);
      return build();
    },
    act: (b) => b.add(const ProductListStarted(_q)),
    verify: (b) {
      expect(b.state.availability['p-1']?.inStock, isTrue);
      expect(b.state.aggregates['p-1']?.ratingCount, 5);
    },
  );
}
