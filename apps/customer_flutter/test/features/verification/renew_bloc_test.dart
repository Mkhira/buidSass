import 'dart:typed_data';

import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/verification/bloc/renew_bloc.dart';
import 'package:customer_flutter/features/verification/data/models/verification_models.dart';
import 'package:customer_flutter/features/verification/data/verification_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeGateway implements VerificationGateway {
  _FakeGateway({this.throwOnLoad = false, this.throwOnRenew = false});

  bool throwOnLoad;
  bool throwOnRenew;
  String? lastIdempotencyKey;
  RenewVerificationRequest? lastRequest;

  @override
  Future<VerificationDetail> getById(String id) async {
    if (throwOnLoad) throw Exception('load failed');
    return VerificationDetail(
      id: id,
      state: 'approved',
      kind: 'business_license',
      createdAt: DateTime.utc(2025, 5, 1),
      fields: const {'businessLicense': 'AB-1'},
      documents: const [],
      requestedInfo: const [],
      timeline: const [],
    );
  }

  @override
  Future<SubmitVerificationResult> renew({
    required RenewVerificationRequest request,
    required String idempotencyKey,
  }) async {
    lastRequest = request;
    lastIdempotencyKey = idempotencyKey;
    if (throwOnRenew) throw Exception('renew failed');
    return SubmitVerificationResult(
      id: 'v-new',
      state: 'submitted',
      createdAt: DateTime.utc(2026, 5, 1),
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
  Future<VerificationDetail> resubmit({
    required String verificationId,
    required ResubmitVerificationRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
}

void main() {
  blocTest<RenewBloc, RenewState>(
    'started → ready with prior detail',
    build: () => RenewBloc(
      gateway: _FakeGateway(),
      idempotencyKeyFactory: () => 'k1',
    ),
    act: (b) => b.add(const RenewStarted(
      priorVerificationId: 'v-prior',
      marketCode: 'SA',
    )),
    expect: () => [
      isA<RenewLoading>(),
      isA<RenewReady>()
          .having((s) => s.prior.id, 'prior.id', 'v-prior')
          .having((s) => s.marketCode, 'marketCode', 'SA'),
    ],
  );

  blocTest<RenewBloc, RenewState>(
    'load failure → load failure',
    build: () => RenewBloc(gateway: _FakeGateway(throwOnLoad: true)),
    act: (b) => b.add(const RenewStarted(
      priorVerificationId: 'v-prior',
      marketCode: 'SA',
    )),
    expect: () => [
      isA<RenewLoading>(),
      isA<RenewLoadFailure>(),
    ],
  );

  blocTest<RenewBloc, RenewState>(
    'happy path: submit sends Idempotency-Key + reaches done',
    build: () => RenewBloc(
      gateway: _FakeGateway(),
      idempotencyKeyFactory: () => 'wizard-renew-1',
    ),
    act: (b) async {
      b.add(const RenewStarted(
        priorVerificationId: 'v-prior',
        marketCode: 'SA',
      ));
      await Future<void>.delayed(Duration.zero);
      b.add(const RenewSubmitted());
    },
    skip: 2,
    expect: () => [
      isA<RenewSubmitting>(),
      isA<RenewDone>().having((s) => s.result.id, 'id', 'v-new'),
    ],
    verify: (b) {
      expect(b.idempotencyKey, 'wizard-renew-1');
    },
  );

  blocTest<RenewBloc, RenewState>(
    'submit failure → ready + formError',
    build: () => RenewBloc(gateway: _FakeGateway(throwOnRenew: true)),
    act: (b) async {
      b.add(const RenewStarted(
        priorVerificationId: 'v-prior',
        marketCode: 'SA',
      ));
      await Future<void>.delayed(Duration.zero);
      b.add(const RenewSubmitted());
    },
    skip: 2,
    expect: () => [
      isA<RenewSubmitting>(),
      isA<RenewReady>().having((s) => s.formError, 'formError', isNotNull),
    ],
  );
}
