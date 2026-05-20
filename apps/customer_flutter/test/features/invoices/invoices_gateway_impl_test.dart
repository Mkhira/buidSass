import 'package:customer_flutter/features/invoices/data/invoices_gateway_impl.dart';
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
  group('getPreview', () {
    test('decodes billing + lines + totals + VAT rate', () async {
      final pair = _build((_) => {
            'invoiceNumber': 'INV-2026-05-1',
            'issuedAt': '2026-05-01T00:00:00Z',
            'currency': 'SAR',
            'billing': {
              'name': 'Test',
              'address': '1 Street',
              'vatNumber': '300000000000003',
            },
            'lines': [
              {
                'name': 'Item',
                'qty': 1,
                'unitPrice': '60.00',
                'taxRate': '0.15',
                'lineTotal': '69.00',
              }
            ],
            'totals': {
              'subtotal': '60.00',
              'taxTotal': '9.00',
              'grandTotal': '69.00'
            },
            'downloadUrl': '/v1/customer/orders/o-1/invoice.pdf',
          });
      final gw = InvoicesGatewayImpl(dio: pair.dio);
      final p = await gw.getPreview('o-1');
      expect(p.invoiceNumber, 'INV-2026-05-1');
      expect(p.currency, 'SAR');
      expect(p.billing.vatNumber, '300000000000003');
      expect(p.lines.single.taxRate, '0.15');
      expect(p.totals.taxTotal, '9.00');
    });
  });

  group('getPdf', () {
    test('returns binary body as Uint8List', () async {
      const bytes = [0x25, 0x50, 0x44, 0x46]; // "%PDF"
      final pair = _build((_) => bytes);
      final gw = InvoicesGatewayImpl(dio: pair.dio);
      final out = await gw.getPdf('o-1');
      expect(out, equals(bytes));
      // The PDF request must ask dio for raw bytes — otherwise dio
      // would try to JSON-decode the binary body and fail at runtime.
      expect(pair.stub.requests.single.responseType, ResponseType.bytes);
    });

    test('empty body raises DioException', () async {
      final pair = _build((_) => <int>[]);
      final gw = InvoicesGatewayImpl(dio: pair.dio);
      expect(gw.getPdf('o-1'), throwsA(isA<Object>()));
    });
  });
}
