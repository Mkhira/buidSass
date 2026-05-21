import 'package:customer_flutter/core/api/idempotency_interceptor.dart';
import 'package:customer_flutter/features/reviews/data/models/review_models.dart';
import 'package:customer_flutter/features/reviews/data/reviews_customer_gateway_impl.dart';
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

void main() {
  group('submit', () {
    test('sends Idempotency-Key + body', () async {
      final pair = _build((_) => {
            'id': 'rv-1',
            'state': 'pending_moderation',
            'createdAt': '2026-05-01T00:00:00Z',
          });
      final gw = ReviewsCustomerGatewayImpl(dio: pair.dio);
      final r = await gw.submit(
        request: const CreateReviewRequest(
          productId: 'p-1',
          orderId: 'o-1',
          rating: 5,
          comment: 'Great',
          locale: 'en',
          mediaIds: ['m-1'],
        ),
        idempotencyKey: 'rev-key-1',
      );
      expect(
        pair.stub.requests.single.headers[IdempotencyInterceptor.headerName],
        'rev-key-1',
      );
      expect(pair.stub.requests.single.path, '/v1/customer/reviews');
      expect(r.state, 'pending_moderation');
    });
  });

  group('listMine', () {
    test('encodes filter + parses page', () async {
      final pair = _build((_) => {
            'items': [
              {
                'id': 'rv-1',
                'productId': 'p-1',
                'productName': 'A',
                'rating': 4,
                'state': 'visible',
                'createdAt': '2026-05-01T00:00:00Z',
              }
            ],
            'page': 1,
            'pageSize': 20,
            'totalCount': 1,
          });
      final gw = ReviewsCustomerGatewayImpl(dio: pair.dio);
      final page = await gw.listMine(const MyReviewsFilter(state: 'visible'));
      expect(pair.stub.requests.single.queryParameters['state'], 'visible');
      expect(page.items.single.rating, 4);
    });
  });

  group('getMine', () {
    test('parses editableUntil', () async {
      final pair = _build((_) => {
            'id': 'rv-9',
            'productId': 'p-1',
            'productName': 'A',
            'rating': 5,
            'comment': 'Solid',
            'state': 'visible',
            'createdAt': '2026-05-01T00:00:00Z',
            'media': [],
            'locale': 'en',
            'editableUntil': '2026-05-10T00:00:00Z',
          });
      final gw = ReviewsCustomerGatewayImpl(dio: pair.dio);
      final d = await gw.getMine('rv-9');
      expect(d.editableUntil?.year, 2026);
      expect(d.isEditableAt(DateTime.utc(2026, 5, 5)), isTrue);
      expect(d.isEditableAt(DateTime.utc(2026, 5, 11)), isFalse);
    });
  });

  group('edit', () {
    test('PATCHes with rating + comment', () async {
      final pair = _build((opts) {
        expect(opts.method, 'PATCH');
        return {
          'id': 'rv-9',
          'productId': 'p-1',
          'productName': 'A',
          'rating': 3,
          'comment': 'Updated',
          'state': 'visible',
          'createdAt': '2026-05-01T00:00:00Z',
          'media': [],
          'locale': 'en',
        };
      });
      final gw = ReviewsCustomerGatewayImpl(dio: pair.dio);
      final d = await gw.edit(
        reviewId: 'rv-9',
        request: const EditReviewRequest(rating: 3, comment: 'Updated'),
      );
      expect(d.rating, 3);
    });
  });

  group('getReportReasons', () {
    test('parses array body', () async {
      final pair = _build((_) => [
            {'key': 'spam', 'label': 'Spam'},
            {'key': 'abuse', 'label': 'Abuse'},
          ]);
      final gw = ReviewsCustomerGatewayImpl(dio: pair.dio);
      final reasons = await gw.getReportReasons();
      expect(reasons, hasLength(2));
      expect(reasons.first.key, 'spam');
    });
  });

  group('report', () {
    test('POSTs reasonKey + note', () async {
      final pair = _build((_) => {'id': 'rep-1', 'state': 'submitted'});
      final gw = ReviewsCustomerGatewayImpl(dio: pair.dio);
      final r = await gw.report(
        reviewId: 'rv-9',
        request: const ReportReviewRequest(reasonKey: 'spam', note: 'why'),
      );
      expect(pair.stub.requests.single.path, '/v1/customer/reviews/rv-9/report');
      expect(r.state, 'submitted');
    });
  });
}
