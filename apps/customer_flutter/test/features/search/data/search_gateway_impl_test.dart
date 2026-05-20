import 'package:customer_flutter/features/search/data/models/search_models.dart';
import 'package:customer_flutter/features/search/data/search_gateway_impl.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

typedef _Handler = Object? Function(RequestOptions opts);

class _StubInterceptor extends Interceptor {
  _StubInterceptor(this.handler);
  final _Handler handler;
  final List<RequestOptions> requests = [];

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler h) {
    requests.add(options);
    final result = handler(options);
    if (result is DioException) {
      h.reject(result);
      return;
    }
    h.resolve(Response<Object?>(
      requestOptions: options,
      statusCode: 200,
      data: result,
    ));
  }
}

({Dio dio, _StubInterceptor stub}) _build(_Handler handler) {
  final dio = Dio(BaseOptions(baseUrl: 'https://example.test'));
  final stub = _StubInterceptor(handler);
  dio.interceptors.add(stub);
  return (dio: dio, stub: stub);
}

void main() {
  group('autocomplete', () {
    test('posts query/marketCode/locale and decodes the response', () async {
      final pair = _build((_) => {
            'suggestions': [
              {'label': 'tile', 'kind': 'term'},
              {
                'label': 'Bathroom tiles',
                'kind': 'category',
                'linkSlug': 'bathroom-tiles',
              },
            ],
            'topMatches': [
              {
                'productId': 'p-1',
                'slug': 'tile-a',
                'name': 'Tile A',
                'imageUrl': 'https://cdn/x',
                'priceHint': {'amount': '120.00', 'currency': 'SAR'},
              }
            ],
          });
      final gw = SearchGatewayImpl(dio: pair.dio);
      final result = await gw.autocomplete(const AutocompleteRequest(
        query: 'til',
        marketCode: 'ksa',
        locale: 'en',
      ));
      final req = pair.stub.requests.single;
      expect(req.path, '/v1/customer/search/autocomplete');
      expect((req.data as Map)['query'], 'til');
      expect((req.data as Map)['marketCode'], 'ksa');
      expect(result.suggestions, hasLength(2));
      expect(result.suggestions.last.linkSlug, 'bathroom-tiles');
      expect(result.topMatches.single.priceHint.amount, '120.00');
    });

    test('malformed payload surfaces as a Failure', () async {
      final pair = _build((_) => 'not a map');
      final gw = SearchGatewayImpl(dio: pair.dio);
      await expectLater(
        () => gw.autocomplete(const AutocompleteRequest(
          query: 'q',
          marketCode: 'ksa',
          locale: 'en',
        )),
        throwsA(anything),
      );
    });
  });

  group('searchProducts', () {
    test('encodes facets + sort + paging in the POST body', () async {
      final pair = _build((_) => {
            'items': [
              {
                'id': 'p-1',
                'slug': 'tile-a',
                'name': 'Tile A',
                'thumbnailUrl': '',
                'priceMinor': 12000,
                'currency': 'SAR',
                'isRestricted': false,
                'inStock': true,
              }
            ],
            'page': 2,
            'pageSize': 24,
            'totalCount': 100,
            'facets': [
              {
                'key': 'brand',
                'label': 'Brand',
                'type': 'checkbox',
                'options': [
                  {'value': 'brand-x', 'label': 'Brand X', 'count': 12}
                ],
              }
            ],
            'sortOptions': [
              {'key': 'relevance', 'label': 'Relevance'}
            ],
          });
      final gw = SearchGatewayImpl(dio: pair.dio);
      final result = await gw.searchProducts(const SearchProductsRequest(
        query: 'tile',
        marketCode: 'ksa',
        locale: 'en',
        page: 2,
        sort: 'priceAsc',
        facets: {
          'brand': ['brand-x']
        },
      ));
      final body = pair.stub.requests.single.data as Map;
      expect(body['page'], 2);
      expect(body['sort'], 'priceAsc');
      expect(body['facets'], {
        'brand': ['brand-x']
      });
      expect(result.page, 2);
      expect(result.items.single.slug, 'tile-a');
      expect(result.facets.single.options.single.count, 12);
    });

    test('hasMore is true when totalCount exceeds page * pageSize', () async {
      final pair = _build((_) => {
            'items': const [],
            'page': 1,
            'pageSize': 10,
            'totalCount': 30,
            'facets': const [],
            'sortOptions': const [],
          });
      final gw = SearchGatewayImpl(dio: pair.dio);
      final result = await gw.searchProducts(const SearchProductsRequest(
        query: 'q',
        marketCode: 'ksa',
        locale: 'en',
      ));
      expect(result.hasMore, isTrue);
    });
  });

  group('lookup', () {
    test('accepts sku-only payloads with a stable wire shape', () async {
      final pair = _build((_) => {
            'matched': true,
            'match': {
              'productId': 'p-1',
              'slug': 'tile-a',
              'name': 'Tile A',
              'kind': 'sku',
            },
          });
      final gw = SearchGatewayImpl(dio: pair.dio);
      final result = await gw.lookup(const LookupRequest(
        sku: 'SKU-123',
        marketCode: 'ksa',
      ));
      final body = pair.stub.requests.single.data as Map;
      expect(body.containsKey('sku'), isTrue);
      expect(body.containsKey('barcode'), isFalse);
      expect(result.matched, isTrue);
      expect(result.match?.slug, 'tile-a');
    });

    test('no-match path decodes matched=false', () async {
      final pair = _build((_) => {'matched': false});
      final gw = SearchGatewayImpl(dio: pair.dio);
      final result = await gw.lookup(const LookupRequest(
        barcode: '00000',
        marketCode: 'ksa',
      ));
      expect(result.matched, isFalse);
      expect(result.match, isNull);
    });
  });
}
