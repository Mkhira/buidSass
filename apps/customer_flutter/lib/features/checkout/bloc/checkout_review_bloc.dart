import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/checkout_gateway.dart';
import '../data/models/checkout_models.dart';
import 'checkout_drift.dart';

@immutable
sealed class CheckoutReviewState {
  const CheckoutReviewState();
}

class CheckoutReviewLoaded extends CheckoutReviewState {
  const CheckoutReviewLoaded({
    required this.summary,
    required this.idempotencyKey,
  });
  final CheckoutSummary summary;
  final String idempotencyKey;
}

class CheckoutReviewSubmitting extends CheckoutReviewState {
  const CheckoutReviewSubmitting({required this.idempotencyKey});
  final String idempotencyKey;
}

class CheckoutReviewRedirecting extends CheckoutReviewState {
  const CheckoutReviewRedirecting({required this.url, required this.orderId});
  final String url;
  final String orderId;
}

class CheckoutReviewSuccess extends CheckoutReviewState {
  const CheckoutReviewSuccess(this.result);
  final SubmitResult result;
}

class CheckoutReviewConflict extends CheckoutReviewState {
  const CheckoutReviewConflict({
    required this.conflict,
    required this.idempotencyKey,
  });
  final CheckoutConflict conflict;
  final String idempotencyKey;
}

class CheckoutReviewFailure extends CheckoutReviewState {
  const CheckoutReviewFailure({
    required this.reason,
    required this.idempotencyKey,
    this.correlationId,
  });
  final String reason;
  final String idempotencyKey;
  final String? correlationId;
}

@immutable
sealed class CheckoutReviewEvent {
  const CheckoutReviewEvent();
}

class ReviewStarted extends CheckoutReviewEvent {
  const ReviewStarted(this.summary);
  final CheckoutSummary summary;
}

class ReviewSubmitted extends CheckoutReviewEvent {
  const ReviewSubmitted();
}

class ReviewDriftAccepted extends CheckoutReviewEvent {
  const ReviewDriftAccepted();
}

class ReviewRedirectReturned extends CheckoutReviewEvent {
  const ReviewRedirectReturned({required this.success});
  final bool success;
}

class CheckoutReviewBloc extends Bloc<CheckoutReviewEvent, CheckoutReviewState>
    with CheckoutDriftAware {
  CheckoutReviewBloc({
    required CheckoutGateway gateway,
    required this.sessionId,
    required CheckoutSummary initialSummary,
    String Function()? idempotencyKeyFactory,
  })  : _gateway = gateway,
        _idFactory = idempotencyKeyFactory ?? _defaultIdempotencyKey,
        super(CheckoutReviewLoaded(
          summary: initialSummary,
          idempotencyKey: (idempotencyKeyFactory ?? _defaultIdempotencyKey)(),
        )) {
    on<ReviewStarted>(_onStarted);
    on<ReviewSubmitted>(_onSubmit);
    on<ReviewDriftAccepted>(_onDriftAccepted);
    on<ReviewRedirectReturned>(_onRedirectReturned);
  }

  final CheckoutGateway _gateway;
  final String Function() _idFactory;
  final String sessionId;

  /// Read the active idempotency key — same key reused for every retry
  /// of the current user intent (BR-3 / plan.md "Idempotency-Key
  /// handling"). Cleared and regenerated only when the bloc is rebuilt
  /// (router replays a new instance on entry to /review).
  String get idempotencyKey {
    final s = state;
    return switch (s) {
      CheckoutReviewLoaded(:final idempotencyKey) => idempotencyKey,
      CheckoutReviewSubmitting(:final idempotencyKey) => idempotencyKey,
      CheckoutReviewConflict(:final idempotencyKey) => idempotencyKey,
      CheckoutReviewFailure(:final idempotencyKey) => idempotencyKey,
      _ => _idFactory(),
    };
  }

  void _onStarted(ReviewStarted event, Emitter<CheckoutReviewState> emit) {
    emit(CheckoutReviewLoaded(
      summary: event.summary,
      idempotencyKey: idempotencyKey,
    ));
  }

  Future<void> _onSubmit(
    ReviewSubmitted event,
    Emitter<CheckoutReviewState> emit,
  ) async {
    final key = idempotencyKey;
    emit(CheckoutReviewSubmitting(idempotencyKey: key));
    try {
      final result = await _gateway.submit(
        sessionId: sessionId,
        idempotencyKey: key,
      );
      if (result.redirect.kind != 'none' && result.redirect.url != null) {
        emit(CheckoutReviewRedirecting(
          url: result.redirect.url!,
          orderId: result.orderId,
        ));
        return;
      }
      emit(CheckoutReviewSuccess(result));
    } on CheckoutDriftException catch (e) {
      emit(CheckoutReviewConflict(
        conflict: driftFrom(e),
        idempotencyKey: key,
      ));
    } on Object catch (e) {
      emit(CheckoutReviewFailure(
        reason: e.toString(),
        idempotencyKey: key,
      ));
    }
  }

  Future<void> _onDriftAccepted(
    ReviewDriftAccepted event,
    Emitter<CheckoutReviewState> emit,
  ) async {
    try {
      final summary = await _gateway.acceptDrift(sessionId);
      emit(CheckoutReviewLoaded(
        summary: summary,
        idempotencyKey: idempotencyKey,
      ));
    } on Object catch (e) {
      emit(CheckoutReviewFailure(
        reason: e.toString(),
        idempotencyKey: idempotencyKey,
      ));
    }
  }

  void _onRedirectReturned(
    ReviewRedirectReturned event,
    Emitter<CheckoutReviewState> emit,
  ) {
    if (event.success) {
      // After a successful 3DS/provider redirect we re-run submit with
      // the SAME idempotency key — the server treats it as a no-op
      // confirmation and returns the canonical SubmitResult.
      add(const ReviewSubmitted());
      return;
    }
    final key = idempotencyKey;
    emit(CheckoutReviewFailure(
      reason: 'payment_cancelled',
      idempotencyKey: key,
    ));
  }
}

/// RFC-4122 v4 UUID — used for the Idempotency-Key header. Mirrors
/// `_defaultUuidV4` in `core/api/correlation_id_interceptor.dart` to
/// keep this bloc free of `package:uuid`.
String _defaultIdempotencyKey() {
  final r = Random.secure();
  final bytes = List<int>.generate(16, (_) => r.nextInt(256));
  bytes[6] = (bytes[6] & 0x0F) | 0x40;
  bytes[8] = (bytes[8] & 0x3F) | 0x80;
  String hex(int from, int to) => bytes
      .sublist(from, to)
      .map((b) => b.toRadixString(16).padLeft(2, '0'))
      .join();
  return '${hex(0, 4)}-${hex(4, 6)}-${hex(6, 8)}-${hex(8, 10)}-${hex(10, 16)}';
}
