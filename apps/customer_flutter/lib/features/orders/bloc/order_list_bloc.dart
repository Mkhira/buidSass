import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/order_view_models.dart';
import '../data/orders_repository.dart';

/// SM-4: OrderListFilter — Idle, Loading, Loaded, Empty, Error.
@immutable
sealed class OrderListState {
  const OrderListState({required this.filter});
  final OrderListFilter filter;
}

class OrderListIdle extends OrderListState {
  const OrderListIdle({required super.filter});
}

class OrderListLoading extends OrderListState {
  const OrderListLoading({required super.filter});
}

class OrderListLoaded extends OrderListState {
  const OrderListLoaded({
    required super.filter,
    required this.items,
    required this.nextCursor,
    this.isLoadingMore = false,
  });
  final List<OrderListItem> items;
  final String? nextCursor;
  final bool isLoadingMore;

  bool get hasMore => nextCursor != null;
}

class OrderListEmpty extends OrderListState {
  const OrderListEmpty({required super.filter});
}

class OrderListError extends OrderListState {
  const OrderListError({required super.filter, required this.reason});
  final String reason;
}

@immutable
sealed class OrderListEvent {
  const OrderListEvent();
}

class OrderListFilterChanged extends OrderListEvent {
  const OrderListFilterChanged(this.filter);
  final OrderListFilter filter;
}

class OrderListRefreshTapped extends OrderListEvent {
  const OrderListRefreshTapped();
}

class OrderListPageRequested extends OrderListEvent {
  const OrderListPageRequested();
}

class OrderListBloc extends Bloc<OrderListEvent, OrderListState> {
  OrderListBloc({required OrdersRepository repository})
      : _repository = repository,
        super(const OrderListIdle(filter: OrderListFilter())) {
    on<OrderListFilterChanged>(_onFilterChanged);
    on<OrderListRefreshTapped>(_onRefresh);
    on<OrderListPageRequested>(_onPage);
  }

  final OrdersRepository _repository;

  Future<void> _onFilterChanged(
    OrderListFilterChanged event,
    Emitter<OrderListState> emit,
  ) async {
    await _refresh(event.filter, emit);
  }

  Future<void> _onRefresh(
    OrderListRefreshTapped event,
    Emitter<OrderListState> emit,
  ) async {
    await _refresh(state.filter, emit);
  }

  Future<void> _refresh(OrderListFilter filter, Emitter<OrderListState> emit) async {
    emit(OrderListLoading(filter: filter));
    try {
      final page = await _repository.fetchList(filter: filter);
      if (page.items.isEmpty) {
        emit(OrderListEmpty(filter: filter));
      } else {
        emit(OrderListLoaded(
          filter: filter,
          items: page.items,
          nextCursor: page.nextCursor,
        ));
      }
    } on Object catch (e) {
      emit(OrderListError(filter: filter, reason: e.toString()));
    }
  }

  Future<void> _onPage(
    OrderListPageRequested event,
    Emitter<OrderListState> emit,
  ) async {
    final s = state;
    if (s is! OrderListLoaded || !s.hasMore || s.isLoadingMore) return;
    emit(OrderListLoaded(
      filter: s.filter,
      items: s.items,
      nextCursor: s.nextCursor,
      isLoadingMore: true,
    ));
    try {
      final page = await _repository.fetchList(
        filter: s.filter,
        cursor: s.nextCursor,
      );
      emit(OrderListLoaded(
        filter: s.filter,
        items: [...s.items, ...page.items],
        nextCursor: page.nextCursor,
      ));
    } on Object catch (e) {
      emit(OrderListError(filter: s.filter, reason: e.toString()));
    }
  }
}
