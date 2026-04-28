import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/order_view_models.dart';
import '../data/orders_repository.dart';

@immutable
sealed class OrderDetailState {
  const OrderDetailState();
}

class OrderDetailLoading extends OrderDetailState {
  const OrderDetailLoading();
}

class OrderDetailLoaded extends OrderDetailState {
  const OrderDetailLoaded(this.detail);
  final OrderDetailViewModel detail;
}

class OrderDetailError extends OrderDetailState {
  const OrderDetailError(this.reason);
  final String reason;
}

@immutable
sealed class OrderDetailEvent {
  const OrderDetailEvent();
}

class OrderDetailRequested extends OrderDetailEvent {
  const OrderDetailRequested(this.orderId);
  final String orderId;
}

class OrderDetailRefreshed extends OrderDetailEvent {
  const OrderDetailRefreshed();
}

class OrderDetailBloc extends Bloc<OrderDetailEvent, OrderDetailState> {
  OrderDetailBloc({required OrdersRepository repository})
      : _repository = repository,
        super(const OrderDetailLoading()) {
    on<OrderDetailRequested>(_onRequested);
    on<OrderDetailRefreshed>(_onRefreshed);
  }

  final OrdersRepository _repository;
  String? _orderId;

  Future<void> _onRequested(
    OrderDetailRequested event,
    Emitter<OrderDetailState> emit,
  ) async {
    _orderId = event.orderId;
    emit(const OrderDetailLoading());
    try {
      final detail = await _repository.fetchDetail(event.orderId);
      emit(OrderDetailLoaded(detail));
    } on Object catch (e) {
      emit(OrderDetailError(e.toString()));
    }
  }

  Future<void> _onRefreshed(
    OrderDetailRefreshed event,
    Emitter<OrderDetailState> emit,
  ) async {
    final id = _orderId;
    if (id == null) return;
    try {
      final detail = await _repository.fetchDetail(id);
      emit(OrderDetailLoaded(detail));
    } on Object catch (e) {
      emit(OrderDetailError(e.toString()));
    }
  }
}
