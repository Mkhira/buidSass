import 'dart:typed_data';

import 'package:customer_flutter/core/api/idempotency_interceptor.dart';
import 'package:customer_flutter/features/b2b/data/models/quote_models.dart';
import 'package:customer_flutter/features/b2b/data/quotes_gateway_impl.dart';
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

({Dio dio, _Stub stub}) _build(_Handler handler,
    {ResponseType responseType = ResponseType.json}) {
  final dio = Dio(BaseOptions(
    baseUrl: 'https://example.test',
    responseType: responseType,
  ));
  dio.interceptors.add(const IdempotencyInterceptor());
  final stub = _Stub(handler);
  dio.interceptors.add(stub);
  return (dio: dio, stub: stub);
}

Map<String, Object?> _detailPayload({
  String id = 'q-1',
  String state = 'awaiting_acceptance',
  Map<String, Object?>? actions,
}) {
  return {
    'id': id,
    'quoteNumber': 'Q-1',
    'state': state,
    'versions': [
      {
        'versionId': 'v1',
        'publishedAt': '2026-05-01T00:00:00Z',
        'terms': 'Net 30',
        'validUntil': '2026-06-01T00:00:00Z',
        'lines': [
          {
            'productId': 'p-1',
            'name': 'gel',
            'qty': 10,
            'unitPrice': '15.00',
            'lineTotal': '150.00'
          }
        ],
        'totals': {
          'subtotal': '150.00',
          'discount': '0',
          'tax': '22.50',
          'grandTotal': '172.50',
          'currency': 'SAR'
        },
        'documents': [
          {'locale': 'en', 'url': 'https://x/en.pdf'},
        ],
      },
    ],
    'actions': actions ??
        {
          'canSubmitAcceptance': true,
          'canFinalizeAcceptance': false,
          'canRejectAcceptance': true,
          'canRequestRevision': true,
          'canWithdraw': true,
          'canSaveAsTemplate': true,
        },
  };
}

