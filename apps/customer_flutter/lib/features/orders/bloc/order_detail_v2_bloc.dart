import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/order_models.dart';
import '../data/orders_gateway.dart';

@immutable
sealed class OrderDetailV2State {
  const OrderDetailV2State();
}

class OrderDetailLoading extends OrderDetailV2State {
  const OrderDetailLoading();
}

/// The detail loads in two phases: the order itself first (so the page
/// can render immediately), then the return-eligibility call refines
/// the Return CTA when its response comes back. `eligibility == null`
/// while the call is in flight.
class OrderDetailLoaded extends OrderDetailV2State {
  const OrderDetailLoaded({
    required this.order,
    this.eligibility,
  });

  final OrderDetail order;
  final ReturnEligibility? eligibility;

  OrderDetailLoaded copyWith({
    OrderDetail? order,
    ReturnEligibility? eligibility,
  }) {
    return OrderDetailLoaded(
      order: order ?? this.order,
      eligibility: eligibility ?? this.eligibility,
    );
  }
}

class OrderDetailFailure extends OrderDetailV2State {
  const OrderDetailFailure({required this.reason});
  final String reason;
}

@immutable
sealed class OrderDetailV2Event {
  const OrderDetailV2Event();
}

class OrderDetailStarted extends OrderDetailV2Event {
  const OrderDetailStarted();
}

class OrderDetailRefreshed extends OrderDetailV2Event {
  const OrderDetailRefreshed();
}

class OrderDetailV2Bloc extends Bloc<OrderDetailV2Event, OrderDetailV2State> {
  OrderDetailV2Bloc({required OrdersGateway gateway, required this.orderId})
      : _gateway = gateway,
        super(const OrderDetailLoading()) {
    on<OrderDetailStarted>(_load);
    on<OrderDetailRefreshed>(_load);
  }

  final OrdersGateway _gateway;
  final String orderId;

  Future<void> _load(
    OrderDetailV2Event event,
    Emitter<OrderDetailV2State> emit,
  ) async {
    emit(const OrderDetailLoading());
    try {
      final order = await _gateway.getById(orderId);
      // Render the detail immediately, then fetch eligibility lazily.
      emit(OrderDetailLoaded(order: order));
      if (!order.actions.canReturn) {
        // Skip the round-trip when the server already says Return is
        // not on the table; saves an unnecessary call.
        return;
      }
      try {
        final eligibility = await _gateway.getReturnEligibility(orderId);
        // Only update if we haven't been bumped to another state by
        // a concurrent refresh.
        final cur = state;
        if (cur is OrderDetailLoaded && cur.order.id == orderId) {
          emit(cur.copyWith(eligibility: eligibility));
        }
      } on Object {
        // Eligibility is a refinement, not a blocker — failure leaves
        // the Return CTA gated by `actions.canReturn` alone.
      }
    } on Object catch (e) {
      emit(OrderDetailFailure(reason: e.toString()));
    }
  }
}
