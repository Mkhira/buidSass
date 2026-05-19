import 'package:customer_flutter/core/error/failure.dart';
import 'package:customer_flutter/features/pricing/data/models/pricing_models.dart';
import 'package:customer_flutter/features/pricing/data/pricing_gateway_impl.dart';
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

PricingRequest _req({String? coupon}) => PricingRequest(
      lines: const [PricingLineRequest(productId: 'p-1', qty: 1)],
      marketCode: 'SA',
      buyerKind: PricingBuyerKind.consumer,
      couponCode: coupon,
    );

void main() {
  group('preview', () {
    test('serializes the request body and decodes the response', () async {
      final pair = _buildStubbedDio((opts) => {
            'total': {'amount': '120.00', 'currency': 'SAR'},
            'lines': [
              {
                'productId': 'p-1',
                'qty': 1,
                'unitPrice': {'amount': '120.00', 'currency': 'SAR'},
                'discount': {'amount': '0.00', 'currency': 'SAR'},
                'lineTotal': {'amount': '120.00', 'currency': 'SAR'},
                'tierLabel': 'consumer',
              }
            ],
            'appliedPromotions': [
              {
                'code': 'WELCOME10',
                'amount': {'amount': '12.00', 'currency': 'SAR'},
                'kind': 'coupon',
              }
            ],
            'explanationToken': 'tok-abc',
          });
      final gw = PricingGatewayImpl(dio: pair.dio);
      final quote = await gw.preview(_req(coupon: 'WELCOME10'));

      expect(quote.total.amountMinor, 12000);
      expect(quote.total.currency, 'SAR');
      expect(quote.lines.single.tierLabel, 'consumer');
      expect(quote.lines.single.lineTotal.amountMinor, 12000);
      expect(quote.appliedPromotions.single.code, 'WELCOME10');
      expect(quote.appliedPromotions.single.kind, 'coupon');
      expect(quote.explanationToken, 'tok-abc');

      final body = pair.stub.requests.single.data as Map<String, Object?>;
      expect(body['marketCode'], 'SA');
      expect(body['buyerKind'], 'consumer');
      expect(body['couponCode'], 'WELCOME10');
      expect(body['lines'], [
        {'productId': 'p-1', 'qty': 1}
      ]);
    });

    test('omits couponCode when null/empty', () async {
      final pair = _buildStubbedDio((opts) => {
            'total': {'amount': '0.00', 'currency': 'SAR'},
            'lines': [],
            'appliedPromotions': [],
            'explanationToken': '',
          });
      final gw = PricingGatewayImpl(dio: pair.dio);
      await gw.preview(_req());
      final body = pair.stub.requests.single.data as Map<String, Object?>;
      expect(body.containsKey('couponCode'), isFalse);
    });

    test('empty cart → 422 → ValidationFailure', () async {
      final pair = _buildStubbedDio((opts) => DioException(
            requestOptions: RequestOptions(path: opts.path),
            type: DioExceptionType.badResponse,
            response: Response<Object?>(
              requestOptions: RequestOptions(path: opts.path),
              statusCode: 422,
              data: const {
                'error': {
                  'code': 'pricing.empty_cart',
                  'message': 'No lines',
                  'correlationId': 'corr-pri-1',
                  'details': {
                    'fields': [
                      {'path': 'lines', 'message': 'must not be empty'}
                    ]
                  }
                }
              },
            ),
          ));
      final gw = PricingGatewayImpl(dio: pair.dio);
      await expectLater(
        () => gw.preview(const PricingRequest(
          lines: [],
          marketCode: 'SA',
          buyerKind: PricingBuyerKind.consumer,
        )),
        throwsA(isA<ValidationFailure>()
            .having((f) => f.code, 'code', 'pricing.empty_cart')
            .having((f) => f.fields, 'fields', hasLength(1))),
      );
    });

    test('connectionError → OfflineFailure', () async {
      final pair = _buildStubbedDio((opts) => DioException(
            requestOptions: RequestOptions(path: opts.path),
            type: DioExceptionType.connectionError,
          ));
      final gw = PricingGatewayImpl(dio: pair.dio);
      await expectLater(
        () => gw.preview(_req()),
        throwsA(isA<OfflineFailure>()),
      );
    });

    test('non-object body → Failure', () async {
      final pair = _buildStubbedDio((opts) => 'oops');
      final gw = PricingGatewayImpl(dio: pair.dio);
      await expectLater(() => gw.preview(_req()), throwsA(isA<Failure>()));
    });
  });
}
