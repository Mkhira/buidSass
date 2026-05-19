import 'dart:async';

import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/catalog/data/catalog_gateway_impl.dart';
import 'package:customer_flutter/features/catalog/data/models/catalog_models.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

typedef _Handler = Object? Function(RequestOptions opts);

/// Hand-rolled stub interceptor — same shape as
/// test/features/auth/auth_repository_impl_test.dart so the testing style
/// is consistent across features.
class _StubInterceptor extends Interceptor {
  _StubInterceptor(this.handler);
  final _Handler handler;

  int callCount = 0;
  final List<RequestOptions> requests = [];

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler h) {
    callCount++;
    requests.add(options);
    final result = handler(options);
    if (result is DioException) {
      h.reject(DioException(
        requestOptions: options,
        type: result.type,
        response: result.response == null
            ? null
            : Response<Object?>(
                requestOptions: options,
                statusCode: result.response!.statusCode,
                data: result.response!.data,
              ),
      ));
      return;
    }
    h.resolve(Response<Object?>(
      requestOptions: options,
      statusCode: 200,
      data: result,
    ));
  }
}

({Dio dio, _StubInterceptor stub}) _buildStubbedDio(_Handler handler) {
  final dio = Dio(BaseOptions(baseUrl: 'https://example.test'));
  final stub = _StubInterceptor(handler);
  dio.interceptors.add(stub);
  return (dio: dio, stub: stub);
}

