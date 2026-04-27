import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/orders/bloc/order_list_bloc.dart';
import 'package:customer_flutter/features/orders/data/order_view_models.dart';
import 'package:customer_flutter/features/orders/data/orders_repository.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements OrdersRepository {}

OrderListItem _item(String id) {
  return OrderListItem(
    id: id,
    orderNumber: 'ORD-$id',
    placedAt: DateTime(2026, 4, 1),
    totalsMinor: 1000,
    currency: 'SAR',
    orderState: 'placed',
    paymentState: 'authorized',
    fulfillmentState: 'pending',
    refundState: 'none',
  );
}

void main() {
  late _MockRepo repo;

  setUp(() {
    repo = _MockRepo();
    registerFallbackValue(const OrderListFilter());
  });

  blocTest<OrderListBloc, OrderListState>(
    'FilterChanged -> Loading -> Loaded',
    build: () {
      when(() => repo.fetchList(
            filter: any(named: 'filter'),
            cursor: any(named: 'cursor'),
          )).thenAnswer((_) async => OrderListPage(
            items: [_item('1')],
            nextCursor: null,
          ));
      return OrderListBloc(repository: repo);
    },
    act: (b) => b.add(
        const OrderListFilterChanged(OrderListFilter(orderState: 'placed'))),
    expect: () => [isA<OrderListLoading>(), isA<OrderListLoaded>()],
  );

  blocTest<OrderListBloc, OrderListState>(
    'empty page -> Empty',
    build: () {
      when(() => repo.fetchList(
                filter: any(named: 'filter'),
                cursor: any(named: 'cursor'),
              ))
          .thenAnswer(
              (_) async => const OrderListPage(items: [], nextCursor: null));
      return OrderListBloc(repository: repo);
    },
    act: (b) => b.add(const OrderListRefreshTapped()),
    expect: () => [isA<OrderListLoading>(), isA<OrderListEmpty>()],
  );

  blocTest<OrderListBloc, OrderListState>(
    'fetch failure -> Error',
    build: () {
      when(() => repo.fetchList(
            filter: any(named: 'filter'),
            cursor: any(named: 'cursor'),
          )).thenThrow(Exception('boom'));
      return OrderListBloc(repository: repo);
    },
    act: (b) => b.add(const OrderListRefreshTapped()),
    expect: () => [isA<OrderListLoading>(), isA<OrderListError>()],
  );

  blocTest<OrderListBloc, OrderListState>(
    'PageRequested appends and clears cursor',
    build: () {
      var calls = 0;
      when(() => repo.fetchList(
            filter: any(named: 'filter'),
            cursor: any(named: 'cursor'),
          )).thenAnswer((_) async {
        calls++;
        if (calls == 1) {
          return OrderListPage(items: [_item('1')], nextCursor: 'cur');
        }
        return OrderListPage(items: [_item('2')], nextCursor: null);
      });
      return OrderListBloc(repository: repo);
    },
    act: (b) => b
      ..add(const OrderListRefreshTapped())
      ..add(const OrderListPageRequested()),
    skip: 2,
    expect: () => [
      // first emission: isLoadingMore=true with the existing item
      predicate<OrderListLoaded>((s) => s.isLoadingMore && s.items.length == 1),
      // second: appended
      predicate<OrderListLoaded>((s) =>
          !s.isLoadingMore && s.items.length == 2 && s.nextCursor == null),
    ],
  );
}