void main() {
  group('list', () {
    test('encodes state filter and parses items', () async {
      final pair = _build((_) => {
            'items': [
              {
                'id': 'q-1',
                'quoteNumber': 'Q-1',
                'state': 'accepted',
                'createdAt': '2026-05-01T00:00:00Z',
                'totals': {'amount': '150.00', 'currency': 'SAR'},
              },
            ],
            'page': 1,
            'pageSize': 20,
            'totalCount': 1,
          });
      final gw = QuotesGatewayImpl(dio: pair.dio);
      final page = await gw.list(const QuotesFilter(state: 'accepted'));
      expect(pair.stub.requests.single.queryParameters['state'], 'accepted');
      expect(page.items.single.state, 'accepted');
      expect(page.items.single.totals?.amount, '150.00');
    });
  });

  group('awaitingMyApproval', () {
    test('parses submittedBy.name', () async {
      final pair = _build((_) => {
            'items': [
              {
                'id': 'q-9',
                'quoteNumber': 'Q-9',
                'state': 'awaiting_acceptance',
                'createdAt': '2026-05-01T00:00:00Z',
                'submittedAt': '2026-05-02T00:00:00Z',
                'submittedBy': {'name': 'Ahmed', 'userId': 'u-1'},
              }
            ],
            'page': 1,
            'pageSize': 20,
            'totalCount': 1,
          });
      final gw = QuotesGatewayImpl(dio: pair.dio);
      final p = await gw.awaitingMyApproval();
      expect(p.items.single.submittedByName, 'Ahmed');
    });
  });

  group('getById', () {
    test('decodes versions, actions, lines, documents', () async {
      final pair = _build((_) => _detailPayload());
      final gw = QuotesGatewayImpl(dio: pair.dio);
      final d = await gw.getById('q-1');
      expect(d.versions, hasLength(1));
      expect(d.versions.single.lines.single.unitPrice, '15.00');
      expect(d.actions.canSubmitAcceptance, isTrue);
      expect(d.actions.canFinalizeAcceptance, isFalse);
      expect(d.versions.single.documents, hasLength(1));
    });
  });

  group('createFromCart', () {
    test('sends Idempotency-Key + body', () async {
      final pair = _build((_) => {
            'id': 'q-9',
            'quoteNumber': 'Q-9',
            'state': 'draft',
            'createdAt': '2026-05-01T00:00:00Z',
          });
      final gw = QuotesGatewayImpl(dio: pair.dio);
      final r = await gw.createFromCart(
        request: const CreateQuoteFromCartRequest(
          cartLines: [(productId: 'p-1', qty: 5)],
          terms: 'Net 30',
        ),
        idempotencyKey: 'cart-key-1',
      );
      expect(
        pair.stub.requests.single.headers[IdempotencyInterceptor.headerName],
        'cart-key-1',
      );
      expect(pair.stub.requests.single.path, '/api/customer/quotes/from-cart');
      expect(r.state, 'draft');
    });
  });

  group('createFromProduct', () {
    test('routes to /from-product with Idempotency-Key', () async {
      final pair = _build((_) => {
            'id': 'q-9',
            'quoteNumber': 'Q-9',
            'state': 'draft',
            'createdAt': '2026-05-01T00:00:00Z',
          });
      final gw = QuotesGatewayImpl(dio: pair.dio);
      await gw.createFromProduct(
        request: const CreateQuoteFromProductRequest(
          productId: 'p-1',
          qty: 100,
          terms: 'Net 30',
        ),
        idempotencyKey: 'prod-key-1',
      );
      expect(
        pair.stub.requests.single.path,
        '/api/customer/quotes/from-product',
      );
    });
  });

  group('actions', () {
    test('submitAcceptance posts to right path with key', () async {
      final pair =
          _build((_) => _detailPayload(state: 'awaiting_finalization'));
      final gw = QuotesGatewayImpl(dio: pair.dio);
      final d = await gw.submitAcceptance(
        quoteId: 'q-1',
        request: const QuoteActionNoteRequest(note: 'go'),
        idempotencyKey: 'sub-1',
      );
      expect(
        pair.stub.requests.single.path,
        '/api/customer/quotes/q-1/submit-acceptance',
      );
      expect(
        pair.stub.requests.single.headers[IdempotencyInterceptor.headerName],
        'sub-1',
      );
      expect(d.state, 'awaiting_finalization');
    });

    test('finalizeAcceptance routes correctly', () async {
      final pair = _build((_) => _detailPayload(state: 'accepted'));
      final gw = QuotesGatewayImpl(dio: pair.dio);
      await gw.finalizeAcceptance(
        quoteId: 'q-1',
        request: const QuoteActionNoteRequest(),
        idempotencyKey: 'fin-1',
      );
      expect(
        pair.stub.requests.single.path,
        '/api/customer/quotes/q-1/finalize-acceptance',
      );
    });

    test('rejectAcceptance routes correctly', () async {
      final pair = _build((_) => _detailPayload(state: 'rejected'));
      final gw = QuotesGatewayImpl(dio: pair.dio);
      await gw.rejectAcceptance(
        quoteId: 'q-1',
        request: const QuoteActionNoteRequest(note: 'too high'),
        idempotencyKey: 'rej-1',
      );
      expect(
        pair.stub.requests.single.path,
        '/api/customer/quotes/q-1/reject-acceptance',
      );
    });

    test('requestRevision + withdraw routes', () async {
      final pair = _build((_) => _detailPayload(state: 'draft'));
      final gw = QuotesGatewayImpl(dio: pair.dio);
      await gw.requestRevision(
        quoteId: 'q-1',
        request: const QuoteActionNoteRequest(note: 'price?'),
        idempotencyKey: 'rev-1',
      );
      expect(
        pair.stub.requests.single.path,
        '/api/customer/quotes/q-1/request-revision',
      );
    });

    test('saveAsTemplate returns templateId', () async {
      final pair = _build((_) => {'templateId': 'tpl-1'});
      final gw = QuotesGatewayImpl(dio: pair.dio);
      final r = await gw.saveAsTemplate(
        quoteId: 'q-1',
        request: const SaveAsTemplateRequest(templateName: 'Monthly'),
        idempotencyKey: 'tpl-key-1',
      );
      expect(r.templateId, 'tpl-1');
    });
  });

  group('downloadDocument', () {
    test('returns bytes from binary endpoint', () async {
      final pair = _build(
        (_) => <int>[0x25, 0x50, 0x44, 0x46], // %PDF
        responseType: ResponseType.bytes,
      );
      final gw = QuotesGatewayImpl(dio: pair.dio);
      final bytes = await gw.downloadDocument(
        quoteId: 'q-1',
        versionId: 'v1',
        locale: 'en',
      );
      expect(bytes, isA<Uint8List>());
      expect(bytes.length, 4);
      expect(
        pair.stub.requests.single.path,
        '/api/customer/quotes/q-1/versions/v1/documents/en',
      );
    });
  });
}
