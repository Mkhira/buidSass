import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/checkout_repository.dart';
import '../data/checkout_view_models.dart';

@immutable
sealed class CheckoutState {
  const CheckoutState();
}

class CheckoutIdle extends CheckoutState {
  const CheckoutIdle();
}

class CheckoutDrafting extends CheckoutState {
  const CheckoutDrafting({
    required this.session,
    this.selectedAddressId,
    this.selectedQuoteId,
    this.selectedPaymentMethodId,
  });

  final CheckoutSession session;
  final String? selectedAddressId;
  final String? selectedQuoteId;
  final String? selectedPaymentMethodId;

  bool get isReady =>
      selectedAddressId != null &&
      selectedQuoteId != null &&
      selectedPaymentMethodId != null;

  CheckoutDrafting copyWith({
    CheckoutSession? session,
    String? selectedAddressId,
    String? selectedQuoteId,
    String? selectedPaymentMethodId,
  }) {
    return CheckoutDrafting(
      session: session ?? this.session,
      selectedAddressId: selectedAddressId ?? this.selectedAddressId,
      selectedQuoteId: selectedQuoteId ?? this.selectedQuoteId,
      selectedPaymentMethodId:
          selectedPaymentMethodId ?? this.selectedPaymentMethodId,
    );
  }
}

class CheckoutReady extends CheckoutState {
  const CheckoutReady({
    required this.session,
    required this.selectedAddressId,
    required this.selectedQuoteId,
    required this.selectedPaymentMethodId,
    required this.idempotencyKey,
  });
  final CheckoutSession session;
  final String selectedAddressId;
  final String selectedQuoteId;
  final String selectedPaymentMethodId;
  final String idempotencyKey;
}

class CheckoutSubmitting extends CheckoutState {
  const CheckoutSubmitting(this.previous);
  final CheckoutReady previous;
}

class CheckoutSubmitted extends CheckoutState {
  const CheckoutSubmitted(this.outcome);
  final CheckoutOutcome outcome;
}

class CheckoutDriftBlocked extends CheckoutState {
  const CheckoutDriftBlocked(this.details);
  final CheckoutDriftDetails details;
}

class CheckoutFailed extends CheckoutState {
  const CheckoutFailed(this.previous, this.reasonCode);
  final CheckoutReady previous;
  final String reasonCode;
}

class CheckoutFailedTerminal extends CheckoutState {
  const CheckoutFailedTerminal(this.reasonCode);
  final String reasonCode;
}

@immutable
sealed class CheckoutEvent {
  const CheckoutEvent();
}

class CheckoutStarted extends CheckoutEvent {
  const CheckoutStarted();
}

class AddressSelected extends CheckoutEvent {
  const AddressSelected(this.addressId);
  final String addressId;
}

class ShippingSelected extends CheckoutEvent {
  const ShippingSelected(this.quoteId);
  final String quoteId;
}

class PaymentSelected extends CheckoutEvent {
  const PaymentSelected(this.methodId);
  final String methodId;
}

class SubmitTapped extends CheckoutEvent {
  const SubmitTapped();
}

class RetryTapped extends CheckoutEvent {
  const RetryTapped();
}

class DriftAccepted extends CheckoutEvent {
  const DriftAccepted();
}

class CheckoutBloc extends Bloc<CheckoutEvent, CheckoutState> {
  CheckoutBloc({required CheckoutRepository repository})
      : _repository = repository,
        super(const CheckoutIdle()) {
    on<CheckoutStarted>(_onStarted);
    on<AddressSelected>(_onAddress);
    on<ShippingSelected>(_onShipping);
    on<PaymentSelected>(_onPayment);
    on<SubmitTapped>(_onSubmit);
    on<RetryTapped>(_onRetry);
    on<DriftAccepted>(_onDriftAccepted);
  }

  final CheckoutRepository _repository;

  Future<void> _onStarted(
    CheckoutStarted event,
    Emitter<CheckoutState> emit,
  ) async {
    try {
      final session = await _repository.startSession();
      emit(CheckoutDrafting(session: session));
    } on CheckoutSessionExpiredException catch (e) {
      emit(CheckoutFailedTerminal(e.toString()));
    } on Object catch (e) {
      emit(CheckoutFailedTerminal(e.toString()));
    }
  }

  Future<void> _onAddress(
    AddressSelected event,
    Emitter<CheckoutState> emit,
  ) =>
      _patch(emit, (s) async {
        final next = await _repository.setAddress(
          sessionId: s.session.sessionId,
          addressId: event.addressId,
        );
        return s.copyWith(session: next, selectedAddressId: event.addressId);
      });

