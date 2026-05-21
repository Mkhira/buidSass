import 'dart:typed_data';

import 'package:customer_flutter/core/api/idempotency_interceptor.dart';
import 'package:customer_flutter/features/verification/data/models/verification_models.dart';
import 'package:customer_flutter/features/verification/data/verification_gateway_impl.dart';
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
  group('list', () {
    test('parses items + states', () async {
      final pair = _build((_) => {
            'items': [
              {
                'id': 'v-1',
                'kind': 'business_license',
                'state': 'approved',
                'createdAt': '2026-05-01T00:00:00Z',
                'expiresAt': '2027-05-01T00:00:00Z',
              }
            ]
          });
      final gw = VerificationGatewayImpl(dio: pair.dio);
      final page = await gw.list();
      expect(page.items.single.state, 'approved');
      expect(page.items.single.expiresAt?.year, 2027);
    });
  });

  group('active', () {
    test('returns hasCase=false when state=none', () async {
      final pair = _build((_) => {'state': 'none'});
      final gw = VerificationGatewayImpl(dio: pair.dio);
      final a = await gw.getActive();
      expect(a.hasCase, isFalse);
    });
  });

  group('schema', () {
    test('decodes fields + document slots', () async {
      final pair = _build((_) => {
            'kind': 'business_license',
            'fields': [
              {
                'key': 'businessLicense',
                'label': 'License',
                'type': 'text',
                'required': true,
                'validation': {'regex': r'\d+'},
              },
              {
                'key': 'specialty',
                'label': 'Specialty',
                'type': 'enum',
                'required': true,
                'options': ['general', 'ortho'],
              },
            ],
            'documentSlots': [
              {'key': 'id_front', 'label': 'ID Front', 'required': true},
            ],
          });
      final gw = VerificationGatewayImpl(dio: pair.dio);
      final s = await gw.getSchema();
      expect(s.fields.first.type, 'text');
      expect(s.fields.first.validation?.regex, r'\d+');
      expect(s.fields[1].options, ['general', 'ortho']);
      expect(s.documentSlots.single.required, isTrue);
    });
  });

  group('getById', () {
    test('decodes timeline + requestedInfo + documents', () async {
      final pair = _build((_) => {
            'id': 'v-9',
            'state': 'info_requested',
            'kind': 'business_license',
            'createdAt': '2026-05-01T00:00:00Z',
            'fields': {'businessLicense': 'AB-1'},
            'documents': [
              {
                'slotKey': 'id_front',
                'url': 'https://x/id_front.jpg',
                'uploadedAt': '2026-05-02T00:00:00Z',
              }
            ],
            'requestedInfo': [
              {'kind': 'doc', 'key': 'id_back', 'note': 'blurry'}
            ],
            'timeline': [
              {
                'kind': 'submitted',
                'occurredAt': '2026-05-01T00:00:00Z',
                'actor': 'customer'
              },
              {
                'kind': 'info_requested',
                'occurredAt': '2026-05-02T00:00:00Z',
                'actor': 'admin'
              },
            ],
          });
      final gw = VerificationGatewayImpl(dio: pair.dio);
      final d = await gw.getById('v-9');
      expect(d.state, 'info_requested');
      expect(d.documents.single.slotKey, 'id_front');
      expect(d.requestedInfo.single.kind, 'doc');
      expect(d.timeline, hasLength(2));
    });
  });

  group('submit', () {
    test('sends Idempotency-Key + kind + fields', () async {
      final pair = _build((_) => {
            'id': 'v-9',
            'state': 'submitted',
            'createdAt': '2026-05-01T00:00:00Z',
          });
      final gw = VerificationGatewayImpl(dio: pair.dio);
      final r = await gw.submit(
        request: const SubmitVerificationRequest(
          kind: 'business_license',
          marketCode: 'SA',
          fields: {'businessLicense': 'AB-1'},
        ),
        idempotencyKey: 'submit-key-1',
      );
      expect(
        pair.stub.requests.single.headers[IdempotencyInterceptor.headerName],
        'submit-key-1',
      );
      expect(pair.stub.requests.single.path, '/api/customer/verifications');
      expect(r.id, 'v-9');
    });
  });

  group('uploadDocument', () {
    test('routes per slot and sends multipart', () async {
      final pair = _build((_) => {
            'slotKey': 'id_front',
            'url': 'https://x/id_front.jpg',
            'uploadedAt': '2026-05-02T00:00:00Z',
          });
      final gw = VerificationGatewayImpl(dio: pair.dio);
      final r = await gw.uploadDocument(
        verificationId: 'v-9',
        slotKey: 'id_front',
        bytes: Uint8List.fromList(const [1, 2, 3]),
        filename: 'id.jpg',
      );
      expect(
        pair.stub.requests.single.path,
        '/api/customer/verifications/v-9/documents',
      );
      expect(
        pair.stub.requests.single.headers['content-type']?.toString() ?? '',
        startsWith('multipart/form-data'),
      );
      expect(r.slotKey, 'id_front');
    });
  });

  group('resubmit', () {
    test('sends Idempotency-Key + scoped fields', () async {
      final pair = _build((_) => {
            'id': 'v-9',
            'state': 'submitted',
            'kind': 'business_license',
            'createdAt': '2026-05-01T00:00:00Z',
            'fields': {'vat': '300'},
            'documents': [],
            'requestedInfo': [],
            'timeline': [],
          });
      final gw = VerificationGatewayImpl(dio: pair.dio);
      final d = await gw.resubmit(
        verificationId: 'v-9',
        request: const ResubmitVerificationRequest(
          fields: {'vat': '300'},
          noteToAdmin: 'Fixed.',
        ),
        idempotencyKey: 'resubmit-key-1',
      );
      expect(
        pair.stub.requests.single.headers[IdempotencyInterceptor.headerName],
        'resubmit-key-1',
      );
      expect(
        pair.stub.requests.single.path,
        '/api/customer/verifications/v-9/resubmit',
      );
      expect(d.id, 'v-9');
    });
  });

  group('renew', () {
    test('sends Idempotency-Key + priorVerificationId', () async {
      final pair = _build((_) => {
            'id': 'v-new',
            'state': 'submitted',
            'createdAt': '2026-05-01T00:00:00Z',
          });
      final gw = VerificationGatewayImpl(dio: pair.dio);
      final r = await gw.renew(
        request: const RenewVerificationRequest(
          priorVerificationId: 'v-prior',
          marketCode: 'SA',
        ),
        idempotencyKey: 'renew-key-1',
      );
      expect(
        pair.stub.requests.single.headers[IdempotencyInterceptor.headerName],
        'renew-key-1',
      );
      expect(
          pair.stub.requests.single.path, '/api/customer/verifications/renew');
      expect(r.id, 'v-new');
    });
  });
}
