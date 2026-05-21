import 'dart:typed_data';

import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/b2b/bloc/quote_detail_bloc.dart';
import 'package:customer_flutter/features/b2b/data/models/quote_models.dart';
import 'package:customer_flutter/features/b2b/data/quotes_gateway.dart';
import 'package:customer_flutter/features/b2b/widgets/quote_actions_toolbar.dart';
import 'package:flutter_test/flutter_test.dart';

QuoteDetail _detail({
  String state = 'awaiting_acceptance',
  QuoteActions actions = const QuoteActions(
    canSubmitAcceptance: true,
    canRejectAcceptance: true,
    canWithdraw: true,
  ),
}) {
  return QuoteDetail(
    id: 'q-1',
    quoteNumber: 'Q-1',
    state: state,
    versions: const [],
    actions: actions,
  );
}

class _FakeGateway implements QuotesGateway {
  _FakeGateway({this.throw409Once = false});

  QuoteDetail? detail;
  Object? throwOnAction;
  bool throw409Once;

  String? lastActionPath;
  String? lastIdempotencyKey;
  final List<String> idempotencyKeys = [];

  @override
  Future<QuoteDetail> getById(String id) async => detail ?? _detail();

  Future<QuoteDetail> _act(String path, String key) async {
    lastActionPath = path;
    lastIdempotencyKey = key;
    idempotencyKeys.add(key);
    if (throw409Once) {
      throw409Once = false;
      throw Exception('http.409 conflict');
    }
    if (throwOnAction != null) throw throwOnAction!;
    return detail ?? _detail(state: 'accepted', actions: const QuoteActions());
  }

  @override
  Future<QuoteDetail> submitAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _act('submit', idempotencyKey);

  @override
  Future<QuoteDetail> finalizeAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _act('finalize', idempotencyKey);

  @override
  Future<QuoteDetail> rejectAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _act('reject', idempotencyKey);

