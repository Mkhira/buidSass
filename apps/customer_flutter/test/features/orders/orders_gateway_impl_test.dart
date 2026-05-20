import 'package:customer_flutter/features/orders/data/models/order_models.dart';
import 'package:customer_flutter/features/orders/data/orders_gateway_impl.dart';
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
  group('list', () {
    test('encodes filter into query and parses page', () async {
      final pair = _build((_) => {
            'items': [
              {
                'id': 'o-1',
                'orderNumber': '2026-1',
                'placedAt': '2026-05-01T00:00:00Z',
                'totals': {
                  'subtotal': '100',
                  'discount': '0',
                  'tax': '15',
                  'shipping': '0',
                  'grandTotal': '115',
                  'currency': 'SAR'
                },
                'orderState': 'confirmed',
                'paymentState': 'captured',
                'fulfillmentState': 'shipped',
                'refundState': 'none',
                'itemPreview': [
                  {'imageUrl': 'x', 'name': 'A'},
                ],
              }
            ],
            'page': 1,
            'pageSize': 20,
            'totalCount': 1,
          });
      final gw = OrdersGatewayImpl(dio: pair.dio);
      final page = await gw.list(const OrdersListFilter(status: 'shipped'));
      expect(pair.stub.requests.single.queryParameters['status'], 'shipped');
      expect(page.items.single.orderNumber, '2026-1');
      expect(page.items.single.states.fulfillmentState, 'shipped');
    });
  });

  group('getById', () {
    test('parses four state pills + actions', () async {
      final pair = _build((_) => {
            'id': 'o-9',
            'orderNumber': '2026-9',
            'placedAt': '2026-05-01T00:00:00Z',
            'states': {
              'orderState': 'confirmed',
              'paymentState': 'captured',
              'fulfillmentState': 'shipped',
              'refundState': 'none',
            },
            'actions': {
              'canCancel': true,
              'canReorder': true,
              'canRetryPayment': false,
              'canReturn': true,
            },
            'lines': const [],
            'totals': const {
              'subtotal': '0',
              'discount': '0',
              'tax': '0',
              'shipping': '0',
              'grandTotal': '0',
              'currency': 'SAR'
            },
          });
      final gw = OrdersGatewayImpl(dio: pair.dio);
      final d = await gw.getById('o-9');
      expect(d.actions.canCancel, isTrue);
      expect(d.actions.canRetryPayment, isFalse);
      expect(d.states.fulfillmentState, 'shipped');
    });
  });

  group('reorder', () {
    test('splits available + unavailable sections', () async {
      final pair = _build((_) => {
            'available': [
              {
                'productId': 'p-1',
                'qty': 2,
                'name': 'A',
                'priceHint': {'amount': '60', 'currency': 'SAR'},
              }
            ],
            'unavailable': [
              {
                'productId': 'p-2',
                'name': 'B',
                'reason': 'out_of_stock',
              }
            ],
          });
      final gw = OrdersGatewayImpl(dio: pair.dio);
      final r = await gw.reorder('o-1');
      expect(r.available.single.qty, 2);
      expect(r.unavailable.single.reason, 'out_of_stock');
    });
  });

  group('return-eligibility', () {
    test('decodes anyEligible + per-line reasons', () async {
      final pair = _build((_) => {
            'lines': [
              {
                'productId': 'p-1',
                'name': 'A',
                'qty': 1,
                'eligible': true,
                'windowEndsAt': '2026-05-15T00:00:00Z',
              }
            ],
            'anyEligible': true,
            'policyMarket': 'SA',
          });
      final gw = OrdersGatewayImpl(dio: pair.dio);
      final e = await gw.getReturnEligibility('o-1');
      expect(e.anyEligible, isTrue);
      expect(e.lines.single.eligible, isTrue);
    });
  });
}
