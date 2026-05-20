import 'dart:typed_data';

import 'package:customer_flutter/core/api/idempotency_interceptor.dart';
import 'package:customer_flutter/features/returns/data/models/return_models.dart';
import 'package:customer_flutter/features/returns/data/returns_gateway_impl.dart';
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
  // Mirror prod: IdempotencyInterceptor lifts the `extra` key onto the
  // header, so tests can assert the header value.
  dio.interceptors.add(const IdempotencyInterceptor());
  final stub = _Stub(handler);
  dio.interceptors.add(stub);
  return (dio: dio, stub: stub);
}

void main() {
  group('list', () {
    test('encodes status + page into query and parses items', () async {
      final pair = _build((_) => {
            'items': [
              {
                'id': 'r-1',
                'returnNumber': 'R-2026-1',
                'orderId': 'o-1',
                'orderNumber': '2026-1',
                'createdAt': '2026-05-01T00:00:00Z',
                'state': 'issued',
                'refundAmount': {'amount': '120.00', 'currency': 'SAR'},
              }
            ],
            'page': 1,
            'pageSize': 20,
            'totalCount': 1,
          });
      final gw = ReturnsGatewayImpl(dio: pair.dio);
      final page =
          await gw.list(const ReturnsListFilter(status: 'issued', page: 1));
      expect(pair.stub.requests.single.queryParameters['status'], 'issued');
      expect(pair.stub.requests.single.queryParameters['page'], 1);
      expect(page.items.single.state, 'issued');
      expect(page.items.single.refundAmount?.amount, '120.00');
    });
  });

  group('getById', () {
    test('decodes timeline + lines + refund + rejection', () async {
      final pair = _build((_) => {
            'id': 'r-9',
            'returnNumber': 'R-2026-9',
            'orderId': 'o-1',
            'orderNumber': '2026-9',
            'state': 'rejected',
            'timeline': [
              {
                'kind': 'created',
                'occurredAt': '2026-05-01T00:00:00Z',
                'actor': 'customer'
              },
              {
                'kind': 'rejected',
                'occurredAt': '2026-05-02T00:00:00Z',
                'actor': 'admin',
                'note': 'Outside policy'
              }
            ],
            'lines': [
              {
                'productId': 'p-1',
                'name': 'A',
                'qty': 1,
                'reason': 'damaged',
                'photos': [
                  {'url': 'https://x/1.jpg'}
                ],
              }
            ],
            'refund': null,
            'rejection': {
              'reason': 'outside_window',
              'noteToCustomer': 'Window expired',
            },
          });
      final gw = ReturnsGatewayImpl(dio: pair.dio);
      final d = await gw.getById('r-9');
      expect(d.state, 'rejected');
      expect(d.timeline, hasLength(2));
      expect(d.lines.single.photos.single.url, 'https://x/1.jpg');
      expect(d.rejection?.reason, 'outside_window');
      expect(d.refund, isNull);
    });
  });

  group('uploadPhoto', () {
    test('sends Idempotency-Key header and multipart body', () async {
      final pair = _build((_) => {
            'photoId': 'p-1',
            'url': 'https://x/p-1.jpg',
            'checksum': 'sha256:abc'
          });
      final gw = ReturnsGatewayImpl(dio: pair.dio);
      final r = await gw.uploadPhoto(
        bytes: Uint8List.fromList(const [1, 2, 3]),
        filename: 'photo.jpg',
        clientPhotoKey: 'key-1',
      );
      // Header is propagated by IdempotencyInterceptor.
      expect(
          pair.stub.requests.single.headers[IdempotencyInterceptor.headerName],
          'key-1');
      // Multipart wraps the FormData; we can't easily inspect the body
      // here, but the contentType should be multipart.
      expect(
          pair.stub.requests.single.headers['content-type']?.toString() ?? '',
          startsWith('multipart/form-data'));
      expect(r.photoId, 'p-1');
    });
  });

  group('create', () {
    test('routes via orders root and sets Idempotency-Key', () async {
      final pair = _build((_) => {
            'id': 'r-9',
            'returnNumber': 'R-2026-9',
            'state': 'pending',
            'createdAt': '2026-05-01T00:00:00Z',
          });
      final gw = ReturnsGatewayImpl(dio: pair.dio);
      final r = await gw.create(
        orderId: 'o-1',
        request: const CreateReturnRequest(
          lines: [
            CreateReturnLine(
              productId: 'p-1',
              qty: 1,
              reason: 'damaged',
              photoIds: ['photo-1'],
            ),
          ],
          preferredRefundMethod: 'original_payment',
        ),
        idempotencyKey: 'wizard-key-1',
      );
      expect(pair.stub.requests.single.path, '/v1/customer/orders/o-1/returns');
      expect(
          pair.stub.requests.single.headers[IdempotencyInterceptor.headerName],
          'wizard-key-1');
      expect(r.id, 'r-9');
      expect(r.state, 'pending');
    });
  });
}
