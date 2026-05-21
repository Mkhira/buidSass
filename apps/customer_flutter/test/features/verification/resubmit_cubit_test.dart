import 'dart:typed_data';

import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/verification/bloc/resubmit_cubit.dart';
import 'package:customer_flutter/features/verification/data/models/verification_models.dart';
import 'package:customer_flutter/features/verification/data/verification_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeGateway implements VerificationGateway {
  _FakeGateway({required this.detail, this.throwOnResubmit = false});

  VerificationDetail detail;
  bool throwOnResubmit;

  ResubmitVerificationRequest? lastRequest;
  String? lastIdempotencyKey;

  @override
  Future<VerificationDetail> getById(String id) async => detail;

  @override
  Future<VerificationDetail> resubmit({
    required String verificationId,
    required ResubmitVerificationRequest request,
    required String idempotencyKey,
  }) async {
    lastRequest = request;
    lastIdempotencyKey = idempotencyKey;
    if (throwOnResubmit) throw Exception('boom');
    return VerificationDetail(
      id: detail.id,
      state: 'submitted',
      kind: detail.kind,
      createdAt: detail.createdAt,
      fields: {...detail.fields, ...request.fields},
      documents: detail.documents,
      requestedInfo: const [],
      timeline: detail.timeline,
    );
  }

  // -- unused --
  @override
  Future<VerificationActive> getActive() async => throw UnimplementedError();
  @override
  Future<VerificationListPage> list() async => throw UnimplementedError();
  @override
  Future<VerificationSchema> getSchema() => throw UnimplementedError();
  @override
  Future<SubmitVerificationResult> submit({
    required SubmitVerificationRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
  @override
  Future<DocumentUploadResult> uploadDocument({
    required String verificationId,
    required String slotKey,
    required Uint8List bytes,
    required String filename,
  }) =>
      throw UnimplementedError();
  @override
  Future<SubmitVerificationResult> renew({
    required RenewVerificationRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
}

VerificationDetail _detail({
  List<VerificationRequestedInfo> requestedInfo = const [],
}) {
  return VerificationDetail(
    id: 'v-1',
    state: 'info_requested',
    kind: 'business_license',
    createdAt: DateTime.utc(2026, 5, 1),
    fields: const {'businessLicense': 'AB-1'},
    documents: const [],
    requestedInfo: requestedInfo,
    timeline: const [],
  );
}

void main() {
  blocTest<ResubmitCubit, ResubmitState>(
    'load → form scoped to field requestedInfo (docs filtered out)',
    build: () => ResubmitCubit(
      gateway: _FakeGateway(
        detail: _detail(requestedInfo: const [
          VerificationRequestedInfo(kind: 'field', key: 'vat'),
          VerificationRequestedInfo(kind: 'doc', key: 'id_back'),
        ]),
      ),
      verificationId: 'v-1',
      idempotencyKeyFactory: () => 'k1',
    ),
    act: (c) => c.load(),
    expect: () => [
      isA<ResubmitLoading>(),
      isA<ResubmitForm>().having(
        (s) => s.editableFields.length,
        'editableFields',
        1,
      ),
    ],
  );

  blocTest<ResubmitCubit, ResubmitState>(
    'fieldChanged rejects edits outside requestedInfo scope',
    build: () => ResubmitCubit(
      gateway: _FakeGateway(
        detail: _detail(requestedInfo: const [
          VerificationRequestedInfo(kind: 'field', key: 'vat'),
        ]),
      ),
      verificationId: 'v-1',
      idempotencyKeyFactory: () => 'k1',
    ),
    act: (c) async {
      await c.load();
      c.fieldChanged('businessLicense', 'CD-2'); // out of scope
      c.fieldChanged('vat', '300');
    },
    skip: 2,
    expect: () => [
      isA<ResubmitForm>().having((s) => s.values, 'values', {'vat': '300'}),
    ],
  );

  blocTest<ResubmitCubit, ResubmitState>(
    'submit blocks when any requested field is empty',
    build: () => ResubmitCubit(
      gateway: _FakeGateway(
        detail: _detail(requestedInfo: const [
          VerificationRequestedInfo(kind: 'field', key: 'vat'),
        ]),
      ),
      verificationId: 'v-1',
      idempotencyKeyFactory: () => 'k1',
    ),
    act: (c) async {
      await c.load();
      await c.submit();
    },
    skip: 2,
    expect: () => [
      isA<ResubmitForm>().having((s) => s.formError, 'formError', isNotNull),
    ],
  );

  blocTest<ResubmitCubit, ResubmitState>(
    'happy path → submits with Idempotency-Key + reaches done',
    build: () => ResubmitCubit(
      gateway: _FakeGateway(
        detail: _detail(requestedInfo: const [
          VerificationRequestedInfo(kind: 'field', key: 'vat'),
        ]),
      ),
      verificationId: 'v-1',
      idempotencyKeyFactory: () => 'wizard-key-1',
    ),
    act: (c) async {
      await c.load();
      c.fieldChanged('vat', '300');
      c.noteChanged('fixed it');
      await c.submit();
    },
    // Skip: Loading, Form-after-load, Form-with-vat, Form-with-note.
    skip: 4,
    expect: () => [
      isA<ResubmitSubmitting>(),
      isA<ResubmitDone>()
          .having((s) => s.detail.state, 'state', 'submitted'),
    ],
    verify: (c) {
      expect(c.idempotencyKey, 'wizard-key-1');
    },
  );

  blocTest<ResubmitCubit, ResubmitState>(
    'gateway throws → form retains values + formError set',
    build: () => ResubmitCubit(
      gateway: _FakeGateway(
        detail: _detail(requestedInfo: const [
          VerificationRequestedInfo(kind: 'field', key: 'vat'),
        ]),
        throwOnResubmit: true,
      ),
      verificationId: 'v-1',
      idempotencyKeyFactory: () => 'k1',
    ),
    act: (c) async {
      await c.load();
      c.fieldChanged('vat', '300');
      await c.submit();
    },
    skip: 2,
    expect: () => [
      isA<ResubmitForm>().having((s) => s.values['vat'], 'vat', '300'),
      isA<ResubmitSubmitting>(),
      isA<ResubmitForm>()
          .having((s) => s.values['vat'], 'preserved', '300')
          .having((s) => s.formError, 'formError', isNotNull),
    ],
  );
}
