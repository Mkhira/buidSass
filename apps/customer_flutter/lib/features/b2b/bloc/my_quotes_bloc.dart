import 'package:flutter/foundation.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../data/models/quote_models.dart';
import '../data/quotes_gateway.dart';

@immutable
sealed class MyQuotesState {
  const MyQuotesState({required this.filter});
  final QuotesFilter filter;
}

class MyQuotesLoading extends MyQuotesState {
  const MyQuotesLoading({required super.filter});
}

class MyQuotesEmpty extends MyQuotesState {
  const MyQuotesEmpty({required super.filter});
}

class MyQuotesLoaded extends MyQuotesState {
  const MyQuotesLoaded({
    required super.filter,
    required this.items,
    required this.totalCount,
    this.isLoadingMore = false,
  });

  final List<QuoteListItem> items;
  final int totalCount;
  final bool isLoadingMore;

  bool get hasMore => filter.page * filter.pageSize < totalCount;

  MyQuotesLoaded copyWith({
    QuotesFilter? filter,
    List<QuoteListItem>? items,
    int? totalCount,
    bool? isLoadingMore,
  }) {
    return MyQuotesLoaded(
      filter: filter ?? this.filter,
      items: items ?? this.items,
      totalCount: totalCount ?? this.totalCount,
      isLoadingMore: isLoadingMore ?? this.isLoadingMore,
    );
  }
}

class MyQuotesFailure extends MyQuotesState {
  const MyQuotesFailure({required super.filter, required this.reason});
  final String reason;
}

@immutable
sealed class MyQuotesEvent {
  const MyQuotesEvent();
}

class MyQuotesStarted extends MyQuotesEvent {
  const MyQuotesStarted();
}

class MyQuotesFilterChanged extends MyQuotesEvent {
  const MyQuotesFilterChanged(this.state);
  final String? state;
}

class MyQuotesRefreshed extends MyQuotesEvent {
  const MyQuotesRefreshed();
}

class MyQuotesPageRequested extends MyQuotesEvent {
  const MyQuotesPageRequested();
}

/// Bloc for S-8.1 my quotes list. Mirrors MyReviewsBloc shape — adds
/// a monotonic refresh counter so stale async responses can't
/// overwrite newer list state when the filter chip rapid-fires.
class MyQuotesBloc extends Bloc<MyQuotesEvent, MyQuotesState> {
  MyQuotesBloc({required QuotesGateway gateway})
      : _gateway = gateway,
        super(const MyQuotesLoading(filter: QuotesFilter())) {
    on<MyQuotesStarted>(_onStarted);
    on<MyQuotesFilterChanged>(_onFilterChanged);
    on<MyQuotesRefreshed>(_onRefreshed);
    on<MyQuotesPageRequested>(_onPageRequested);
  }

  final QuotesGateway _gateway;
  int _refreshVersion = 0;

  Future<void> _onStarted(MyQuotesStarted e, Emitter<MyQuotesState> emit) =>
      _refresh(state.filter, emit);

  Future<void> _onFilterChanged(
    MyQuotesFilterChanged e,
    Emitter<MyQuotesState> emit,
  ) =>
      _refresh(state.filter.copyWith(state: e.state, page: 1), emit);

  Future<void> _onRefreshed(
    MyQuotesRefreshed e,
    Emitter<MyQuotesState> emit,
  ) =>
      _refresh(state.filter.copyWith(page: 1), emit);

  Future<void> _refresh(
    QuotesFilter filter,
    Emitter<MyQuotesState> emit,
  ) async {
    final version = ++_refreshVersion;
    emit(MyQuotesLoading(filter: filter));
    try {
      final page = await _gateway.list(filter);
      if (version != _refreshVersion) return;
      if (page.items.isEmpty) {
        emit(MyQuotesEmpty(filter: filter));
        return;
      }
      emit(MyQuotesLoaded(
        filter: filter,
        items: page.items,
        totalCount: page.totalCount,
      ));
    } on Object catch (_) {
      if (version != _refreshVersion) return;
      emit(MyQuotesFailure(filter: filter, reason: 'quote.list_failed'));
    }
  }

  Future<void> _onPageRequested(
    MyQuotesPageRequested e,
    Emitter<MyQuotesState> emit,
  ) async {
    final s = state;
    if (s is! MyQuotesLoaded || !s.hasMore || s.isLoadingMore) return;
    final version = _refreshVersion;
    emit(s.copyWith(isLoadingMore: true));
    try {
      final nextFilter = s.filter.copyWith(page: s.filter.page + 1);
      final page = await _gateway.list(nextFilter);
      if (version != _refreshVersion) return;
      final current = state;
      if (current is! MyQuotesLoaded) return;
      emit(current.copyWith(
        filter: nextFilter,
        items: [...current.items, ...page.items],
        totalCount: page.totalCount,
        isLoadingMore: false,
      ));
    } on Object catch (err) {
      if (version != _refreshVersion) return;
      // ignore: avoid_print
      print('MyQuotesBloc pagination error (preserving loaded list): $err');
      final current = state;
      if (current is MyQuotesLoaded) {
        emit(current.copyWith(isLoadingMore: false));
      }
    }
  }
}
