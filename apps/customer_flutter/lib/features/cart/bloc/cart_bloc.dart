import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../core/auth/auth_session_bloc.dart';
import '../../../core/cart/anonymous_cart_token_store.dart';
import '../data/cart_repository.dart';
import '../data/cart_view_models.dart';
import '../services/cart_merge_service.dart';

@immutable
sealed class CartState {
  const CartState();
}

class CartEmpty extends CartState {
  const CartEmpty();
}

class CartLoading extends CartState {
  const CartLoading();
}

class CartLoaded extends CartState {
  const CartLoaded(this.cart, {this.conflicts = const []});
  final CartViewModel cart;
  final List<CartConflictReport> conflicts;
}

class CartMutating extends CartState {
  const CartMutating(this.previous);
  final CartLoaded previous;
}

class CartOutOfSync extends CartState {
  const CartOutOfSync(this.previous);
  final CartLoaded previous;
}

class CartError extends CartState {
  const CartError(this.reason);
  final String reason;
}

@immutable
sealed class CartEvent {
  const CartEvent();
}

class CartRefreshed extends CartEvent {
  const CartRefreshed();
}

class LineQuantityChanged extends CartEvent {
  const LineQuantityChanged({required this.productId, required this.quantity});
  final String productId;
  final int quantity;
}

class LineRemoved extends CartEvent {
  const LineRemoved(this.productId);
  final String productId;
}

class CartClaimRequested extends CartEvent {
  const CartClaimRequested();
}

class CartBloc extends Bloc<CartEvent, CartState> {
  CartBloc({
    required CartRepository repository,
    required AnonymousCartTokenStore tokenStore,
    required AuthSessionBloc authSessionBloc,
    CartMergeService mergeService = const CartMergeService(),
  })  : _repository = repository,
        _tokenStore = tokenStore,
        _mergeService = mergeService,
        super(const CartLoading()) {
    on<CartRefreshed>(_onRefreshed);
    on<LineQuantityChanged>(_onLineQuantityChanged);
    on<LineRemoved>(_onLineRemoved);
    on<CartClaimRequested>(_onClaimRequested);

    _authSub = authSessionBloc.stream.listen((auth) {
      if (auth is AuthAuthenticated) {
        add(const CartClaimRequested());
      }
    });
  }

  final CartRepository _repository;
  final AnonymousCartTokenStore _tokenStore;
  final CartMergeService _mergeService;
  StreamSubscription<AuthSessionState>? _authSub;

  Future<void> _onRefreshed(
    CartRefreshed event,
    Emitter<CartState> emit,
  ) async {
    emit(const CartLoading());
    try {
      final cart = await _repository.fetchCart();
      emit(cart.isEmpty ? const CartEmpty() : CartLoaded(cart));
    } on Object catch (e) {
      emit(CartError(e.toString()));
    }
  }

  Future<void> _onLineQuantityChanged(
    LineQuantityChanged event,
    Emitter<CartState> emit,
  ) async {
    final s = state;
    if (s is! CartLoaded) return;
    emit(CartMutating(s));
    try {
      final cart = await _repository.updateLineQuantity(
        productId: event.productId,
        quantity: event.quantity,
      );
      emit(cart.isEmpty ? const CartEmpty() : CartLoaded(cart));
    } on CartRevisionMismatchException {
      emit(CartOutOfSync(s));
    } on Object catch (e) {
      emit(CartError(e.toString()));
    }
  }

  Future<void> _onLineRemoved(
    LineRemoved event,
    Emitter<CartState> emit,
  ) async {
    final s = state;
    if (s is! CartLoaded) return;
    emit(CartMutating(s));
    try {
      final cart = await _repository.removeLine(event.productId);
      emit(cart.isEmpty ? const CartEmpty() : CartLoaded(cart));
    } on CartRevisionMismatchException {
      emit(CartOutOfSync(s));
    } on Object catch (e) {
      emit(CartError(e.toString()));
    }
  }

  /// FR-013a — invoked on every AuthSession transition into Authenticated.
  /// Calls spec 009's claim endpoint with the anonymous token; falls back
  /// to client-side merge (FR-013b) when the response indicates a gap.
  Future<void> _onClaimRequested(
    CartClaimRequested event,
    Emitter<CartState> emit,
  ) async {
    final token = await _tokenStore.readToken();
    if (token == null) return;
    try {
      final outcome = await _repository.claimAnonymousCart(token: token);
      if (outcome.gap) {
        // Best-effort merge fallback. The guest-side cart and authenticated
        // cart are read separately; in v1 both are empty until spec 009
        // ships, so this lands a clean Authenticated cart.
        final authenticated = await _repository.fetchCart();
        final merged = _mergeService.merge(
          guest: const [],
          authenticated: authenticated.lines,
          restrictedProductIds: const {},
          outOfStockProductIds: const {},
          isCustomerVerified: false,
        );
        final next = CartViewModel(
          revision: authenticated.revision,
          lines: merged.lines,
          totals: authenticated.totals,
          tokenKind: CartTokenKind.authenticated,
        );
        emit(next.isEmpty
            ? const CartEmpty()
            : CartLoaded(next, conflicts: merged.conflicts));
        return;
      }
      emit(outcome.cart.isEmpty
          ? const CartEmpty()
          : CartLoaded(outcome.cart, conflicts: outcome.conflicts));
    } on Object catch (e) {
      emit(CartError(e.toString()));
    }
  }

  @override
  Future<void> close() async {
    await _authSub?.cancel();
    return super.close();
  }
}
