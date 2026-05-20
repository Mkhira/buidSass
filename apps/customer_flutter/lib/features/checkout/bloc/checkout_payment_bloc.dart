import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/checkout_gateway.dart';
import '../data/models/checkout_models.dart';
import '../payment_adapters/payment_adapter.dart';
import '../payment_adapters/stub_adapters.dart';
import 'checkout_drift.dart';

@immutable
sealed class CheckoutPaymentState {
  const CheckoutPaymentState();
}

class CheckoutPaymentIdle extends CheckoutPaymentState {
  const CheckoutPaymentIdle(this.summary);
  final CheckoutSummary summary;
}

class CheckoutPaymentCollecting extends CheckoutPaymentState {
  const CheckoutPaymentCollecting(this.method);
  final String method;
}

class CheckoutPaymentSubmitting extends CheckoutPaymentState {
  const CheckoutPaymentSubmitting();
}

class CheckoutPaymentSubmitted extends CheckoutPaymentState {
  const CheckoutPaymentSubmitted(this.summary);
  final CheckoutSummary summary;
}

class CheckoutPaymentConflict extends CheckoutPaymentState {
  const CheckoutPaymentConflict(this.conflict);
  final CheckoutConflict conflict;
}

class CheckoutPaymentFailure extends CheckoutPaymentState {
  const CheckoutPaymentFailure({required this.reason, this.fields = const {}});
  final String reason;
  final Map<String, String> fields;
}

@immutable
sealed class CheckoutPaymentEvent {
  const CheckoutPaymentEvent();
}

class PaymentMethodChosen extends CheckoutPaymentEvent {
  const PaymentMethodChosen({required this.method, required this.token});
  final String method;
  final PaymentTokenResult token;
}

class CheckoutPaymentBloc
    extends Bloc<CheckoutPaymentEvent, CheckoutPaymentState>
    with CheckoutDriftAware {
  CheckoutPaymentBloc({
    required CheckoutGateway gateway,
    required this.sessionId,
    required CheckoutSummary initial,
    PaymentAdapterRegistry? registry,
  })  : _gateway = gateway,
        _registry = registry ?? PaymentAdapterRegistry(),
        super(CheckoutPaymentIdle(initial)) {
    on<PaymentMethodChosen>(_onChosen);
  }

  final CheckoutGateway _gateway;
  final PaymentAdapterRegistry _registry;
  final String sessionId;

  /// Exposed so the screen layer can look up the right adapter and run
  /// its widget-aware collection step before dispatching the chosen
  /// event with the resulting token.
  PaymentAdapter? adapterFor(String method) => _registry.forMethod(method);

  Future<void> _onChosen(
    PaymentMethodChosen event,
    Emitter<CheckoutPaymentState> emit,
  ) async {
    if (event.token.cancelled) {
      // Re-enter the idle picker with the current summary if we have
      // one; bloc re-fetches summary on cancel via the screen layer
      // routing.
      return;
    }
    emit(const CheckoutPaymentSubmitting());
    try {
      final summary = await _gateway.patchPaymentMethod(
        sessionId: sessionId,
        method: event.method,
        providerToken: event.token.providerToken,
        bankTransferReference: event.token.bankTransferReference,
      );
      emit(CheckoutPaymentSubmitted(summary));
    } on CheckoutDriftException catch (e) {
      emit(CheckoutPaymentConflict(driftFrom(e)));
    } on Object catch (e) {
      emit(CheckoutPaymentFailure(reason: e.toString()));
    }
  }
}
