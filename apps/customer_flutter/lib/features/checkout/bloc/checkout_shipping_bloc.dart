import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/checkout_gateway.dart';
import '../data/models/checkout_models.dart';
import 'checkout_drift.dart';

@immutable
sealed class CheckoutShippingState {
  const CheckoutShippingState();
}

class CheckoutShippingLoadingQuotes extends CheckoutShippingState {
  const CheckoutShippingLoadingQuotes();
}

class CheckoutShippingLoaded extends CheckoutShippingState {
  const CheckoutShippingLoaded({required this.options, this.selectedMethod});
  final List<ShippingQuoteOption> options;
  final String? selectedMethod;
}

class CheckoutShippingEmpty extends CheckoutShippingState {
  const CheckoutShippingEmpty();
}

class CheckoutShippingSubmitting extends CheckoutShippingState {
  const CheckoutShippingSubmitting();
}

class CheckoutShippingSubmitted extends CheckoutShippingState {
  const CheckoutShippingSubmitted(this.summary);
  final CheckoutSummary summary;
}

class CheckoutShippingConflict extends CheckoutShippingState {
  const CheckoutShippingConflict(this.conflict);
  final CheckoutConflict conflict;
}

class CheckoutShippingFailure extends CheckoutShippingState {
  const CheckoutShippingFailure({required this.reason});
  final String reason;
}

@immutable
sealed class CheckoutShippingEvent {
  const CheckoutShippingEvent();
}

class ShippingQuotesRequested extends CheckoutShippingEvent {
  const ShippingQuotesRequested();
}

class ShippingMethodSelected extends CheckoutShippingEvent {
  const ShippingMethodSelected(this.method);
  final String method;
}

class ShippingSubmitted extends CheckoutShippingEvent {
  const ShippingSubmitted(this.method);
  final String method;
}

class CheckoutShippingBloc
    extends Bloc<CheckoutShippingEvent, CheckoutShippingState>
    with CheckoutDriftAware {
  CheckoutShippingBloc({
    required CheckoutGateway gateway,
    required this.sessionId,
  })  : _gateway = gateway,
        super(const CheckoutShippingLoadingQuotes()) {
    on<ShippingQuotesRequested>(_load);
    on<ShippingMethodSelected>(_onSelected);
    on<ShippingSubmitted>(_onSubmit);
  }

  final CheckoutGateway _gateway;
  final String sessionId;

  Future<void> _load(
    ShippingQuotesRequested event,
    Emitter<CheckoutShippingState> emit,
  ) async {
    emit(const CheckoutShippingLoadingQuotes());
    try {
      final options = await _gateway.getShippingQuotes(sessionId);
      if (options.isEmpty) {
        emit(const CheckoutShippingEmpty());
        return;
      }
      emit(CheckoutShippingLoaded(options: options));
    } on Object catch (e) {
      emit(CheckoutShippingFailure(reason: e.toString()));
    }
  }

  void _onSelected(
    ShippingMethodSelected event,
    Emitter<CheckoutShippingState> emit,
  ) {
    final s = state;
    if (s is CheckoutShippingLoaded) {
      emit(CheckoutShippingLoaded(
          options: s.options, selectedMethod: event.method));
    }
  }

  Future<void> _onSubmit(
    ShippingSubmitted event,
    Emitter<CheckoutShippingState> emit,
  ) async {
    emit(const CheckoutShippingSubmitting());
    try {
      final summary = await _gateway.patchShipping(
        sessionId: sessionId,
        method: event.method,
      );
      emit(CheckoutShippingSubmitted(summary));
    } on CheckoutDriftException catch (e) {
      emit(CheckoutShippingConflict(driftFrom(e)));
    } on Object catch (e) {
      emit(CheckoutShippingFailure(reason: e.toString()));
    }
  }
}
