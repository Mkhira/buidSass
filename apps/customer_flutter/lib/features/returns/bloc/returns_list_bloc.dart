import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/return_models.dart';
import '../data/returns_gateway.dart';

@immutable
sealed class ReturnsListState {
  const ReturnsListState({required this.filter});
  final ReturnsListFilter filter;
}

class ReturnsListLoading extends ReturnsListState {
  const ReturnsListLoading({required super.filter});
}

class ReturnsListLoaded extends ReturnsListState {
  const ReturnsListLoaded({
    required super.filter,
    required this.items,
    required this.totalCount,
    this.isLoadingMore = false,
  });
  final List<ReturnListItem> items;
  final int totalCount;
  final bool isLoadingMore;

  bool get hasMore => filter.page * filter.pageSize < totalCount;

  ReturnsListLoaded copyWith({
    List<ReturnListItem>? items,
    int? totalCount,
    bool? isLoadingMore,
    ReturnsListFilter? filter,
  }) {
    return ReturnsListLoaded(
      filter: filter ?? this.filter,
      items: items ?? this.items,
      totalCount: totalCount ?? this.totalCount,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
    );
  }
}

class ReturnsListEmpty extends ReturnsListState {
  const ReturnsListEmpty({required super.filter});
}

class ReturnsListFailure extends ReturnsListState {
  const ReturnsListFailure({required super.filter, required this.reason});
  final String reason;
}

@immutable
sealed class ReturnsListEvent {
  const ReturnsListEvent();
}

class ReturnsListStarted extends ReturnsListEvent {
  const ReturnsListStarted();
}

class ReturnsListFilterChanged extends ReturnsListEvent {
  const ReturnsListFilterChanged(this.status);

  /// `null` clears the filter (the "All" chip). Wire enum value otherwise.
  final String? status;
}

class ReturnsListRefreshed extends ReturnsListEvent {
  const ReturnsListRefreshed();
}

class ReturnsListPageRequested extends ReturnsListEvent {
  const ReturnsListPageRequested();
}

/// Bloc for S-6.1 returns list. Mirrors the Phase 5
/// [OrdersListV2Bloc] shape so wiring (refresh indicators,
/// pagination controllers, filter chips) carries over verbatim.
class ReturnsListBloc extends Bloc<ReturnsListEvent, ReturnsListState> {
  ReturnsListBloc({required ReturnsGateway gateway})
      : _gateway = gateway,
        super(const ReturnsListLoading(filter: ReturnsListFilter())) {
    on<ReturnsListStarted>(_onStarted);
    on<ReturnsListFilterChanged>(_onFilterChanged);
    on<ReturnsListRefreshed>(_onRefreshed);
    on<ReturnsListPageRequested>(_onPageRequested);
  }

  final ReturnsGateway _gateway;

  Future<void> _onStarted(
    ReturnsListStarted event,
    Emitter<ReturnsListState> emit,
  ) async {
    await _refresh(state.filter, emit);
  }

  Future<void> _onFilterChanged(
    ReturnsListFilterChanged event,
    Emitter<ReturnsListState> emit,
  ) async {
    await _refresh(
      state.filter.copyWith(status: event.status, page: 1),
      emit,
    );
  }

  Future<void> _onRefreshed(
    ReturnsListRefreshed event,
    Emitter<ReturnsListState> emit,
  ) async {
    await _refresh(state.filter.copyWith(page: 1), emit);
  }

  Future<void> _refresh(
    ReturnsListFilter filter,
    Emitter<ReturnsListState> emit,
  ) async {
    emit(ReturnsListLoading(filter: filter));
    try {
      final page = await _gateway.list(filter);
      if (page.items.isEmpty) {
        emit(ReturnsListEmpty(filter: filter));
        return;
      }
      emit(ReturnsListLoaded(
        filter: filter,
        items: page.items,
        totalCount: page.totalCount,
      ));
    } on Object catch (e) {
      emit(ReturnsListFailure(filter: filter, reason: e.toString()));
    }
  }

  Future<void> _onPageRequested(
    ReturnsListPageRequested event,
    Emitter<ReturnsListState> emit,
  ) async {
    final s = state;
    if (s is! ReturnsListLoaded || !s.hasMore || s.isLoadingMore) return;
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
      // CodeRabbit feedback: a pagination error must NOT wipe the
      // already-loaded list. Stop the spinner and keep the existing
      // items + filter so the user can scroll/retry. The bloc state
      // still surfaces the error via debug log; UI relies on the
      // pull-to-refresh cycle for the next visible attempt.
      // ignore: avoid_print
      print('ReturnsListBloc pagination error (preserving loaded list): $e');
      emit(s.copyWith(isLoadingMore: false));
    }
  }
}