  @override
  Future<QuoteDetail> requestRevision({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _act('revision', idempotencyKey);

  @override
  Future<QuoteDetail> withdraw({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      _act('withdraw', idempotencyKey);

  @override
  Future<SaveAsTemplateResult> saveAsTemplate({
    required String quoteId,
    required SaveAsTemplateRequest request,
    required String idempotencyKey,
  }) async {
    lastActionPath = 'template';
    lastIdempotencyKey = idempotencyKey;
    return const SaveAsTemplateResult(templateId: 'tpl-1');
  }

  // unused
  @override
  Future<QuotesPage> list(QuotesFilter filter) => Future.value(
      const QuotesPage(items: [], page: 1, pageSize: 20, totalCount: 0));

  @override
  Future<QuotesPage> awaitingMyApproval() => Future.value(
      const QuotesPage(items: [], page: 1, pageSize: 20, totalCount: 0));

  @override
  Future<CreateQuoteResult> createFromCart({
    required CreateQuoteFromCartRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<CreateQuoteResult> createFromProduct({
    required CreateQuoteFromProductRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<Uint8List> downloadDocument({
    required String quoteId,
    required String versionId,
    required String locale,
  }) =>
      throw UnimplementedError();
}

void main() {
  blocTest<QuoteDetailBloc, QuoteDetailState>(
    'started → loaded',
    build: () => QuoteDetailBloc(
      gateway: _FakeGateway(),
      quoteId: 'q-1',
    ),
    act: (b) => b.add(const QuoteDetailStarted()),
    expect: () => [
      isA<QuoteDetailLoading>(),
      isA<QuoteDetailLoaded>(),
    ],
  );

  blocTest<QuoteDetailBloc, QuoteDetailState>(
    'action busy state cycles through busy → not-busy',
    build: () => QuoteDetailBloc(
      gateway: _FakeGateway(),
      quoteId: 'q-1',
      idempotencyKeyFactory: () => 'sub-1',
    ),
    act: (b) async {
      b.add(const QuoteDetailStarted());
      await Future<void>.delayed(Duration.zero);
      b.add(const QuoteDetailActionRequested(
        kind: QuoteActionKind.submitAcceptance,
      ));
    },
    skip: 2,
    expect: () => [
      isA<QuoteDetailLoaded>().having(
          (s) => s.busyAction, 'busy', QuoteActionKind.submitAcceptance),
      isA<QuoteDetailLoaded>().having((s) => s.busyAction, 'busy', isNull),
    ],
  );

  blocTest<QuoteDetailBloc, QuoteDetailState>(
    'each successive action runs sequentially, never overlapping',
    build: () => QuoteDetailBloc(
      gateway: _FakeGateway(),
      quoteId: 'q-1',
    ),
    act: (b) async {
      b.add(const QuoteDetailStarted());
      await Future<void>.delayed(Duration.zero);
      b.add(const QuoteDetailActionRequested(
        kind: QuoteActionKind.submitAcceptance,
      ));
      b.add(const QuoteDetailActionRequested(
        kind: QuoteActionKind.withdraw,
      ));
    },
    skip: 2,
    // Bloc event handling is sequential by default, so the second
    // action queues behind the first. We verify there's exactly one
    // busy state per action and busy never holds two kinds at once.
    expect: () => [
      isA<QuoteDetailLoaded>().having(
          (s) => s.busyAction, 'busy', QuoteActionKind.submitAcceptance),
      isA<QuoteDetailLoaded>().having((s) => s.busyAction, 'busy', isNull),
      isA<QuoteDetailLoaded>()
          .having((s) => s.busyAction, 'busy', QuoteActionKind.withdraw),
      isA<QuoteDetailLoaded>().having((s) => s.busyAction, 'busy', isNull),
    ],
  );

  blocTest<QuoteDetailBloc, QuoteDetailState>(
    '409 → refresh detail silently + actionError set',
    build: () => QuoteDetailBloc(
      gateway: _FakeGateway(throw409Once: true),
      quoteId: 'q-1',
    ),
    act: (b) async {
      b.add(const QuoteDetailStarted());
      await Future<void>.delayed(Duration.zero);
      b.add(const QuoteDetailActionRequested(
        kind: QuoteActionKind.submitAcceptance,
      ));
    },
    skip: 2,
    expect: () => [
      isA<QuoteDetailLoaded>().having(
          (s) => s.busyAction, 'busy', QuoteActionKind.submitAcceptance),
      isA<QuoteDetailLoaded>()
          .having((s) => s.busyAction, 'busy', isNull)
          .having((s) => s.actionError, 'actionError', 'quote.state_conflict'),
    ],
  );

  blocTest<QuoteDetailBloc, QuoteDetailState>(
    'load failure surfaces failure state',
    build: () => QuoteDetailBloc(
      gateway: _ThrowingGateway(),
      quoteId: 'q-1',
    ),
    act: (b) => b.add(const QuoteDetailStarted()),
    expect: () => [
      isA<QuoteDetailLoading>(),
      isA<QuoteDetailLoadFailure>(),
    ],
  );
}

class _ThrowingGateway implements QuotesGateway {
  @override
  Future<QuoteDetail> getById(String id) async => throw Exception('boom');

  @override
  Future<QuotesPage> list(QuotesFilter filter) => throw UnimplementedError();

  @override
  Future<QuotesPage> awaitingMyApproval() => throw UnimplementedError();

  @override
  Future<CreateQuoteResult> createFromCart({
    required CreateQuoteFromCartRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<CreateQuoteResult> createFromProduct({
    required CreateQuoteFromProductRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<QuoteDetail> submitAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<QuoteDetail> finalizeAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<QuoteDetail> rejectAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<QuoteDetail> requestRevision({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<QuoteDetail> withdraw({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<SaveAsTemplateResult> saveAsTemplate({
    required String quoteId,
    required SaveAsTemplateRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();

  @override
  Future<Uint8List> downloadDocument({
    required String quoteId,
    required String versionId,
    required String locale,
  }) =>
      throw UnimplementedError();
}
