import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/cart/data/cart_store.dart';
import 'package:customer_flutter/features/checkout/data/models/checkout_models.dart';
import 'package:customer_flutter/features/orders/bloc/reorder_bloc.dart';
import 'package:customer_flutter/features/orders/data/models/order_models.dart';
import 'package:customer_flutter/features/orders/data/orders_gateway.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

class _MockGateway extends Mock implements OrdersGateway {}

void main() {
  late _MockGateway gateway;
  late CartStore cart;

  setUp(() async {
    gateway = _MockGateway();
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();
    cart = CartStore(prefs: prefs);
    await cart.load();
  });

  blocTest<ReorderBloc, ReorderState>(
    'preview returns available + unavailable',
    build: () {
      when(() => gateway.reorder('o-1'))
          .thenAnswer((_) async => const ReorderResult(
                available: [
                  ReorderAvailableLine(
                    productId: 'p-1',
                    qty: 2,
                    name: 'A',
                    priceHint: Money(amount: '60', currency: 'SAR'),
                  ),
                ],
                unavailable: [
                  ReorderUnavailableLine(
                    productId: 'p-2',
                    name: 'B',
                    reason: 'out_of_stock',
                  ),
                ],
              ));
      return ReorderBloc(gateway: gateway, cartStore: cart, orderId: 'o-1');
    },
    act: (b) => b.add(const ReorderStarted()),
    expect: () => [
      isA<ReorderLoading>(),
      isA<ReorderLoaded>()
          .having((s) => s.result.available.length, 'avail', 1)
          .having((s) => s.result.unavailable.length, 'unavail', 1),
    ],
  );

  blocTest<ReorderBloc, ReorderState>(
    'confirm merges available lines into the cart store',
    build: () {
      when(() => gateway.reorder('o-1'))
          .thenAnswer((_) async => const ReorderResult(
                available: [
                  ReorderAvailableLine(
                    productId: 'p-1',
                    qty: 2,
                    name: 'A',
                    priceHint: Money(amount: '60', currency: 'SAR'),
                  ),
                ],
                unavailable: [],
              ));
      return ReorderBloc(gateway: gateway, cartStore: cart, orderId: 'o-1');
    },
    seed: () => const ReorderLoaded(
      result: ReorderResult(
        available: [
          ReorderAvailableLine(
            productId: 'p-1',
            qty: 2,
            name: 'A',
            priceHint: Money(amount: '60', currency: 'SAR'),
          ),
        ],
        unavailable: [],
      ),
    ),
    act: (b) => b.add(const ReorderAddToCartConfirmed()),
    expect: () => [isA<ReorderConfirming>(), isA<ReorderDone>()],
    verify: (_) {
      expect(cart.snapshot.lines.single.productId, 'p-1');
      expect(cart.snapshot.lines.single.qty, 2);
      expect(cart.snapshot.lines.single.unitPriceMinor, 6000);
    },
  );
}
