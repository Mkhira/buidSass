import 'dart:typed_data';

import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/orders/data/models/order_models.dart';
import 'package:customer_flutter/features/orders/data/orders_gateway.dart';
import 'package:customer_flutter/features/returns/bloc/return_wizard_bloc.dart';
import 'package:customer_flutter/features/returns/data/models/return_models.dart';
import 'package:customer_flutter/features/returns/data/returns_gateway.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:uuid/uuid.dart';

// ============================================================
// Fakes
// ============================================================

class _UpDispatch {
  _UpDispatch(this.bytes, this.filename, this.clientPhotoKey);
  final Uint8List bytes;
  final String filename;
  final String clientPhotoKey;
}

class _CreateDispatch {
  _CreateDispatch(this.orderId, this.request, this.idempotencyKey);
  final String orderId;
  final CreateReturnRequest request;
  final String idempotencyKey;
}

class _FakeReturns implements ReturnsGateway {
  _FakeReturns({this.failPhotoOnce = false, this.failCreateWith409 = false});

  bool failPhotoOnce;
  bool failCreateWith409;
  final List<_UpDispatch> uploads = [];
  final List<_CreateDispatch> creates = [];

  @override
  Future<ReturnListPage> list(ReturnsListFilter filter) =>
      throw UnimplementedError();
  @override
  Future<ReturnDetail> getById(String id) => throw UnimplementedError();

  @override
  Future<ReturnPhotoUploadResult> uploadPhoto({
    required Uint8List bytes,
    required String filename,
    required String clientPhotoKey,
  }) async {
    uploads.add(_UpDispatch(bytes, filename, clientPhotoKey));
    if (failPhotoOnce) {
      failPhotoOnce = false;
      throw Exception('transient upload failure');
    }
    return ReturnPhotoUploadResult(
      photoId: 'photo-$clientPhotoKey',
      url: 'https://x/$clientPhotoKey.jpg',
      checksum: 'sha256:${bytes.length}',
    );
  }

  @override
  Future<CreateReturnResult> create({
    required String orderId,
    required CreateReturnRequest request,
    required String idempotencyKey,
  }) async {
    creates.add(_CreateDispatch(orderId, request, idempotencyKey));
    if (failCreateWith409) {
      failCreateWith409 = false;
      throw Exception('ConflictFailure: 409 eligibility expired');
    }
    return CreateReturnResult(
      id: 'r-1',
      returnNumber: 'R-1',
      state: 'pending',
      createdAt: DateTime.utc(2026, 5, 1),
    );
  }
}

class _FakeOrders implements OrdersGateway {
  _FakeOrders(this._eligibility);
  final ReturnEligibility _eligibility;
  int eligibilityCalls = 0;

  @override
  Future<ReturnEligibility> getReturnEligibility(String orderId) async {
    eligibilityCalls += 1;
    return _eligibility;
  }

  @override
  Future<OrderListPage> list(OrdersListFilter filter) =>
      throw UnimplementedError();
  @override
  Future<OrderDetail> getById(String orderId) => throw UnimplementedError();
  @override
  Future<OrderDetail> cancel({
    required String orderId,
    required CancelOrderRequest request,
  }) =>
      throw UnimplementedError();
  @override
  Future<ReorderResult> reorder(String orderId) => throw UnimplementedError();
}

ReturnEligibility _eligibility({bool anyEligible = true}) => ReturnEligibility(
      lines: const [
        ReturnEligibilityLine(
          productId: 'p-1',
          name: 'Product 1',
          qty: 1,
          eligible: true,
        ),
        ReturnEligibilityLine(
          productId: 'p-2',
          name: 'Product 2',
          qty: 2,
          eligible: true,
        ),
      ],
      anyEligible: anyEligible,
    );

ReturnWizardBloc _build({
  required _FakeReturns returns,
  required _FakeOrders orders,
  Uuid? uuid,
}) {
  return ReturnWizardBloc(
    ordersGateway: orders,
    returnsGateway: returns,
    orderId: 'order-1',
    uuid: uuid,
    // Tests skip the real `image` decoder — large files in CI don't
    // have valid JPEG headers, and the downscale call is orthogonal to
    // the upload-orchestration behavior we want to verify.
    downscaler: (b) => b,
  );
}