  Future<void> _onShipping(
    ShippingSelected event,
    Emitter<CheckoutState> emit,
  ) =>
      _patch(emit, (s) async {
        final next = await _repository.setShipping(
          sessionId: s.session.sessionId,
          quoteId: event.quoteId,
        );
        return s.copyWith(session: next, selectedQuoteId: event.quoteId);
      });

  Future<void> _onPayment(
    PaymentSelected event,
    Emitter<CheckoutState> emit,
  ) =>
      _patch(emit, (s) async {
        final next = await _repository.setPayment(
          sessionId: s.session.sessionId,
          methodId: event.methodId,
        );
        return s.copyWith(
          session: next,
          selectedPaymentMethodId: event.methodId,
        );
      });

  Future<void> _patch(
    Emitter<CheckoutState> emit,
    Future<CheckoutDrafting> Function(CheckoutDrafting) update,
  ) async {
    final s = state;
    final draft = switch (s) {
      CheckoutDrafting() => s,
      CheckoutReady(:final session, :final selectedAddressId, :final selectedQuoteId, :final selectedPaymentMethodId) =>
        CheckoutDrafting(
          session: session,
          selectedAddressId: selectedAddressId,
          selectedQuoteId: selectedQuoteId,
          selectedPaymentMethodId: selectedPaymentMethodId,
        ),
      _ => null,
    };
    if (draft == null) return;
    try {
      final next = await update(draft);
      emit(next.isReady
          ? CheckoutReady(
              session: next.session,
              selectedAddressId: next.selectedAddressId!,
              selectedQuoteId: next.selectedQuoteId!,
              selectedPaymentMethodId: next.selectedPaymentMethodId!,
              idempotencyKey: _generateIdempotencyKey(),
            )
          : next);
    } on CheckoutSessionExpiredException catch (e) {
      emit(CheckoutFailedTerminal(e.toString()));
    } on Object catch (_) {
      // keep prior state
    }
  }

  Future<void> _onSubmit(
    SubmitTapped event,
    Emitter<CheckoutState> emit,
  ) async {
    final s = state;
    final ready = s is CheckoutReady ? s : null;
    if (ready == null) return;
    emit(CheckoutSubmitting(ready));
    try {
      final outcome = await _repository.submit(
        sessionId: ready.session.sessionId,
        idempotencyKey: ready.idempotencyKey,
      );
      emit(CheckoutSubmitted(outcome));
    } on CheckoutDriftException catch (e) {
      emit(CheckoutDriftBlocked(e.details));
    } on CheckoutSessionExpiredException catch (e) {
      emit(CheckoutFailedTerminal(e.toString()));
    } on Object catch (e) {
      emit(CheckoutFailed(ready, e.toString()));
    }
  }

  Future<void> _onRetry(
    RetryTapped event,
    Emitter<CheckoutState> emit,
  ) async {
    final s = state;
    final failed = s is CheckoutFailed ? s : null;
    if (failed == null) return;
    emit(CheckoutSubmitting(failed.previous));
    try {
      final outcome = await _repository.submit(
        sessionId: failed.previous.session.sessionId,
        idempotencyKey: failed.previous.idempotencyKey,
      );
      emit(CheckoutSubmitted(outcome));
    } on Object catch (e) {
      emit(CheckoutFailed(failed.previous, e.toString()));
    }
  }

  void _onDriftAccepted(
    DriftAccepted event,
    Emitter<CheckoutState> emit,
  ) {
    final s = state;
    if (s is CheckoutDriftBlocked) {
      // After accepting the drift the user re-opens the session; for v1 we
      // bounce back to Idle and let the screen restart `CheckoutStarted`.
      emit(const CheckoutIdle());
    }
  }

  static String _generateIdempotencyKey() {
    final r = Random.secure();
    final bytes = List<int>.generate(16, (_) => r.nextInt(256));
    bytes[6] = (bytes[6] & 0x0F) | 0x40;
    bytes[8] = (bytes[8] & 0x3F) | 0x80;
    String hex(int from, int to) =>
        bytes.sublist(from, to).map((b) => b.toRadixString(16).padLeft(2, '0')).join();
    return '${hex(0, 4)}-${hex(4, 6)}-${hex(6, 8)}-${hex(8, 10)}-${hex(10, 16)}';
  }
}
