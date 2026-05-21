import 'dart:async';
import 'dart:typed_data';

import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/verification/bloc/verification_detail_bloc.dart';
import 'package:customer_flutter/features/verification/data/models/verification_models.dart';
import 'package:customer_flutter/features/verification/data/verification_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeGateway implements VerificationGateway {
  _FakeGateway(
      {this.detail, this.throwOnLoad = false, this.throwOnUpload = false});

  VerificationDetail? detail;
  bool throwOnLoad;
  bool throwOnUpload;

  /// Number of currently in-flight uploads. The test asserts that this
  /// never exceeds the bloc's concurrency cap.
  int inFlight = 0;
  int peakInFlight = 0;

  /// Completers per slot — tests fulfil them to unblock simulated
  /// uploads in a controlled order.
  final Map<String, Completer<void>> gates = {};

  @override
  Future<VerificationDetail> getById(String id) async {
    if (throwOnLoad) throw Exception('boom');
    return detail!;
  }

  @override
  Future<DocumentUploadResult> uploadDocument({
    required String verificationId,
    required String slotKey,
    required Uint8List bytes,
    required String filename,
  }) async {
    inFlight++;
    if (inFlight > peakInFlight) peakInFlight = inFlight;
    final gate = gates[slotKey] ?? Completer<void>();
    gates[slotKey] = gate;
    try {
      await gate.future;
      if (throwOnUpload) throw Exception('upload failed');
      return DocumentUploadResult(
        slotKey: slotKey,
        url: 'https://x/$slotKey.jpg',
        uploadedAt: DateTime.utc(2026, 5, 20),
      );
    } finally {
      inFlight--;
    }
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

VerificationDetail _detail({
  String state = 'info_requested',
  List<VerificationDocument> documents = const [],
  List<VerificationRequestedInfo> requestedInfo = const [],
  Map<String, Object?> fields = const {},
}) {
  return VerificationDetail(
    id: 'v-1',
    state: state,
    kind: 'business_license',
    createdAt: DateTime.utc(2026, 5, 1),
    fields: fields,
    documents: documents,
    requestedInfo: requestedInfo,
    timeline: const [],
  );
}

void main() {
  group('load', () {
    blocTest<VerificationDetailBloc, VerificationDetailState>(
      'success → loaded',
      build: () {
        return VerificationDetailBloc(
          gateway: _FakeGateway(detail: _detail()),
          verificationId: 'v-1',
        );
      },
      act: (b) => b.add(const VerificationDetailStarted()),
      expect: () => [
        isA<VerificationDetailLoading>(),
        isA<VerificationDetailLoaded>(),
      ],
    );

    blocTest<VerificationDetailBloc, VerificationDetailState>(
      'failure on initial load → failure state',
      build: () => VerificationDetailBloc(
        gateway: _FakeGateway(throwOnLoad: true),
        verificationId: 'v-1',
      ),
      act: (b) => b.add(const VerificationDetailStarted()),
      expect: () => [
        isA<VerificationDetailLoading>(),
        isA<VerificationDetailFailure>(),
      ],
    );
  });

  group('upload', () {
    test('bounded concurrency caps in-flight at maxConcurrentUploads',
        () async {
      final gw = _FakeGateway(detail: _detail());
      final bloc = VerificationDetailBloc(
        gateway: gw,
        verificationId: 'v-1',
        maxConcurrentUploads: 2,
      );
      bloc.add(const VerificationDetailStarted());
      await bloc.stream.firstWhere((s) => s is VerificationDetailLoaded);

      for (final slot in ['a', 'b', 'c', 'd']) {
        gw.gates[slot] = Completer<void>();
        bloc.add(VerificationDocumentUploadRequested(
          slotKey: slot,
          bytes: Uint8List.fromList(const [1]),
          filename: '$slot.jpg',
        ));
      }
      // Yield to the event queue so the first batch starts.
      await Future<void>.delayed(const Duration(milliseconds: 50));
      expect(gw.peakInFlight, lessThanOrEqualTo(2));
      // Release first two gates → next two should proceed; peak still ≤ 2.
      gw.gates['a']!.complete();
      gw.gates['b']!.complete();
      await Future<void>.delayed(const Duration(milliseconds: 50));
      expect(gw.peakInFlight, lessThanOrEqualTo(2));
      gw.gates['c']!.complete();
      gw.gates['d']!.complete();
      await Future<void>.delayed(const Duration(milliseconds: 50));
      await bloc.close();
    });

    test('upload success splices the document into detail', () async {
      final gw = _FakeGateway(
          detail: _detail(
        requestedInfo: const [
          VerificationRequestedInfo(kind: 'doc', key: 'id_back'),
        ],
      ));
      final bloc = VerificationDetailBloc(
        gateway: gw,
        verificationId: 'v-1',
      );
      bloc.add(const VerificationDetailStarted());
      await bloc.stream.firstWhere((s) => s is VerificationDetailLoaded);

      gw.gates['id_back'] = Completer<void>()..complete();
      bloc.add(VerificationDocumentUploadRequested(
        slotKey: 'id_back',
        bytes: Uint8List.fromList(const [1]),
        filename: 'id_back.jpg',
      ));
      // Wait until ready
      final loaded = await bloc.stream.firstWhere((s) {
        if (s is! VerificationDetailLoaded) return false;
        final up = s.uploads['id_back'];
        return up?.status == SlotUploadStatus.ready;
      }) as VerificationDetailLoaded;
      expect(
        loaded.detail.documents.where((d) => d.slotKey == 'id_back'),
        hasLength(1),
      );
      expect(loaded.resubmitReady, isTrue);
      await bloc.close();
    });

    test('upload failure surfaces failed status + retains values', () async {
      final gw = _FakeGateway(
        detail: _detail(),
        throwOnUpload: true,
      );
      final bloc = VerificationDetailBloc(
        gateway: gw,
        verificationId: 'v-1',
      );
      bloc.add(const VerificationDetailStarted());
      await bloc.stream.firstWhere((s) => s is VerificationDetailLoaded);

      gw.gates['id_back'] = Completer<void>()..complete();
      bloc.add(VerificationDocumentUploadRequested(
        slotKey: 'id_back',
        bytes: Uint8List.fromList(const [1]),
        filename: 'id_back.jpg',
      ));
      final loaded = await bloc.stream.firstWhere((s) {
        if (s is! VerificationDetailLoaded) return false;
        return s.uploads['id_back']?.status == SlotUploadStatus.failed;
      }) as VerificationDetailLoaded;
      expect(loaded.uploads['id_back']?.errorMessage, isNotNull);
      await bloc.close();
    });
  });

  group('resubmitReady', () {
    test('all requested items satisfied → true', () {
      final loaded = VerificationDetailLoaded(
        detail: _detail(
          requestedInfo: const [
            VerificationRequestedInfo(kind: 'doc', key: 'id_back'),
            VerificationRequestedInfo(kind: 'field', key: 'vat'),
          ],
          documents: [
            VerificationDocument(
              slotKey: 'id_back',
              url: 'x',
              uploadedAt: DateTime.utc(2026, 5, 1),
            ),
          ],
          fields: const {'vat': '300'},
        ),
        uploads: const {},
      );
      expect(loaded.resubmitReady, isTrue);
    });

    test('missing doc → false', () {
      final loaded = VerificationDetailLoaded(
        detail: _detail(
          requestedInfo: const [
            VerificationRequestedInfo(kind: 'doc', key: 'id_back'),
          ],
        ),
        uploads: const {},
      );
      expect(loaded.resubmitReady, isFalse);
    });

    test('empty requestedInfo → false (nothing to act on)', () {
      final loaded = VerificationDetailLoaded(
        detail: _detail(requestedInfo: const []),
        uploads: const {},
      );
      expect(loaded.resubmitReady, isFalse);
    });
  });
}
