import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:uuid/uuid.dart';

import '../data/models/quote_models.dart';
import '../data/quotes_gateway.dart';
import '../widgets/quote_actions_toolbar.dart';

@immutable
sealed class QuoteDetailEvent {
  const QuoteDetailEvent();
}

class QuoteDetailStarted extends QuoteDetailEvent {
  const QuoteDetailStarted();
}

class QuoteDetailRefreshed extends QuoteDetailEvent {
  const QuoteDetailRefreshed();
}

/// Dispatch a server-side action. The bloc generates a fresh
/// Idempotency-Key per action attempt (so retries reuse the same key
/// but separate user-initiated taps get fresh ones).
class QuoteDetailActionRequested extends QuoteDetailEvent {
  const QuoteDetailActionRequested(
      {required this.kind, this.note, this.templateName});
  final QuoteActionKind kind;
  final String? note;

  /// Required when [kind] is [QuoteActionKind.saveAsTemplate].
  final String? templateName;
}

@immutable
sealed class QuoteDetailState {
  const QuoteDetailState();
}

class QuoteDetailLoading extends QuoteDetailState {
  const QuoteDetailLoading();
}

class QuoteDetailLoadFailure extends QuoteDetailState {
  const QuoteDetailLoadFailure({required this.reason});
  final String reason;
}

class QuoteDetailLoaded extends QuoteDetailState {
  const QuoteDetailLoaded({
    required this.quote,
    this.busyAction,
    this.actionError,
  });

  final QuoteDetail quote;

  /// The action currently being submitted, if any. The toolbar disables
  /// every other action while one is in-flight.
  final QuoteActionKind? busyAction;

  /// Last action's failure reason (stable error key — UI resolves to
  /// a localized fallback).
  final String? actionError;

  QuoteDetailLoaded copyWith({
    QuoteDetail? quote,
    Object? busyAction = _sentinel,
    Object? actionError = _sentinel,
  }) {
    return QuoteDetailLoaded(
      quote: quote ?? this.quote,
      busyAction: identical(busyAction, _sentinel)
          ? this.busyAction
          : busyAction as QuoteActionKind?,
      actionError: identical(actionError, _sentinel)
          ? this.actionError
          : actionError as String?,
    );
  }
}

const _sentinel = Object();

/// Bloc for S-8.5 — quote detail + 6 transition actions. Action
/// dispatch is single-flight: while one action is in flight all
/// others gate off in the toolbar. On any 409 from the server, we
/// silently refresh and let the user re-try with the fresh action
/// allowlist.
class QuoteDetailBloc extends Bloc<QuoteDetailEvent, QuoteDetailState> {
  QuoteDetailBloc({
    required QuotesGateway gateway,
    required String quoteId,
    String Function()? idempotencyKeyFactory,
  })  : _gateway = gateway,
        _quoteId = quoteId,
        _newKey = idempotencyKeyFactory ?? const Uuid().v4,
        super(const QuoteDetailLoading()) {
    on<QuoteDetailStarted>(_load);
    on<QuoteDetailRefreshed>(_load);
    on<QuoteDetailActionRequested>(_onAction);
  }

  final QuotesGateway _gateway;
  final String _quoteId;
  final String Function() _newKey;

  Future<void> _load(
    QuoteDetailEvent e,
    Emitter<QuoteDetailState> emit,
  ) async {
    if (state is! QuoteDetailLoaded) {
      emit(const QuoteDetailLoading());
    }
    try {
      final quote = await _gateway.getById(_quoteId);
      final current = state;
      if (current is QuoteDetailLoaded) {
        emit(current.copyWith(
          quote: quote,
          busyAction: null,
          actionError: null,
        ));
      } else {
        emit(QuoteDetailLoaded(quote: quote));
      }
    } on Object catch (_) {
      emit(const QuoteDetailLoadFailure(reason: 'quote.load_failed'));
    }
  }

  Future<void> _onAction(
    QuoteDetailActionRequested e,
    Emitter<QuoteDetailState> emit,
  ) async {
    final s = state;
    if (s is! QuoteDetailLoaded || s.busyAction != null) return;
    emit(s.copyWith(busyAction: e.kind, actionError: null));
    final key = _newKey();
    try {
      QuoteDetail? next;
      switch (e.kind) {
        case QuoteActionKind.submitAcceptance:
          next = await _gateway.submitAcceptance(
            quoteId: _quoteId,
            request: QuoteActionNoteRequest(note: e.note),
            idempotencyKey: key,
          );
          break;
        case QuoteActionKind.finalizeAcceptance:
          next = await _gateway.finalizeAcceptance(
            quoteId: _quoteId,
            request: QuoteActionNoteRequest(note: e.note),
            idempotencyKey: key,
          );
          break;
        case QuoteActionKind.rejectAcceptance:
          next = await _gateway.rejectAcceptance(
            quoteId: _quoteId,
            request: QuoteActionNoteRequest(note: e.note),
            idempotencyKey: key,
          );
          break;
        case QuoteActionKind.requestRevision:
          next = await _gateway.requestRevision(
            quoteId: _quoteId,
            request: QuoteActionNoteRequest(note: e.note),
            idempotencyKey: key,
          );
          break;
        case QuoteActionKind.withdraw:
          next = await _gateway.withdraw(
            quoteId: _quoteId,
            request: QuoteActionNoteRequest(note: e.note),
            idempotencyKey: key,
          );
          break;
        case QuoteActionKind.saveAsTemplate:
          await _gateway.saveAsTemplate(
            quoteId: _quoteId,
            request: SaveAsTemplateRequest(
              templateName: e.templateName ?? s.quote.quoteNumber,
            ),
            idempotencyKey: key,
          );
          // Template-save doesn't mutate quote state; refresh detail
          // anyway so any server-side action gating updates are picked
          // up.
          next = await _gateway.getById(_quoteId);
          break;
      }
      emit(s.copyWith(quote: next, busyAction: null, actionError: null));
    } on Object catch (err) {
      final msg = err.toString();
      // BR-2: a 409 means the quote state changed under us — refresh
      // silently and let the user re-evaluate with the new allowlist.
      if (msg.contains('409') || msg.contains('conflict')) {
        try {
          final refreshed = await _gateway.getById(_quoteId);
          emit(s.copyWith(
            quote: refreshed,
            busyAction: null,
            actionError: 'quote.state_conflict',
          ));
          return;
        } on Object catch (_) {
          // fall through to the generic error path
        }
      }
      emit(s.copyWith(
        busyAction: null,
        actionError: 'quote.action_failed',
      ));
    }
  }
}
