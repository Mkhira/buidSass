import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:stream_transform/stream_transform.dart';

import '../../checkout/data/checkout_gateway.dart';
import '../../checkout/data/models/checkout_models.dart';
import '../data/cart_store.dart';
import '../data/models/cart_store_models.dart';

// ===== State =====

@immutable
sealed class CartV2State {
  const CartV2State();
}

class CartV2Loading extends CartV2State {
  const CartV2Loading();
}

class CartV2Empty extends CartV2State {
  const CartV2Empty();
}

class CartV2Loaded extends CartV2State {
  const CartV2Loaded({
    required this.snapshot,
    required this.totals,
    this.couponError,
    this.isQuoteInFlight = false,
    this.unavailableProductIds = const {},
  });

  final CartSnapshot snapshot;
  final CheckoutTotals totals;
  final String? couponError;
  final bool isQuoteInFlight;

  /// Product ids that came back as `inStock=false` from the latest
  /// availability batch. The line is rendered strikethrough with a
  /// Remove CTA; Proceed is disabled until they're resolved (BR-9).
  final Set<String> unavailableProductIds;

  bool get hasUnavailable => unavailableProductIds.isNotEmpty;

  CartV2Loaded copyWith({
    CartSnapshot? snapshot,
    CheckoutTotals? totals,
    String? couponError,
    bool clearCouponError = false,
    bool? isQuoteInFlight,
    Set<String>? unavailableProductIds,
  }) {
    return CartV2Loaded(
      snapshot: snapshot ?? this.snapshot,
      totals: totals ?? this.totals,
      couponError: clearCouponError ? null : (couponError ?? this.couponError),
      isQuoteInFlight: isQuoteInFlight ?? this.isQuoteInFlight,
      unavailableProductIds:
          unavailableProductIds ?? this.unavailableProductIds,
    );
  }
}

class CartV2Failure extends CartV2State {
  const CartV2Failure({required this.reason, this.correlationId});
  final String reason;
  final String? correlationId;
}

class CartV2Proceeding extends CartV2State {
  const CartV2Proceeding(this.sessionId);
  final String sessionId;
}

// ===== Events =====

@immutable
sealed class CartV2Event {
  const CartV2Event();
}

class CartStarted extends CartV2Event {
  const CartStarted();
}

class CartLineQtyChanged extends CartV2Event {
  const CartLineQtyChanged({required this.productId, required this.qty});
  final String productId;
  final int qty;
}

class CartLineRemoved extends CartV2Event {
  const CartLineRemoved(this.productId);
  final String productId;
}

class CartCouponApplied extends CartV2Event {
  const CartCouponApplied(this.code);
  final String code;
}

class CartCouponCleared extends CartV2Event {
  const CartCouponCleared();
}

class CartRefreshedV2 extends CartV2Event {
  const CartRefreshedV2();
}

class CartProceedRequested extends CartV2Event {
  const CartProceedRequested({this.buyerKind = 'consumer'});
  final String buyerKind;
}

// ===== Bloc =====

class CartV2Bloc extends Bloc<CartV2Event, CartV2State> {
  CartV2Bloc({
    required CartStore store,
    required CheckoutGateway gateway,
    required String Function() marketProvider,
    Duration quoteDebounce = const Duration(milliseconds: 300),
  })  : _store = store,
        _gateway = gateway,
        _market = marketProvider,
        super(const CartV2Loading()) {
    on<CartStarted>(_onStarted);
    on<CartLineQtyChanged>(
      _onQtyChanged,
      transformer: (events, mapper) =>
          events.debounce(quoteDebounce).switchMap(mapper),
    );
    on<CartLineRemoved>(_onLineRemoved);
    on<CartCouponApplied>(_onCouponApplied);
    on<CartCouponCleared>(_onCouponCleared);
    on<CartRefreshedV2>(_onRefreshed);
    on<CartProceedRequested>(_onProceed);
  }

  final CartStore _store;
  final CheckoutGateway _gateway;
  final String Function() _market;

  Future<void> _onStarted(CartStarted event, Emitter<CartV2State> emit) async {
    await _store.load();
    await _quote(emit);
  }

  Future<void> _onQtyChanged(
      CartLineQtyChanged event, Emitter<CartV2State> emit) async {
    await _store.setQty(productId: event.productId, qty: event.qty);
    await _quote(emit);
  }

  Future<void> _onLineRemoved(
      CartLineRemoved event, Emitter<CartV2State> emit) async {
    await _store.removeLine(event.productId);
    await _quote(emit);
  }

  Future<void> _onCouponApplied(
      CartCouponApplied event, Emitter<CartV2State> emit) async {
    final code = event.code.trim();
    if (code.isEmpty) return;
    // Optimistic apply — store the code first so the input renders the
    // chip immediately, then run the quote. On 422 from the server
    // (`couponValid=false`) we surface the message inline and the chip
    // gets the validation styling.
    await _store.applyCoupon(code);
    await _quote(emit);
  }

  Future<void> _onCouponCleared(
      CartCouponCleared event, Emitter<CartV2State> emit) async {
    await _store.clearCoupon();
    await _quote(emit);
  }

  Future<void> _onRefreshed(
      CartRefreshedV2 event, Emitter<CartV2State> emit) async {
    await _quote(emit);
  }

  Future<void> _onProceed(
      CartProceedRequested event, Emitter<CartV2State> emit) async {
    final snapshot = _store.snapshot;
    if (snapshot.isEmpty) return;
    final current = state;
    if (current is CartV2Loaded && current.hasUnavailable) return;
    try {
      final result = await _gateway.createSession(CreateSessionRequest(
        lines: snapshot.lines
            .map((l) => CreateSessionLine(productId: l.productId, qty: l.qty))
            .toList(growable: false),
        couponCode: snapshot.couponCode,
        buyerKind: event.buyerKind,
        marketCode: _market(),
      ));
      emit(CartV2Proceeding(result.sessionId));
    } on Object catch (e) {
      emit(CartV2Failure(reason: e.toString()));
    }
  }

  Future<void> _quote(Emitter<CartV2State> emit) async {
    final snapshot = _store.snapshot;
    if (snapshot.isEmpty) {
      emit(const CartV2Empty());
      return;
    }
    final current = state;
    if (current is CartV2Loaded) {
      emit(current.copyWith(isQuoteInFlight: true, snapshot: snapshot));
    }
    try {
      final result = await _gateway.priceCart(PriceCartRequest(
        lines: snapshot.lines
            .map((l) => CreateSessionLine(productId: l.productId, qty: l.qty))
            .toList(growable: false),
        couponCode: snapshot.couponCode,
        buyerKind: 'consumer',
        marketCode: _market(),
      ));
      emit(CartV2Loaded(
        snapshot: snapshot,
        totals: result.totals,
        couponError: result.couponValid ? null : result.couponMessage,
        isQuoteInFlight: false,
        unavailableProductIds:
            current is CartV2Loaded ? current.unavailableProductIds : const {},
      ));
    } on Object catch (e) {
      emit(CartV2Failure(reason: e.toString()));
    }
  }
}
