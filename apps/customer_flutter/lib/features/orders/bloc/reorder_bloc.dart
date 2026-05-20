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

  /// Parse a decimal-string amount (server convention, e.g. "120.00",
  /// "120", "120.5") into minor units (cents/halalas). Returns `null`
  /// on any parse failure so the caller can skip the line cleanly
  /// rather than silently zero out the price.
  static int? _parseMinor(String amount) {
    final trimmed = amount.trim();
    if (trimmed.isEmpty) return null;
    final parts = trimmed.split('.');
    if (parts.length > 2) return null;
    final whole = int.tryParse(parts[0]);
    if (whole == null) return null;
    if (parts.length == 1) return whole * 100;
    final fracRaw = parts[1];
    if (fracRaw.isEmpty || fracRaw.length > 2) {
      // 1-2 fractional digits only — anything else is unexpected for
      // a 2-decimal currency. Reject rather than guess.
      if (fracRaw.length > 2) return null;
    }
    final fracPadded = fracRaw.padRight(2, '0').substring(0, 2);
    final frac = int.tryParse(fracPadded);
    if (frac == null) return null;
    final sign = whole.isNegative ? -1 : 1;
    return whole * 100 + sign * frac;
  }

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
      var added = 0;
      var skipped = s.result.unavailable.length;
      for (final line in s.result.available) {
        // Parse the server-formatted decimal string (e.g. "60.00")
        // into minor units WITHOUT going through double — avoids
        // floating-point rounding that could corrupt cart totals.
        // Skip lines we can't parse cleanly; they roll into the
        // skipped count so the toast stays accurate.
        final minor = _parseMinor(line.priceHint.amount);
        if (minor == null) {
          skipped += 1;
          continue;
        }
        await _cart.addLine(CartStoreLine(
          productId: line.productId,
          slug: line.productId,
          name: line.name,
          imageUrl: '',
          qty: line.qty,
          unitPriceMinor: minor,
          currency: line.priceHint.currency,
        ));
        added += 1;
      }
      emit(ReorderDone(addedCount: added, skippedCount: skipped));
    } on Object catch (e) {
      emit(ReorderFailure(reason: e.toString()));
    }
  }
}
