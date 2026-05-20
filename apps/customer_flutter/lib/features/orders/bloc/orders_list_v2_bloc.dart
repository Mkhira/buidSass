import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/order_models.dart';
import '../data/orders_gateway.dart';

@immutable
sealed class OrdersListV2State {
  const OrdersListV2State({required this.filter});
  final OrdersListFilter filter;
}

class OrdersListLoading extends OrdersListV2State {
  const OrdersListLoading({required super.filter});
}

class OrdersListLoaded extends OrdersListV2State {
  const OrdersListLoaded({
    required super.filter,
    required this.items,
    required this.totalCount,
    this.isLoadingMore = false,
  });
  final List<OrderListItem> items;
  final int totalCount;
  final bool isLoadingMore;

  bool get hasMore => filter.page * filter.pageSize < totalCount;

  OrdersListLoaded copyWith({
    List<OrderListItem>? items,
    int? totalCount,
    bool? isLoadingMore,
    OrdersListFilter? filter,
  }) {
    return OrdersListLoaded(
      filter: filter ?? this.filter,
      items: items ?? this.items,
      totalCount: totalCount ?? this.totalCount,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
    );
  }
}

class OrdersListEmpty extends OrdersListV2State {
  const OrdersListEmpty({required super.filter});
}

class OrdersListFailure extends OrdersListV2State {
  const OrdersListFailure({required super.filter, required this.reason});
  final String reason;
}

@immutable
sealed class OrdersListV2Event {
  const OrdersListV2Event();
}

class OrdersListStarted extends OrdersListV2Event {
  const OrdersListStarted();
}

class OrdersListFilterChanged extends OrdersListV2Event {
  const OrdersListFilterChanged(this.status);

  /// `null` clears the filter (the "All" chip).
  final String? status;
}

class OrdersListPageRequested extends OrdersListV2Event {
  const OrdersListPageRequested();
}

class OrdersListRefreshed extends OrdersListV2Event {
  const OrdersListRefreshed();
}

class OrdersListV2Bloc extends Bloc<OrdersListV2Event, OrdersListV2State> {
  OrdersListV2Bloc({required OrdersGateway gateway})
      : _gateway = gateway,
        super(const OrdersListLoading(filter: OrdersListFilter())) {
    on<OrdersListStarted>(_onStarted);
    on<OrdersListFilterChanged>(_onFilterChanged);
    on<OrdersListPageRequested>(_onPageRequested);
    on<OrdersListRefreshed>(_onRefreshed);
  }

  final OrdersGateway _gateway;

  Future<void> _onStarted(
    OrdersListStarted event,
    Emitter<OrdersListV2State> emit,
  ) async {
    await _refresh(state.filter, emit);
  }

  Future<void> _onFilterChanged(
    OrdersListFilterChanged event,
    Emitter<OrdersListV2State> emit,
  ) async {
    await _refresh(
      state.filter.copyWith(status: event.status, page: 1),
      emit,
    );
  }

  Future<void> _onRefreshed(
    OrdersListRefreshed event,
    Emitter<OrdersListV2State> emit,
  ) async {
    await _refresh(state.filter.copyWith(page: 1), emit);
  }

  Future<void> _refresh(
    OrdersListFilter filter,
    Emitter<OrdersListV2State> emit,
  ) async {
    emit(OrdersListLoading(filter: filter));
    try {
      final page = await _gateway.list(filter);
      if (page.items.isEmpty) {
        emit(OrdersListEmpty(filter: filter));
        return;
      }
      emit(OrdersListLoaded(
        filter: filter,
        items: page.items,
        totalCount: page.totalCount,
      ));
    } on Object catch (e) {
      emit(OrdersListFailure(filter: filter, reason: e.toString()));
    }
  }

  Future<void> _onPageRequested(
    OrdersListPageRequested event,
    Emitter<OrdersListV2State> emit,
  ) async {
    final s = state;
    if (s is! OrdersListLoaded || !s.hasMore || s.isLoadingMore) return;
    emit(s.copyWith(isLoadingMore: true));
    try {
      final nextFilter = s.filter.copyWith(page: s.filter.page + 1);
      final page = await _gateway.list(nextFilter);
      emit(s.copyWith(
        filter: nextFilter,
        items: [...s.items, ...page.items],
        totalCount: page.totalCount,
        isLoadingMore: false,
      ));
    } on Object catch (e) {
      emit(OrdersListFailure(filter: s.filter, reason: e.toString()));
    }
  }
}