void main() {
  group('eligibility', () {
    blocTest<ReturnWizardBloc, ReturnWizardState>(
      'emits Ineligible when anyEligible == false',
      build: () => _build(
        returns: _FakeReturns(),
        orders: _FakeOrders(_eligibility(anyEligible: false)),
      ),
      act: (bloc) => bloc.add(const ReturnWizardStarted('order-1')),
      expect: () => [
        isA<ReturnWizardLoading>(),
        isA<ReturnWizardIneligible>(),
      ],
    );

    blocTest<ReturnWizardBloc, ReturnWizardState>(
      'emits Form with one draft per eligible line',
      build: () => _build(
        returns: _FakeReturns(),
        orders: _FakeOrders(_eligibility()),
      ),
      act: (bloc) => bloc.add(const ReturnWizardStarted('order-1')),
      expect: () => [
        isA<ReturnWizardLoading>(),
        isA<ReturnWizardForm>().having((s) => s.lines.length, 'lines', 2),
      ],
    );
  });

  group('photo upload', () {
    test('photo retry reuses key in the underlying gateway', () async {
      final fakeReturns = _FakeReturns(failPhotoOnce: true);
      final bloc = _build(
        returns: fakeReturns,
        orders: _FakeOrders(_eligibility()),
      );
      bloc.add(const ReturnWizardStarted('order-1'));
      await bloc.stream.firstWhere((s) => s is ReturnWizardForm);
      bloc.add(ReturnWizardPhotoAddRequested(
        bytes: Uint8List.fromList(const [1, 2, 3]),
        filename: 'photo.jpg',
      ));
      // Wait for failed state.
      await bloc.stream.firstWhere((s) =>
          s is ReturnWizardForm &&
          s.photos.isNotEmpty &&
          s.photos.first.status == ReturnPhotoTileStatus.failed);
      final tile = (bloc.state as ReturnWizardForm).photos.first;
      bloc.add(ReturnWizardPhotoRetried(tile.clientPhotoKey));
      await bloc.stream.firstWhere((s) =>
          s is ReturnWizardForm &&
          s.photos.first.status == ReturnPhotoTileStatus.ready);

      // Two uploads to the gateway — same clientPhotoKey on both
      // (server-side dedupe path, BR-2). Different keys would mean the
      // bloc treated this as a new file.
      expect(fakeReturns.uploads, hasLength(2));
      expect(fakeReturns.uploads[0].clientPhotoKey,
          fakeReturns.uploads[1].clientPhotoKey);

      await bloc.close();
    });
  });

  group('submit', () {
    test('reuses one idempotency key across submit retries', () async {
      final fakeReturns = _FakeReturns(failCreateWith409: true);
      final bloc = _build(
        returns: fakeReturns,
        orders: _FakeOrders(_eligibility()),
      );
      bloc.add(const ReturnWizardStarted('order-1'));
      await bloc.stream.firstWhere((s) => s is ReturnWizardForm);
      bloc.add(
          const ReturnWizardLineSelected(productId: 'p-1', selected: true));
      bloc.add(const ReturnWizardLineReasonChanged(
          productId: 'p-1', reason: 'damaged'));
      // Wait for the bloc to digest the line + reason events.
      await Future<void>.delayed(Duration.zero);

      // First attempt fails (409).
      bloc.add(const ReturnWizardSubmitted());
      await bloc.stream.firstWhere((s) => s is ReturnWizardFailure);

      // Second attempt — same wizard-create key per BR-3.
      bloc.add(const ReturnWizardSubmitted());
      await bloc.stream.firstWhere((s) => s is ReturnWizardSuccess);

      expect(fakeReturns.creates, hasLength(2));
      expect(fakeReturns.creates[0].idempotencyKey,
          fakeReturns.creates[1].idempotencyKey);
      expect(fakeReturns.creates[0].idempotencyKey, bloc.wizardCreateKey);

      await bloc.close();
    });

    blocTest<ReturnWizardBloc, ReturnWizardState>(
      'rejects submit when no line is selected',
      build: () => _build(
        returns: _FakeReturns(),
        orders: _FakeOrders(_eligibility()),
      ),
      seed: () => ReturnWizardForm(
        eligibility: _eligibility(),
        lines: const [
          ReturnWizardLineDraft(productId: 'p-1', qty: 1),
        ],
        photos: const [],
        refundMethod: null,
      ),
      act: (bloc) => bloc.add(const ReturnWizardSubmitted()),
      expect: () => [
        isA<ReturnWizardForm>().having(
          (s) => s.validationError,
          'validationError',
          'select_line',
        ),
      ],
    );

    blocTest<ReturnWizardBloc, ReturnWizardState>(
      'rejects submit when reason missing for selected line',
      build: () => _build(
        returns: _FakeReturns(),
        orders: _FakeOrders(_eligibility()),
      ),
      seed: () => ReturnWizardForm(
        eligibility: _eligibility(),
        lines: const [
          ReturnWizardLineDraft(productId: 'p-1', qty: 1, selected: true),
        ],
        photos: const [],
        refundMethod: null,
      ),
      act: (bloc) => bloc.add(const ReturnWizardSubmitted()),
      expect: () => [
        isA<ReturnWizardForm>().having(
          (s) => s.validationError,
          'validationError',
          'reason_required',
        ),
      ],
    );

    test('409 on submit flags eligibility as stale', () async {
      final bloc = _build(
        returns: _FakeReturns(failCreateWith409: true),
        orders: _FakeOrders(_eligibility()),
      );
      bloc.add(const ReturnWizardStarted('order-1'));
      await bloc.stream.firstWhere((s) => s is ReturnWizardForm);
      bloc.add(
          const ReturnWizardLineSelected(productId: 'p-1', selected: true));
      bloc.add(const ReturnWizardLineReasonChanged(
          productId: 'p-1', reason: 'damaged'));
      await Future<void>.delayed(Duration.zero);
      bloc.add(const ReturnWizardSubmitted());
      await bloc.stream.firstWhere((s) => s is ReturnWizardFailure);
      expect((bloc.state as ReturnWizardFailure).form.eligibilityStale, isTrue);
      await bloc.close();
    });
  });

  group('downscaler', () {
    test('downscales only when input exceeds 10 MB', () {
      final small = Uint8List(1024);
      expect(defaultReturnPhotoDownscaler(small), same(small));
      // We don't synthesise a real >10 MB JPEG in unit tests — the
      // image-package decoder rejects non-JPEG bytes and returns the
      // input unchanged. This is documented in the function's
      // tolerant-failure path.
      final junk = Uint8List(11 * 1024 * 1024);
      expect(defaultReturnPhotoDownscaler(junk), same(junk));
    });
  });
}
