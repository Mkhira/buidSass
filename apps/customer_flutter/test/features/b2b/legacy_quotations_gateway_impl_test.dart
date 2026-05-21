import 'package:customer_flutter/core/api/idempotency_interceptor.dart';
import 'package:customer_flutter/features/b2b/data/legacy_quotations_gateway_impl.dart';
import 'package:customer_flutter/features/b2b/data/models/legacy_quotation_models.dart';
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
  dio.interceptors.add(const IdempotencyInterceptor());
  final stub = _Stub(handler);
  dio.interceptors.add(stub);
  return (dio: dio, stub: stub);
}

Map<String, Object?> _detailPayload({String state = 'pending'}) => {
      'id': 'lq-1',
      'quotationNumber': 'QT-1',
      'state': state,
      'createdAt': '2026-05-01T00:00:00Z',
      'lines': [
        {'name': 'gel', 'qty': 5, 'unitPrice': '10', 'lineTotal': '50'},
      ],
      'totals': {
        'subtotal': '50',
        'tax': '7.50',
        'grandTotal': '57.50',
        'currency': 'SAR',
      },
      'actions': {'canAccept': true, 'canReject': true},
    };

void main() {
  test('list accepts bare-array body', () async {
    final pair = _build((_) => [
          {
            'id': 'lq-1',
            'quotationNumber': 'QT-1',
            'state': 'pending',
            'createdAt': '2026-05-01T00:00:00Z',
            'total': {'amount': '50', 'currency': 'SAR'},
          },
        ]);
    final gw = LegacyQuotationsGatewayImpl(dio: pair.dio);
    final items = await gw.list();
    expect(items, hasLength(1));
    expect(items.single.totalAmount, '50');
  });

  test('list accepts {items:[...]} body', () async {
    final pair = _build((_) => {
          'items': [
            {
              'id': 'lq-1',
              'quotationNumber': 'QT-1',
              'state': 'pending',
              'createdAt': '2026-05-01T00:00:00Z',
            },
          ],
        });
    final gw = LegacyQuotationsGatewayImpl(dio: pair.dio);
    final items = await gw.list();
    expect(items, hasLength(1));
  });

  test('list returns [] on 404 (migrated accounts)', () async {
    final pair = _build((opts) => DioException(
          requestOptions: opts,
          type: DioExceptionType.badResponse,
          response: Response<Object?>(
            requestOptions: opts,
            statusCode: 404,
          ),
        ));
    final gw = LegacyQuotationsGatewayImpl(dio: pair.dio);
    final items = await gw.list();
    expect(items, isEmpty);
  });

  test('getById decodes lines, totals, action flags', () async {
    final pair = _build((_) => _detailPayload());
    final gw = LegacyQuotationsGatewayImpl(dio: pair.dio);
    final d = await gw.getById('lq-1');
    expect(d.lines, hasLength(1));
    expect(d.grandTotal, '57.50');
    expect(d.canAccept, isTrue);
  });

  test('accept sends Idempotency-Key + posts to /accept', () async {
    final pair = _build((_) => _detailPayload(state: 'accepted'));
    final gw = LegacyQuotationsGatewayImpl(dio: pair.dio);
    final d = await gw.accept(
      id: 'lq-1',
      request: const LegacyQuotationActionRequest(note: 'ok'),
      idempotencyKey: 'acc-1',
    );
    expect(d.state, 'accepted');
    expect(
      pair.stub.requests.single.path,
      '/v1/customer/quotations/lq-1/accept',
    );
    expect(
      pair.stub.requests.single.headers[IdempotencyInterceptor.headerName],
      'acc-1',
    );
  });

  test('reject posts to /reject', () async {
    final pair = _build((_) => _detailPayload(state: 'rejected'));
    final gw = LegacyQuotationsGatewayImpl(dio: pair.dio);
    await gw.reject(
      id: 'lq-1',
      request: const LegacyQuotationActionRequest(note: 'too high'),
      idempotencyKey: 'rej-1',
    );
    expect(
      pair.stub.requests.single.path,
      '/v1/customer/quotations/lq-1/reject',
    );
  });
}
