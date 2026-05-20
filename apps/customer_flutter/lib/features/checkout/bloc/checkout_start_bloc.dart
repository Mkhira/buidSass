import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/checkout_gateway.dart';
import '../data/models/checkout_models.dart';
import 'checkout_drift.dart';

// ===== State =====

@immutable
sealed class CheckoutStartState {
  const CheckoutStartState();
}

class CheckoutStartIdle extends CheckoutStartState {
  const CheckoutStartIdle();
}

class CheckoutStarting extends CheckoutStartState {
  const CheckoutStarting();
}

class CheckoutStartedState extends CheckoutStartState {
  const CheckoutStartedState({required this.sessionId, required this.summary});
  final String sessionId;
  final CheckoutSummary summary;
}

class CheckoutStartConflict extends CheckoutStartState {
  const CheckoutStartConflict(this.conflict);
  final CheckoutConflict conflict;
}

class CheckoutStartFailure extends CheckoutStartState {
  const CheckoutStartFailure({required this.reason, this.correlationId});
  final String reason;
  final String? correlationId;
}

// ===== Events =====

@immutable
sealed class CheckoutStartEvent {
  const CheckoutStartEvent();
}

class StartCheckoutRequested extends CheckoutStartEvent {
  const StartCheckoutRequested({required this.request});
  final CreateSessionRequest request;
}

class StartCheckoutRetried extends CheckoutStartEvent {
  const StartCheckoutRetried({required this.request});
  final CreateSessionRequest request;
}

// ===== Bloc =====

class CheckoutStartBloc extends Bloc<CheckoutStartEvent, CheckoutStartState>
    with CheckoutDriftAware {
  CheckoutStartBloc({required CheckoutGateway gateway})
      : _gateway = gateway,
        super(const CheckoutStartIdle()) {
    on<StartCheckoutRequested>(_onStart);
    on<StartCheckoutRetried>((e, emit) => _run(e.request, emit));
  }

  final CheckoutGateway _gateway;

  Future<void> _onStart(
    StartCheckoutRequested event,
    Emitter<CheckoutStartState> emit,
  ) async {
    await _run(event.request, emit);
  }

  Future<void> _run(
    CreateSessionRequest request,
    Emitter<CheckoutStartState> emit,
  ) async {
    emit(const CheckoutStarting());
    try {
      final result = await _gateway.createSession(request);
      emit(CheckoutStartedState(
        sessionId: result.sessionId,
        summary: result.summary,
      ));
    } on CheckoutDriftException catch (e) {
      emit(CheckoutStartConflict(driftFrom(e)));
    } on Object catch (e) {
      emit(CheckoutStartFailure(reason: e.toString()));
    }
  }
}
