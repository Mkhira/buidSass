import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/reviews/bloc/my_reviews_bloc.dart';
import 'package:customer_flutter/features/reviews/data/models/review_models.dart';
import 'package:customer_flutter/features/reviews/data/reviews_customer_gateway.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeGateway implements ReviewsCustomerGateway {
  _FakeGateway({this.items = const [], this.throwOnList = false});

  final List<MyReviewListItem> items;
  final bool throwOnList;

  final List<MyReviewsFilter> calls = [];

  @override
  Future<MyReviewsPage> listMine(MyReviewsFilter filter) async {
    calls.add(filter);
    if (throwOnList) throw Exception('boom');
    final filtered = filter.state == null
        ? items
        : items.where((i) => i.state == filter.state).toList(growable: false);
    final start = (filter.page - 1) * filter.pageSize;
    final end = (start + filter.pageSize) > filtered.length
        ? filtered.length
        : start + filter.pageSize;
    return MyReviewsPage(
      items: start >= filtered.length ? const [] : filtered.sublist(start, end),
      page: filter.page,
      pageSize: filter.pageSize,
      totalCount: filtered.length,
    );
  }

  // unused
  @override
  Future<CreateReviewResult> submit({
    required CreateReviewRequest request,
    required String idempotencyKey,
  }) =>
      throw UnimplementedError();
  @override
  Future<MyReviewDetail> getMine(String reviewId) => throw UnimplementedError();
  @override
  Future<MyReviewDetail> edit({
    required String reviewId,
    required EditReviewRequest request,
  }) =>
      throw UnimplementedError();
  @override
  Future<List<ReportReason>> getReportReasons() => throw UnimplementedError();
  @override
  Future<ReportReviewResult> report({
    required String reviewId,
    required ReportReviewRequest request,
  }) =>
      throw UnimplementedError();
}

MyReviewListItem _seed(String id, {String state = 'visible'}) =>
    MyReviewListItem(
      id: id,
      productId: 'p-$id',
      productName: 'p-$id',
      rating: 4,
      state: state,
      createdAt: DateTime.utc(2026, 5, 1),
    );

void main() {
  blocTest<MyReviewsBloc, MyReviewsState>(
    'started → loaded',
    build: () => MyReviewsBloc(
      gateway: _FakeGateway(items: [_seed('a'), _seed('b')]),
    ),
    act: (b) => b.add(const MyReviewsStarted()),
    expect: () => [
      isA<MyReviewsLoading>(),
      isA<MyReviewsLoaded>().having((s) => s.items.length, 'items', 2),
    ],
  );

  blocTest<MyReviewsBloc, MyReviewsState>(
    'empty items → empty state',
    build: () => MyReviewsBloc(gateway: _FakeGateway()),
    act: (b) => b.add(const MyReviewsStarted()),
    expect: () => [
      isA<MyReviewsLoading>(),
      isA<MyReviewsEmpty>(),
    ],
  );

  blocTest<MyReviewsBloc, MyReviewsState>(
    'filter sets state= on the request',
    build: () => MyReviewsBloc(
      gateway: _FakeGateway(items: [_seed('a', state: 'pending_moderation')]),
    ),
    act: (b) async {
      b.add(const MyReviewsStarted());
      await Future<void>.delayed(Duration.zero);
      b.add(const MyReviewsFilterChanged('pending_moderation'));
    },
    skip: 2,
    expect: () => [
      isA<MyReviewsLoading>(),
      isA<MyReviewsLoaded>()
          .having((s) => s.filter.state, 'filter.state', 'pending_moderation'),
    ],
  );

  blocTest<MyReviewsBloc, MyReviewsState>(
    'gateway throws → failure',
    build: () => MyReviewsBloc(gateway: _FakeGateway(throwOnList: true)),
    act: (b) => b.add(const MyReviewsStarted()),
    expect: () => [
      isA<MyReviewsLoading>(),
      isA<MyReviewsFailure>(),
    ],
  );
}
