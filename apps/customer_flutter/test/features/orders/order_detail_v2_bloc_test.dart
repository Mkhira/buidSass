import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/checkout/data/models/checkout_models.dart';
import 'package:customer_flutter/features/orders/bloc/order_detail_v2_bloc.dart';
import 'package:customer_flutter/features/orders/data/models/order_models.dart';
import 'package:customer_flutter/features/orders/data/orders_gateway.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockGateway extends Mock implements OrdersGateway {}

OrderDetail _detail({bool canReturn = true}) => OrderDetail(
      id: 'o-1',
      orderNumber: '2026-1',
      placedAt: DateTime.utc(2026, 5, 1),
      states: const OrderStateBundle(
        orderState: 'confirmed',
        paymentState: 'captured',
        fulfillmentState: 'shipped',
        refundState: 'none',
      ),
      actions: OrderActions(
        canCancel: true,
        canReorder: true,
        canRetryPayment: false,
        canReturn: canReturn,
      ),
      lines: const [],
      shipment: const OrderShipment(),
      payment: const OrderPayment(method: 'card'),
      totals: const CheckoutTotals(
        subtotal: '100',
        discount: '0',
        tax: '15',
        shipping: '0',
        grandTotal: '115',
        currency: 'SAR',
      ),
    );

void main() {
  late _MockGateway gateway;

  setUp(() => gateway = _MockGateway());

  blocTest<OrderDetailV2Bloc, OrderDetailV2State>(
    'loads detail then refines with eligibility',
    build: () {
      when(() => gateway.getById('o-1')).thenAnswer((_) async => _detail());
      when(() => gateway.getReturnEligibility('o-1'))
          .thenAnswer((_) async => const ReturnEligibility(
                lines: [],
                anyEligible: true,
                policyMarket: 'SA',
              ));
      return OrderDetailV2Bloc(gateway: gateway, orderId: 'o-1');
    },
    act: (b) => b.add(const OrderDetailStarted()),
    expect: () => [
      isA<OrderDetailLoading>(),
      isA<OrderDetailLoaded>()
          .having((s) => s.eligibility, 'eligibility (before refine)', isNull),
      isA<OrderDetailLoaded>().having(
          (s) => s.eligibility?.anyEligible, 'eligibility refined', true),
    ],
  );

  blocTest<OrderDetailV2Bloc, OrderDetailV2State>(
    'skips eligibility call when actions.canReturn is false',
    build: () {
      when(() => gateway.getById('o-1'))
          .thenAnswer((_) async => _detail(canReturn: false));
      return OrderDetailV2Bloc(gateway: gateway, orderId: 'o-1');
    },
    act: (b) => b.add(const OrderDetailStarted()),
    expect: () => [
      isA<OrderDetailLoading>(),
      isA<OrderDetailLoaded>(),
    ],
    verify: (_) {
      verifyNever(() => gateway.getReturnEligibility(any()));
    },
  );

  blocTest<OrderDetailV2Bloc, OrderDetailV2State>(
    'eligibility failure leaves detail loaded (refinement is best-effort)',
    build: () {
      when(() => gateway.getById('o-1')).thenAnswer((_) async => _detail());
      when(() => gateway.getReturnEligibility('o-1'))
          .thenThrow(Exception('network'));
      return OrderDetailV2Bloc(gateway: gateway, orderId: 'o-1');
    },
    act: (b) => b.add(const OrderDetailStarted()),
    expect: () => [
      isA<OrderDetailLoading>(),
      isA<OrderDetailLoaded>(),
    ],
  );
}