void main() {
  group('listCategories', () {
    test('decodes a list of categories with locale-keyed names', () async {
      final pair = _buildStubbedDio((opts) => [
            {
              'id': 'cat-1',
              'slug': 'bathroom-tiles',
              'name': {'ar': 'بلاط الحمام', 'en': 'Bathroom tiles'},
              'iconUrl': 'https://cdn/tile.png',
            },
            {'id': 'cat-2', 'slug': 'sinks', 'name': 'Sinks'},
          ]);
      final gw = CatalogGatewayImpl(
        dio: pair.dio,
        locale: () => 'en',
      );
      final result = await gw.listCategories(market: 'ksa');
      expect(result, hasLength(2));
      expect(result.first.slug, 'bathroom-tiles');
      expect(result.first.name.resolve('en'), 'Bathroom tiles');
      expect(result.first.name.resolve('ar'), 'بلاط الحمام');
      expect(result.last.name.resolve('en'), 'Sinks');
    });

    test('sends the market query param', () async {
      final pair = _buildStubbedDio((opts) => []);
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      await gw.listCategories(market: 'eg');
      expect(pair.stub.requests.single.queryParameters['market'], 'eg');
      expect(pair.stub.requests.single.path,
          '/v1/customer/catalog/categories');
    });

    test('maps DioException to a typed Failure', () async {
      final pair = _buildStubbedDio((opts) => DioException(
            requestOptions: RequestOptions(path: opts.path),
            type: DioExceptionType.badResponse,
            response: Response<Object?>(
              requestOptions: RequestOptions(path: opts.path),
              statusCode: 500,
              data: const {
                'error': {
                  'code': 'server.boom',
                  'message': 'down',
                  'correlationId': 'corr-1',
                },
              },
            ),
          ));
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      await expectLater(
        () => gw.listCategories(market: 'ksa'),
        throwsA(isA<ServerFailure>()
            .having((f) => f.correlationId, 'correlationId', 'corr-1')),
      );
    });
  });

  group('listBrands', () {
    test('decodes brands', () async {
      final pair = _buildStubbedDio((opts) => [
            {'id': 'b-1', 'slug': 'brand-x', 'name': 'Brand X'},
          ]);
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      final result = await gw.listBrands(market: 'ksa');
      expect(result.single.slug, 'brand-x');
    });
  });

  group('listCategoryProducts', () {
    test('encodes pagination + sort + restricted filter into query', () async {
      final pair = _buildStubbedDio((opts) => {
            'items': [
              {
                'id': 'p-1',
                'slug': 'tile-a',
                'name': {'en': 'Tile A'},
                'thumbnailUrl': 'https://cdn/a.png',
                'priceHint': {'amount': '120.00', 'currency': 'SAR'},
                'restricted': false,
              }
            ],
            'page': 2,
            'pageSize': 20,
            'totalItems': 41,
          });
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      final page = await gw.listCategoryProducts(
        slug: 'bathroom-tiles',
        market: 'ksa',
        page: 2,
        pageSize: 20,
        sort: CatalogSort.priceAsc,
        brand: 'brand-x',
        priceMin: 10000,
        priceMax: 90000,
        restricted: CatalogRestrictedFilter.onlyUnrestricted,
      );

      expect(page.items.single.slug, 'tile-a');
      expect(page.items.single.priceHint.amountMinor, 12000);
      expect(page.items.single.priceHint.currency, 'SAR');
      expect(page.page, 2);
      expect(page.totalItems, 41);
      expect(page.hasMore, isTrue);

      final qp = pair.stub.requests.single.queryParameters;
      expect(qp['market'], 'ksa');
      expect(qp['page'], 2);
      expect(qp['pageSize'], 20);
      expect(qp['sort'], 'price-asc');
      expect(qp['brand'], 'brand-x');
      expect(qp['priceMin'], 10000);
      expect(qp['priceMax'], 90000);
      expect(qp['restricted'], 'only-unrestricted');
    });

    test('omits null/empty optional filters', () async {
      final pair = _buildStubbedDio((opts) => {'items': []});
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      await gw.listCategoryProducts(slug: 'cats', market: 'ksa');
      final qp = pair.stub.requests.single.queryParameters;
      expect(qp.containsKey('sort'), isFalse);
      expect(qp.containsKey('brand'), isFalse);
      expect(qp.containsKey('priceMin'), isFalse);
      expect(qp.containsKey('priceMax'), isFalse);
      expect(qp.containsKey('restricted'), isFalse);
    });

    test('accepts a bare list response as a single-page payload', () async {
      final pair = _buildStubbedDio((opts) => [
            {
              'id': 'p-1',
              'slug': 'a',
              'name': 'A',
              'thumbnailUrl': '',
              'priceHint': {'amount': '1.00', 'currency': 'SAR'},
              'restricted': false,
            },
          ]);
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      final page = await gw.listCategoryProducts(slug: 'x', market: 'ksa');
      expect(page.items, hasLength(1));
    });
  });

  group('getProductBySlug', () {
    test('decodes localized name + description + attributes', () async {
      final pair = _buildStubbedDio((opts) => {
            'id': 'p-1',
            'slug': 'tile-a',
            'sku': 'SKU-1',
            'name': {'ar': 'بلاط أ', 'en': 'Tile A'},
            'description': {'en': 'Hard-wearing tile'},
            'mediaUrls': ['https://cdn/a-1.png', 'https://cdn/a-2.png'],
            'attributes': {
              'finish': {'ar': 'لامع', 'en': 'Glossy'},
              'size': '30x30',
            },
            'priceHint': {'amount': '120.00', 'currency': 'SAR'},
            'restricted': true,
            'restrictedRationale': {'en': 'Requires verification'},
          });
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      final detail =
          await gw.getProductBySlug(slug: 'tile-a', market: 'ksa');
      expect(detail.sku, 'SKU-1');
      expect(detail.name.resolve('ar'), 'بلاط أ');
      expect(detail.attributes['finish']?.resolve('ar'), 'لامع');
      expect(detail.attributes['size']?.resolve('en'), '30x30');
      expect(detail.mediaUrls, hasLength(2));
      expect(detail.isRestricted, isTrue);
      expect(detail.restrictedRationale?.resolve('en'),
          'Requires verification');
    });

    test('throws on non-object payload', () async {
      final pair = _buildStubbedDio((opts) => 'not-an-object');
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      await expectLater(
        () => gw.getProductBySlug(slug: 'x', market: 'ksa'),
        throwsA(isA<Failure>()),
      );
    });
  });

  group('cache behavior', () {
    test('returns cached value within TTL without a second HTTP call',
        () async {
      DateTime now = DateTime.utc(2026, 1, 1, 12);
      final pair = _buildStubbedDio((opts) => []);
      final gw = CatalogGatewayImpl(
        dio: pair.dio,
        locale: () => 'en',
        clock: () => now,
        ttl: const Duration(minutes: 5),
      );
      await gw.listCategories(market: 'ksa');
      now = now.add(const Duration(minutes: 4));
      await gw.listCategories(market: 'ksa');
      expect(pair.stub.callCount, 1);
    });

    test('refetches after TTL expires', () async {
      DateTime now = DateTime.utc(2026, 1, 1, 12);
      final pair = _buildStubbedDio((opts) => []);
      final gw = CatalogGatewayImpl(
        dio: pair.dio,
        locale: () => 'en',
        clock: () => now,
        ttl: const Duration(minutes: 5),
      );
      await gw.listCategories(market: 'ksa');
      now = now.add(const Duration(minutes: 5, seconds: 1));
      await gw.listCategories(market: 'ksa');
      expect(pair.stub.callCount, 2);
    });

    test('caches per (locale, market) — locale flip refetches', () async {
      var locale = 'en';
      final pair = _buildStubbedDio((opts) => []);
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => locale);
      await gw.listCategories(market: 'ksa');
      locale = 'ar';
      await gw.listCategories(market: 'ksa');
      expect(pair.stub.callCount, 2);
    });

    test('caches per market — market flip refetches', () async {
      final pair = _buildStubbedDio((opts) => []);
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      await gw.listCategories(market: 'ksa');
      await gw.listCategories(market: 'eg');
      expect(pair.stub.callCount, 2);
    });

    test(
        'listCategoryProducts caches per query-hash; different sort = different key',
        () async {
      final pair = _buildStubbedDio((opts) => {'items': []});
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      await gw.listCategoryProducts(
          slug: 'x', market: 'ksa', sort: CatalogSort.relevance);
      await gw.listCategoryProducts(
          slug: 'x', market: 'ksa', sort: CatalogSort.relevance);
      expect(pair.stub.callCount, 1);
      await gw.listCategoryProducts(
          slug: 'x', market: 'ksa', sort: CatalogSort.priceDesc);
      expect(pair.stub.callCount, 2);
    });

    test('clearCache() forces a refetch', () async {
      final pair = _buildStubbedDio((opts) => []);
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      await gw.listCategories(market: 'ksa');
      gw.clearCache();
      await gw.listCategories(market: 'ksa');
      expect(pair.stub.callCount, 2);
    });

    test('invalidationSignal clears the cache', () async {
      final pair = _buildStubbedDio((opts) => []);
      final controller = StreamController<void>.broadcast();
      final gw = CatalogGatewayImpl(
        dio: pair.dio,
        locale: () => 'en',
        invalidationSignal: controller.stream,
      );
      await gw.listCategories(market: 'ksa');
      controller.add(null);
      // Microtask hop so the stream listener runs.
      await Future<void>.delayed(Duration.zero);
      await gw.listCategories(market: 'ksa');
      expect(pair.stub.callCount, 2);
      await controller.close();
      await gw.dispose();
    });

    test('errors are NOT cached — next call retries', () async {
      var failNext = true;
      final pair = _buildStubbedDio((opts) {
        if (failNext) {
          failNext = false;
          return DioException(
            requestOptions: RequestOptions(path: opts.path),
            type: DioExceptionType.connectionError,
          );
        }
        return <Object?>[];
      });
      final gw = CatalogGatewayImpl(dio: pair.dio, locale: () => 'en');
      await expectLater(
        () => gw.listCategories(market: 'ksa'),
        throwsA(isA<OfflineFailure>()),
      );
      final result = await gw.listCategories(market: 'ksa');
      expect(result, isEmpty);
      expect(pair.stub.callCount, 2);
    });
  });

  group('CatalogMoney.fromJson', () {
    test('parses decimal-string amount to minor units', () {
      final m = CatalogMoney.fromJson(
        const {'amount': '120.50', 'currency': 'SAR'},
      );
      expect(m.amountMinor, 12050);
      expect(m.currency, 'SAR');
    });

    test('prefers amountMinor when both present', () {
      final m = CatalogMoney.fromJson(const {
        'amountMinor': 12345,
        'amount': '999.99',
        'currency': 'EGP',
      });
      expect(m.amountMinor, 12345);
    });

    test('returns zero on malformed payload', () {
      final m = CatalogMoney.fromJson(null);
      expect(m.amountMinor, 0);
      expect(m.currency, '');
    });
  });
}
