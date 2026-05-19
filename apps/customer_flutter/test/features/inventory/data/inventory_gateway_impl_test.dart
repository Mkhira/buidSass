import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/inventory/data/inventory_gateway_impl.dart';
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
  group('getAvailability', () {
    test('batches productIds into comma-separated query param', () async {
      final pair = _buildStubbedDio((opts) => []);
      final gw = InventoryGatewayImpl(dio: pair.dio);
      await gw.getAvailability(
        productIds: const ['p-1', 'p-2', 'p-3'],
        market: 'ksa',
      );
      final qp = pair.stub.requests.single.queryParameters;
      expect(qp['productIds'], 'p-1,p-2,p-3');
      expect(qp['market'], 'ksa');
    });

    test('decodes badge state', () async {
      final pair = _buildStubbedDio((opts) => [
            {
              'productId': 'p-1',
              'inStock': true,
              'lowStock': false,
              'earliestDeliveryDate': '2026-05-21',
              'warehouseHint': 'RUH-1',
            },
            {'productId': 'p-2', 'inStock': true, 'lowStock': true},
            {'productId': 'p-3', 'inStock': false, 'lowStock': false},
          ]);
      final gw = InventoryGatewayImpl(dio: pair.dio);
      final result = await gw.getAvailability(
        productIds: const ['p-1', 'p-2', 'p-3'],
        market: 'ksa',
      );
      expect(result, hasLength(3));
      expect(result[0].badgeState, 'inStock');
      expect(result[0].earliestDeliveryDate?.year, 2026);
      expect(result[0].warehouseHint, 'RUH-1');
      expect(result[1].badgeState, 'low');
      expect(result[2].badgeState, 'outOfStock');
    });

    test('empty productIds short-circuits (no HTTP call)', () async {
      final pair = _buildStubbedDio((opts) => []);
      final gw = InventoryGatewayImpl(dio: pair.dio);
      final result =
          await gw.getAvailability(productIds: const [], market: 'ksa');
      expect(result, isEmpty);
      expect(pair.stub.requests, isEmpty);
    });

    test('connectionError → OfflineFailure', () async {
      final pair = _buildStubbedDio((opts) => DioException(
            requestOptions: RequestOptions(path: opts.path),
            type: DioExceptionType.connectionError,
          ));
      final gw = InventoryGatewayImpl(dio: pair.dio);
      await expectLater(
        () => gw.getAvailability(productIds: const ['p-1'], market: 'ksa'),
        throwsA(isA<OfflineFailure>()),
      );
    });

    test('non-list body → empty list', () async {
      final pair = _buildStubbedDio((opts) => {'wrong': 'shape'});
      final gw = InventoryGatewayImpl(dio: pair.dio);
      final result =
          await gw.getAvailability(productIds: const ['p-1'], market: 'ksa');
      expect(result, isEmpty);
    });
  });
}
