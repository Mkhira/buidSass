import 'dart:typed_data';

import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/verification/bloc/verification_list_bloc.dart';
import 'package:customer_flutter/features/verification/data/models/verification_models.dart';
import 'package:customer_flutter/features/verification/data/verification_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeGateway implements VerificationGateway {
  _FakeGateway({this.active, this.items = const [], this.throwOnLoad = false});
  final VerificationActive? active;
  final List<VerificationListItem> items;
  final bool throwOnLoad;

  @override
  Future<VerificationActive> getActive() async {
    if (throwOnLoad) throw Exception('boom');
    return active ?? const VerificationActive(state: 'none');
  }

  @override
  Future<VerificationListPage> list() async {
    if (throwOnLoad) throw Exception('boom');
    return VerificationListPage(items: items);
  }

  // ---- unused in this bloc; concrete throws to surface accidental use ----

  @override
  Future<VerificationSchema> getSchema() => throw UnimplementedError();

  @override
  Future<VerificationDetail> getById(String id) => throw UnimplementedError();

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

  @override
  Future<SubmitVerificationResult> renew({
    required RenewVerificationRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
}

void main() {
  blocTest<VerificationListBloc, VerificationListState>(
    'started → loading → loaded with active banner + items',
    build: () => VerificationListBloc(
      gateway: _FakeGateway(
        active: const VerificationActive(
          id: 'v-1',
          state: 'info_requested',
          kind: 'business_license',
        ),
        items: [
          VerificationListItem(
            id: 'v-1',
            kind: 'business_license',
            state: 'info_requested',
            createdAt: DateTime.utc(2026, 5, 1),
          ),
        ],
      ),
    ),
    act: (b) => b.add(const VerificationListStarted()),
    expect: () => [
      isA<VerificationListLoading>(),
      isA<VerificationListLoaded>()
          .having((s) => s.active.id, 'active.id', 'v-1')
          .having((s) => s.items, 'items', hasLength(1)),
    ],
  );

  blocTest<VerificationListBloc, VerificationListState>(
    'empty active + empty list → loaded.hasAny=false',
    build: () => VerificationListBloc(gateway: _FakeGateway()),
    act: (b) => b.add(const VerificationListStarted()),
    expect: () => [
      isA<VerificationListLoading>(),
      isA<VerificationListLoaded>().having((s) => s.hasAny, 'hasAny', isFalse),
    ],
  );

  blocTest<VerificationListBloc, VerificationListState>(
    'gateway throws → failure',
    build: () => VerificationListBloc(gateway: _FakeGateway(throwOnLoad: true)),
    act: (b) => b.add(const VerificationListStarted()),
    expect: () => [
      isA<VerificationListLoading>(),
      isA<VerificationListFailure>(),
    ],
  );
}
