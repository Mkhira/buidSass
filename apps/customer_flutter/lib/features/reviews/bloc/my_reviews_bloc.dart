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

  /// Monotonic refresh counter — bumped on every refresh/filter start.
  /// Stale async responses (e.g. fast filter switch while the previous
  /// request is still in-flight) drop their result rather than
  /// overwriting newer list state.
  int _refreshVersion = 0;

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
    final version = ++_refreshVersion;
    emit(MyReviewsLoading(filter: filter));
    try {
      final page = await _gateway.listMine(filter);
      if (version != _refreshVersion) return; // newer refresh in flight
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
      if (version != _refreshVersion) return;
      emit(MyReviewsFailure(filter: filter, reason: err.toString()));
    }
  }

  Future<void> _onPageRequested(
    MyReviewsPageRequested e,
    Emitter<MyReviewsState> emit,
  ) async {
    final s = state;
    if (s is! MyReviewsLoaded || !s.hasMore || s.isLoadingMore) return;
    // Snapshot the version: if a refresh kicks off mid-pagination, drop
    // the page result rather than splicing it onto a different list.
    final version = _refreshVersion;
    emit(s.copyWith(isLoadingMore: true));
    try {
      final nextFilter = s.filter.copyWith(page: s.filter.page + 1);
      final page = await _gateway.listMine(nextFilter);
      if (version != _refreshVersion) return;
      // The list might have been replaced under us (filter chip), so
      // re-read state before splicing.
      final current = state;
      if (current is! MyReviewsLoaded) return;
      emit(current.copyWith(
        filter: nextFilter,
        items: [...current.items, ...page.items],
        totalCount: page.totalCount,
        isLoadingMore: false,
      ));
    } on Object catch (err) {
      // Match returns/orders pattern: a pagination error must NOT wipe
      // the loaded list. Stop the spinner; the user can pull-to-refresh.
      if (version != _refreshVersion) return;
      // ignore: avoid_print
      print('MyReviewsBloc pagination error (preserving loaded list): $err');
      final current = state;
      if (current is MyReviewsLoaded) {
        emit(current.copyWith(isLoadingMore: false));
      }
    }
  }
}
