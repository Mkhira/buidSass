import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/reviews/data/reviews_aggregates_gateway_impl.dart';
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
      h.reject(DioException(
        requestOptions: options,
        type: result.type,
        response: result.response,
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
  group('getAggregatesBatch', () {
    test('batches product_ids + decodes histograms', () async {
      final pair = _buildStubbedDio((opts) => [
            {
              'productId': 'p-1',
              'ratingAverage': 4.7,
              'ratingCount': 125,
              'starHistogram': [3, 7, 12, 28, 75],
            },
            {'productId': 'p-2', 'ratingAverage': 3.5, 'ratingCount': 4},
          ]);
      final gw = ReviewsAggregatesGatewayImpl(dio: pair.dio);
      final result = await gw.getAggregatesBatch(
        productIds: const ['p-1', 'p-2'],
        marketCode: 'SA',
      );
      expect(result, hasLength(2));
      expect(result.first.starHistogram, [3, 7, 12, 28, 75]);
      expect(result.first.ratingAverage, closeTo(4.7, 0.001));
      expect(result.last.starHistogram, isEmpty);

      final qp = pair.stub.requests.single.queryParameters;
      expect(qp['product_ids'], 'p-1,p-2');
      expect(qp['market_code'], 'SA');
    });

    test('empty productIds short-circuits', () async {
      final pair = _buildStubbedDio((opts) => []);
      final gw = ReviewsAggregatesGatewayImpl(dio: pair.dio);
      final result = await gw.getAggregatesBatch(
        productIds: const [],
        marketCode: 'SA',
      );
      expect(result, isEmpty);
      expect(pair.stub.requests, isEmpty);
    });

    test('429 → ValidationFailure (rate-limited)', () async {
      final pair = _buildStubbedDio((opts) => DioException(
            requestOptions: RequestOptions(path: opts.path),
            type: DioExceptionType.badResponse,
            response: Response<Object?>(
              requestOptions: RequestOptions(path: opts.path),
              statusCode: 429,
              data: const {
                'error': {
                  'code': 'rate.limited',
                  'message': 'Slow down',
                  'correlationId': 'c-rev',
                  'details': {'retryAfterSeconds': 60},
                },
              },
            ),
          ));
      final gw = ReviewsAggregatesGatewayImpl(dio: pair.dio);
      await expectLater(
        () => gw.getAggregatesBatch(
          productIds: const ['p-1'],
          marketCode: 'SA',
        ),
        throwsA(isA<ValidationFailure>()
            .having((f) => f.retryAfterSeconds, 'retryAfterSeconds', 60)),
      );
    });
  });

  group('getAggregate', () {
    test('object response decodes', () async {
      final pair = _buildStubbedDio((opts) => {
            'productId': 'p-1',
            'ratingAverage': 4.0,
            'ratingCount': 10,
            'starHistogram': [1, 0, 2, 3, 4],
          });
      final gw = ReviewsAggregatesGatewayImpl(dio: pair.dio);
      final result = await gw.getAggregate(
        productId: 'p-1',
        marketCode: 'SA',
      );
      expect(result, isNotNull);
      expect(result!.ratingCount, 10);
      expect(pair.stub.requests.single.path,
          '/v1/public/reviews/aggregates/p-1');
    });

    test('single-element array response is unwrapped', () async {
      final pair = _buildStubbedDio((opts) => [
            {
              'productId': 'p-2',
              'ratingAverage': 4.5,
              'ratingCount': 8,
              'starHistogram': [],
            }
          ]);
      final gw = ReviewsAggregatesGatewayImpl(dio: pair.dio);
      final result = await gw.getAggregate(
        productId: 'p-2',
        marketCode: 'SA',
      );
      expect(result?.productId, 'p-2');
    });

    test('malformed body returns null instead of throwing', () async {
      final pair = _buildStubbedDio((opts) => 'oops');
      final gw = ReviewsAggregatesGatewayImpl(dio: pair.dio);
      final result =
          await gw.getAggregate(productId: 'p-1', marketCode: 'SA');
      expect(result, isNull);
    });
  });
}
