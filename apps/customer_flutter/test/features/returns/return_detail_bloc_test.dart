import 'dart:typed_data';

import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/returns/bloc/return_detail_bloc.dart';
import 'package:customer_flutter/features/returns/data/models/return_models.dart';
import 'package:customer_flutter/features/returns/data/returns_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

class _Gateway implements ReturnsGateway {
  _Gateway(this._detail);
  final ReturnDetail _detail;

  @override
  Future<ReturnListPage> list(ReturnsListFilter filter) =>
      throw UnimplementedError();
  @override
  Future<ReturnDetail> getById(String id) async => _detail;
  @override
  Future<ReturnPhotoUploadResult> uploadPhoto({
    required Uint8List bytes,
    required String filename,
    required String clientPhotoKey,
  }) =>
      throw UnimplementedError();
  @override
  Future<CreateReturnResult> create({
    required String orderId,
    required CreateReturnRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
}

class _ErrGateway implements ReturnsGateway {
  @override
  Future<ReturnListPage> list(ReturnsListFilter filter) =>
      throw UnimplementedError();
  @override
  Future<ReturnDetail> getById(String id) async => throw Exception('boom');
  @override
  Future<ReturnPhotoUploadResult> uploadPhoto({
    required Uint8List bytes,
    required String filename,
    required String clientPhotoKey,
  }) =>
      throw UnimplementedError();
  @override
  Future<CreateReturnResult> create({
    required String orderId,
    required CreateReturnRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
}

ReturnDetail _detail() => ReturnDetail(
      id: 'r-1',
      returnNumber: 'R-1',
      orderId: 'o-1',
      orderNumber: '2026-1',
      state: 'rejected',
      timeline: [
        ReturnTimelineEvent(
          kind: 'created',
          occurredAt: DateTime.utc(2026, 5, 1),
          actor: 'customer',
        ),
        ReturnTimelineEvent(
          kind: 'rejected',
          occurredAt: DateTime.utc(2026, 5, 2),
          actor: 'admin',
          note: 'Outside policy',
        ),
      ],
      lines: const [
        ReturnDetailLine(
          productId: 'p-1',
          name: 'A',
          qty: 1,
          reason: 'damaged',
          photos: [ReturnPhoto(url: 'https://x/1.jpg')],
        ),
      ],
      rejection: const ReturnRejection(
        reason: 'outside_window',
        noteToCustomer: 'Window expired',
      ),
    );

void main() {
  blocTest<ReturnDetailBloc, ReturnDetailState>(
    'loads detail on Started',
    build: () =>
        ReturnDetailBloc(gateway: _Gateway(_detail()), returnId: 'r-1'),
    act: (bloc) => bloc.add(const ReturnDetailStarted()),
    expect: () => [
      isA<ReturnDetailLoading>(),
      isA<ReturnDetailLoaded>().having(
        (s) => s.detail.rejection?.reason,
        'rejection.reason',
        'outside_window',
      ),
    ],
  );

  blocTest<ReturnDetailBloc, ReturnDetailState>(
    'surfaces Failure on gateway error',
    build: () => ReturnDetailBloc(gateway: _ErrGateway(), returnId: 'r-1'),
    act: (bloc) => bloc.add(const ReturnDetailStarted()),
    expect: () => [
      isA<ReturnDetailLoading>(),
      isA<ReturnDetailFailure>(),
    ],
  );
}
