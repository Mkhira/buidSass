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
  const RenewStarted({required this.priorVerificationId, required this.marketCode});
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
      formError:
          identical(formError, _sentinel) ? this.formError : formError as String?,
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

  @visibleForTesting
  String get idempotencyKey => _idempotencyKey;

  Future<void> _onStarted(
    RenewStarted event,
    Emitter<RenewState> emit,
  ) async {
    emit(const RenewLoading());
    try {
      final prior = await _gateway.getById(event.priorVerificationId);
      emit(RenewReady(
        prior: prior,
        priorVerificationId: event.priorVerificationId,
        marketCode: event.marketCode,
      ));
    } on Object catch (e) {
      emit(RenewLoadFailure(reason: e.toString()));
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
    } on Object catch (e) {
      emit(s.copyWith(formError: e.toString()));
    }
  }
}
