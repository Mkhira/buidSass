import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/checkout_gateway.dart';
import '../data/models/checkout_models.dart';

@immutable
sealed class CheckoutSummaryState {
  const CheckoutSummaryState();
}

class CheckoutSummaryLoading extends CheckoutSummaryState {
  const CheckoutSummaryLoading();
}

class CheckoutSummaryLoaded extends CheckoutSummaryState {
  const CheckoutSummaryLoaded(this.summary);
  final CheckoutSummary summary;
}

class CheckoutSummaryFailure extends CheckoutSummaryState {
  const CheckoutSummaryFailure({required this.reason, this.correlationId});
  final String reason;
  final String? correlationId;
}

@immutable
sealed class CheckoutSummaryEvent {
  const CheckoutSummaryEvent();
}

class CheckoutSummaryRequested extends CheckoutSummaryEvent {
  const CheckoutSummaryRequested();
}

class CheckoutSummaryRefreshed extends CheckoutSummaryEvent {
  const CheckoutSummaryRefreshed();
}

class CheckoutSummaryBloc
    extends Bloc<CheckoutSummaryEvent, CheckoutSummaryState> {
  CheckoutSummaryBloc({
    required CheckoutGateway gateway,
    required this.sessionId,
  })  : _gateway = gateway,
        super(const CheckoutSummaryLoading()) {
    on<CheckoutSummaryRequested>(_load);
    on<CheckoutSummaryRefreshed>(_load);
  }

  final CheckoutGateway _gateway;
  final String sessionId;

  Future<void> _load(
    CheckoutSummaryEvent event,
    Emitter<CheckoutSummaryState> emit,
  ) async {
    emit(const CheckoutSummaryLoading());
    try {
      final s = await _gateway.getSummary(sessionId);
      emit(CheckoutSummaryLoaded(s));
    } on Object catch (e) {
      emit(CheckoutSummaryFailure(reason: e.toString()));
    }
  }
}
