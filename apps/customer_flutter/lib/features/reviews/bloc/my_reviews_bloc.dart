import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/review_models.dart';
import '../data/reviews_customer_gateway.dart';

@immutable
sealed class MyReviewsState {
  const MyReviewsState({required this.filter});
  final MyReviewsFilter filter;
}

class MyReviewsLoading extends MyReviewsState {
  const MyReviewsLoading({required super.filter});
}

class MyReviewsLoaded extends MyReviewsState {
  const MyReviewsLoaded({
    required super.filter,
    required this.items,
    required this.totalCount,
    this.isLoadingMore = false,
  });

  final List<MyReviewListItem> items;
  final int totalCount;
  final bool isLoadingMore;

  bool get hasMore => filter.page * filter.pageSize < totalCount;

  MyReviewsLoaded copyWith({
    List<MyReviewListItem>? items,
    int? totalCount,
    bool? isLoadingMore,
    MyReviewsFilter? filter,
  }) {
    return MyReviewsLoaded(
      filter: filter ?? this.filter,
      items: items ?? this.items,
      totalCount: totalCount ?? this.totalCount,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
    );
  }
}

class MyReviewsEmpty extends MyReviewsState {
  const MyReviewsEmpty({required super.filter});
}

class MyReviewsFailure extends MyReviewsState {
  const MyReviewsFailure({required super.filter, required this.reason});
  final String reason;
}

@immutable
sealed class MyReviewsEvent {
  const MyReviewsEvent();
}

class MyReviewsStarted extends MyReviewsEvent {
  const MyReviewsStarted();
}

class MyReviewsFilterChanged extends MyReviewsEvent {
  const MyReviewsFilterChanged(this.state);
  final String? state;
}

class MyReviewsRefreshed extends MyReviewsEvent {
  const MyReviewsRefreshed();
}

class MyReviewsPageRequested extends MyReviewsEvent {
  const MyReviewsPageRequested();
}

/// Bloc for S-7.6 — mirrors the ReturnsListBloc shape so pagination /
/// pull-to-refresh / filter-chip wiring carries over.
class MyReviewsBloc extends Bloc<MyReviewsEvent, MyReviewsState> {
  MyReviewsBloc({required ReviewsCustomerGateway gateway})
      : _gateway = gateway,
        super(const MyReviewsLoading(filter: MyReviewsFilter())) {
    on<MyReviewsStarted>(_onStarted);
    on<MyReviewsFilterChanged>(_onFilterChanged);
    on<MyReviewsRefreshed>(_onRefreshed);
    on<MyReviewsPageRequested>(_onPageRequested);
  }

  final ReviewsCustomerGateway _gateway;

  Future<void> _onStarted(MyReviewsStarted e, Emitter<MyReviewsState> emit) =>
      _refresh(state.filter, emit);

  Future<void> _onFilterChanged(
    MyReviewsFilterChanged e,
    Emitter<MyReviewsState> emit,
  ) =>
      _refresh(state.filter.copyWith(state: e.state, page: 1), emit);

  Future<void> _onRefreshed(
    MyReviewsRefreshed e,
    Emitter<MyReviewsState> emit,
  ) =>
      _refresh(state.filter.copyWith(page: 1), emit);

  Future<void> _refresh(
    MyReviewsFilter filter,
    Emitter<MyReviewsState> emit,
  ) async {
    emit(MyReviewsLoading(filter: filter));
    try {
      final page = await _gateway.listMine(filter);
      if (page.items.isEmpty) {
        emit(MyReviewsEmpty(filter: filter));
        return;
      }
      emit(MyReviewsLoaded(
        filter: filter,
        items: page.items,
        totalCount: page.totalCount,
      ));
    } on Object catch (err) {
      emit(MyReviewsFailure(filter: filter, reason: err.toString()));
    }
  }

  Future<void> _onPageRequested(
    MyReviewsPageRequested e,
    Emitter<MyReviewsState> emit,
  ) async {
    final s = state;
    if (s is! MyReviewsLoaded || !s.hasMore || s.isLoadingMore) return;
    emit(s.copyWith(isLoadingMore: true));
    try {
      final nextFilter = s.filter.copyWith(page: s.filter.page + 1);
      final page = await _gateway.listMine(nextFilter);
      emit(s.copyWith(
        filter: nextFilter,
        items: [...s.items, ...page.items],
        totalCount: page.totalCount,
        isLoadingMore: false,
      ));
    } on Object catch (err) {
      // Match returns/orders pattern: a pagination error must NOT wipe
      // the loaded list. Stop the spinner; the user can pull-to-refresh.
      // ignore: avoid_print
      print('MyReviewsBloc pagination error (preserving loaded list): $err');
      emit(s.copyWith(isLoadingMore: false));
    }
  }
}
