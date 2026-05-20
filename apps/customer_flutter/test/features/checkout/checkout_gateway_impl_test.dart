import 'package:customer_flutter/features/checkout/data/checkout_gateway_impl.dart';
import 'package:customer_flutter/features/checkout/data/models/checkout_models.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

typedef _Handler = Object? Function(RequestOptions opts);

class _Stub extends Interceptor {
  _Stub(this.handler);
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

({Dio dio, _Stub stub}) _build(_Handler handler) {
  final dio = Dio(BaseOptions(baseUrl: 'https://example.test'));
  final stub = _Stub(handler);
  dio.interceptors.add(stub);
  return (dio: dio, stub: stub);
}

void main() {
  group('createSession', () {
    test('posts the create request and parses the result', () async {
      final pair = _build((_) => {
            'sessionId': 'sess-1',
            'expiresAt': '2026-01-01T00:00:00Z',
            'summary': {
              'sessionId': 'sess-1',
              'expiresAt': '2026-01-01T00:00:00Z',
              'lines': const [],
              'totals': {
                'subtotal': '0',
                'discount': '0',
                'tax': '0',
                'shipping': '0',
                'grandTotal': '0',
                'currency': 'SAR'
              },
              'availableMethods': ['card', 'cod'],
            },
            'availableSteps': ['address', 'shipping', 'payment', 'review'],
          });
      final gw = CheckoutGatewayImpl(dio: pair.dio);
      final result = await gw.createSession(const CreateSessionRequest(
        lines: [CreateSessionLine(productId: 'p-1', qty: 2)],
        buyerKind: 'consumer',
        marketCode: 'ksa',
      ));
      expect(result.sessionId, 'sess-1');
      expect(result.summary.availableMethods, ['card', 'cod']);
      expect(pair.stub.requests.single.path, '/v1/customer/checkout/sessions');
      expect((pair.stub.requests.single.data as Map)['buyerKind'], 'consumer');
    });
  });

  group('drift handling', () {
    test('409 maps to CheckoutDriftException with parsed deltas', () async {
      final pair = _build((opts) => DioException(
            requestOptions: opts,
            type: DioExceptionType.badResponse,
            response: Response<Object?>(
              requestOptions: opts,
              statusCode: 409,
              data: const {
                'error': {
                  'code': 'checkout.drift',
                  'message': 'Prices changed',
                  'correlationId': 'corr-9',
                  'details': {
                    'deltas': [
                      {
                        'kind': 'price',
                        'productId': 'p-1',
                        'before': '100.00',
                        'after': '110.00',
                      }
                    ],
                  },
                },
              },
            ),
          ));
      final gw = CheckoutGatewayImpl(dio: pair.dio);
      await expectLater(
        () => gw.patchShipping(sessionId: 'sess-1', method: 'standard'),
        throwsA(isA<CheckoutDriftException>()
            .having((e) => e.correlationId, 'correlationId', 'corr-9')
            .having(
                (e) => e.details.deltas.single.kind, 'delta kind', 'price')),
      );
    });

    test('non-409 errors flow through the regular ErrorMapper', () async {
      final pair = _build((opts) => DioException(
            requestOptions: opts,
            type: DioExceptionType.badResponse,
            response: Response<Object?>(
              requestOptions: opts,
              statusCode: 500,
              data: const {
                'error': {
                  'code': 'server.boom',
                  'message': 'down',
                  'correlationId': 'corr-x',
                },
              },
            ),
          ));
      final gw = CheckoutGatewayImpl(dio: pair.dio);
      await expectLater(
        () => gw.getSummary('sess-1'),
        throwsA(isNot(isA<CheckoutDriftException>())),
      );
    });
  });

  group('submit', () {
    test('forwards idempotency-key via Options.extra', () async {
      final pair = _build((_) => {
            'orderId': 'o-1',
            'orderNumber': '2026-1',
            'paymentState': 'captured',
            'fulfillmentState': 'pending',
            'redirect': {'kind': 'none'},
          });
      final gw = CheckoutGatewayImpl(dio: pair.dio);
      await gw.submit(sessionId: 'sess-1', idempotencyKey: 'KEY-1');
      expect(pair.stub.requests.single.extra['idempotencyKey'], 'KEY-1');
    });
  });
}
