import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../cart/data/cart_store.dart';
import '../../cart/data/models/cart_store_models.dart';
import '../data/models/order_models.dart';
import '../data/orders_gateway.dart';

@immutable
sealed class ReorderState {
  const ReorderState();
}

class ReorderLoading extends ReorderState {
  const ReorderLoading();
}

class ReorderLoaded extends ReorderState {
  const ReorderLoaded({required this.result});
  final ReorderResult result;

  bool get hasAvailable => result.available.isNotEmpty;
}

class ReorderConfirming extends ReorderState {
  const ReorderConfirming();
}

class ReorderDone extends ReorderState {
  const ReorderDone({required this.addedCount, required this.skippedCount});
  final int addedCount;
  final int skippedCount;
}

class ReorderFailure extends ReorderState {
  const ReorderFailure({required this.reason});
  final String reason;
}

@immutable
sealed class ReorderEvent {
  const ReorderEvent();
}

class ReorderStarted extends ReorderEvent {
  const ReorderStarted();
}

class ReorderAddToCartConfirmed extends ReorderEvent {
  const ReorderAddToCartConfirmed();
}

class ReorderBloc extends Bloc<ReorderEvent, ReorderState> {
  ReorderBloc({
    required OrdersGateway gateway,
    required CartStore cartStore,
    required this.orderId,
  })  : _gateway = gateway,
        _cart = cartStore,
        super(const ReorderLoading()) {
    on<ReorderStarted>(_onStarted);
    on<ReorderAddToCartConfirmed>(_onConfirmed);
  }

  final OrdersGateway _gateway;
  final CartStore _cart;
  final String orderId;

  Future<void> _onStarted(
    ReorderStarted event,
    Emitter<ReorderState> emit,
  ) async {
    emit(const ReorderLoading());
    try {
      final result = await _gateway.reorder(orderId);
      emit(ReorderLoaded(result: result));
    } on Object catch (e) {
      emit(ReorderFailure(reason: e.toString()));
    }
  }

  Future<void> _onConfirmed(
    ReorderAddToCartConfirmed event,
    Emitter<ReorderState> emit,
  ) async {
    final s = state;
    if (s is! ReorderLoaded || !s.hasAvailable) return;
    emit(const ReorderConfirming());
    try {
      await _cart.load();
      for (final line in s.result.available) {
        // Merge with existing lines: CartStore.addLine increments qty
        // when productId matches, otherwise appends.
        await _cart.addLine(CartStoreLine(
          productId: line.productId,
          slug: line.productId,
          name: line.name,
          imageUrl: '',
          qty: line.qty,
          unitPriceMinor:
              ((double.tryParse(line.priceHint.amount) ?? 0) * 100).round(),
          currency: line.priceHint.currency,
        ));
      }
      emit(ReorderDone(
        addedCount: s.result.available.length,
        skippedCount: s.result.unavailable.length,
      ));
    } on Object catch (e) {
      emit(ReorderFailure(reason: e.toString()));
    }
  }
}
