import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:uuid/uuid.dart';

import '../data/models/verification_models.dart';
import '../data/verification_gateway.dart';

// ============================================================
// Events
// ============================================================

@immutable
sealed class RenewEvent {
  const RenewEvent();
}

class RenewStarted extends RenewEvent {
  const RenewStarted(
      {required this.priorVerificationId, required this.marketCode});
  final String priorVerificationId;
  final String marketCode;
}

class RenewSubmitted extends RenewEvent {
  const RenewSubmitted();
}

// ============================================================
// State
// ============================================================

@immutable
sealed class RenewState {
  const RenewState();
}

class RenewLoading extends RenewState {
  const RenewLoading();
}

class RenewLoadFailure extends RenewState {
  const RenewLoadFailure({required this.reason});
  final String reason;
}

class RenewReady extends RenewState {
  const RenewReady({
    required this.prior,
    required this.priorVerificationId,
    required this.marketCode,
    this.formError,
  });

  final VerificationDetail prior;
  final String priorVerificationId;
  final String marketCode;
  final String? formError;

  RenewReady copyWith({Object? formError = _sentinel}) {
    return RenewReady(
      prior: prior,
      priorVerificationId: priorVerificationId,
      marketCode: marketCode,
      formError: identical(formError, _sentinel)
          ? this.formError
          : formError as String?,
    );
  }
}

class RenewSubmitting extends RenewState {
  const RenewSubmitting(this.ready);
  final RenewReady ready;
}

class RenewDone extends RenewState {
  const RenewDone(this.result);
  final SubmitVerificationResult result;
}

const _sentinel = Object();

// ============================================================
// Bloc
// ============================================================

/// Bloc for S-7.4 renew. Pre-fetches the prior verification so the
/// screen can render the customer's existing details for confirmation
/// — the actual renew call is a single POST with priorVerificationId
/// (BR-5) and an Idempotency-Key locked in at construction (BR-5a).
class RenewBloc extends Bloc<RenewEvent, RenewState> {
  RenewBloc({
    required VerificationGateway gateway,
    String Function()? idempotencyKeyFactory,
  })  : _gateway = gateway,
        _idempotencyKey = (idempotencyKeyFactory ?? const Uuid().v4)(),
        super(const RenewLoading()) {
    on<RenewStarted>(_onStarted);
    on<RenewSubmitted>(_onSubmitted);
  }

  final VerificationGateway _gateway;
  final String _idempotencyKey;

  /// Stored so the screen can dispatch a fresh `RenewStarted` from a
  /// failure state without holding the original event reference. The
  /// retry path on the error screen reads these to re-emit the start
  /// event rather than popping the route.
  String? _priorVerificationId;
  String? _marketCode;

  @visibleForTesting
  String get idempotencyKey => _idempotencyKey;

  /// Args last seen from `RenewStarted` — exposed for the retry CTA on
  /// the failure screen. Null when the bloc has never received a
  /// `RenewStarted`.
  ({String priorVerificationId, String marketCode})? get lastStartArgs =>
      (_priorVerificationId != null && _marketCode != null)
          ? (
              priorVerificationId: _priorVerificationId!,
              marketCode: _marketCode!,
            )
          : null;

  Future<void> _onStarted(
    RenewStarted event,
    Emitter<RenewState> emit,
  ) async {
    _priorVerificationId = event.priorVerificationId;
    _marketCode = event.marketCode;
    emit(const RenewLoading());
    try {
      final prior = await _gateway.getById(event.priorVerificationId);
      emit(RenewReady(
        prior: prior,
        priorVerificationId: event.priorVerificationId,
        marketCode: event.marketCode,
      ));
    } on Object catch (_) {
      // Don't surface raw exception text — UI shows a stable
      // commonErrorBody copy. The error stays observable via logs /
      // crash reporting; the bloc state just signals "load failed".
      emit(const RenewLoadFailure(reason: 'renew.load_failed'));
    }
  }

  Future<void> _onSubmitted(
    RenewSubmitted event,
    Emitter<RenewState> emit,
  ) async {
    final s = state;
    if (s is! RenewReady) return;
    emit(RenewSubmitting(s));
    try {
      final result = await _gateway.renew(
        request: RenewVerificationRequest(
          priorVerificationId: s.priorVerificationId,
          marketCode: s.marketCode,
        ),
        idempotencyKey: _idempotencyKey,
      );
      emit(RenewDone(result));
    } on Object catch (_) {
      // Use a stable error key — the screen resolves it to a
      // localized commonErrorBody string.
      emit(s.copyWith(formError: 'renew.submit_failed'));
    }
  }
}
