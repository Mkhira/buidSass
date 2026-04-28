import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/catalog/data/catalog_view_models.dart';
import 'package:customer_flutter/features/orders/bloc/order_detail_bloc.dart';
import 'package:customer_flutter/features/orders/data/order_view_models.dart';
import 'package:customer_flutter/features/orders/data/orders_repository.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements OrdersRepository {}

OrderDetailViewModel _vm() {
  return OrderDetailViewModel(
    id: 'ord_1',
    orderNumber: 'ORD-1',
    placedAt: DateTime(2026, 4, 1),
    orderState: 'placed',
    paymentState: 'authorized',
    fulfillmentState: 'pending',
    refundState: 'none',
    lines: const [],
    totals: const PriceBreakdown(
      unitPriceMinor: 1000,
      discountMinor: 0,
      taxMinor: 150,
      totalMinor: 1150,
      currency: 'SAR',
    ),
    timeline: const [],
    refundEligibility: const RefundEligibility(canRequest: false),
  );
}

void main() {
  late _MockRepo repo;
  setUp(() => repo = _MockRepo());

  blocTest<OrderDetailBloc, OrderDetailState>(
    'Requested -> Loading -> Loaded',
    build: () {
      when(() => repo.fetchDetail('ord_1')).thenAnswer((_) async => _vm());
      return OrderDetailBloc(repository: repo);
    },
    act: (b) => b.add(const OrderDetailRequested('ord_1')),
    expect: () => [isA<OrderDetailLoading>(), isA<OrderDetailLoaded>()],
  );

  blocTest<OrderDetailBloc, OrderDetailState>(
    'Refreshed without prior id is a no-op',
    build: () => OrderDetailBloc(repository: repo),
    act: (b) => b.add(const OrderDetailRefreshed()),
    expect: () => isEmpty,
  );

  blocTest<OrderDetailBloc, OrderDetailState>(
    'Refreshed re-fetches the active id',
    build: () {
      when(() => repo.fetchDetail(any())).thenAnswer((_) async => _vm());
      return OrderDetailBloc(repository: repo);
    },
    act: (b) => b
      ..add(const OrderDetailRequested('ord_1'))
      ..add(const OrderDetailRefreshed()),
    expect: () => [
      isA<OrderDetailLoading>(),
      isA<OrderDetailLoaded>(),
      isA<OrderDetailLoaded>(),
    ],
  );

  blocTest<OrderDetailBloc, OrderDetailState>(
    'fetch failure -> Error',
    build: () {
      when(() => repo.fetchDetail(any())).thenThrow(Exception('boom'));
      return OrderDetailBloc(repository: repo);
    },
    act: (b) => b.add(const OrderDetailRequested('ord_1')),
    expect: () => [isA<OrderDetailLoading>(), isA<OrderDetailError>()],
  );
}
