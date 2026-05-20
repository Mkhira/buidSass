import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/cart/bloc/cart_v2_bloc.dart';
import 'package:customer_flutter/features/cart/data/cart_store.dart';
import 'package:customer_flutter/features/cart/data/models/cart_store_models.dart';
import 'package:customer_flutter/features/checkout/data/checkout_gateway.dart';
import 'package:customer_flutter/features/checkout/data/models/checkout_models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

class _MockGateway extends Mock implements CheckoutGateway {}

CartStoreLine _line(String id, {int qty = 1}) => CartStoreLine(
      productId: id,
      slug: id,
      name: 'P-$id',
      imageUrl: '',
      qty: qty,
      unitPriceMinor: 12000,
      currency: 'SAR',
    );

PriceCartResult _quote() => const PriceCartResult(
      lines: [],
      totals: CheckoutTotals(
        subtotal: '120',
        discount: '0',
        tax: '18',
        shipping: '0',
        grandTotal: '138',
        currency: 'SAR',
      ),
    );

void main() {
  setUpAll(() {
    registerFallbackValue(
      const PriceCartRequest(lines: [], buyerKind: '', marketCode: ''),
    );
    registerFallbackValue(
      const CreateSessionRequest(lines: [], buyerKind: '', marketCode: ''),
    );
  });

  late _MockGateway gateway;
  late CartStore store;

  setUp(() async {
    gateway = _MockGateway();
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();
    store = CartStore(prefs: prefs);
    await store.load();
  });

  CartV2Bloc build() => CartV2Bloc(
        store: store,
        gateway: gateway,
        marketProvider: () => 'ksa',
        quoteDebounce: const Duration(milliseconds: 1),
      );

  blocTest<CartV2Bloc, CartV2State>(
    'CartStarted on empty cart emits Empty',
    build: build,
    act: (b) => b.add(const CartStarted()),
    expect: () => [isA<CartV2Empty>()],
  );

  blocTest<CartV2Bloc, CartV2State>(
    'CartStarted on non-empty cart fetches preview and emits Loaded',
    setUp: () async {
      await store.addLine(_line('a'));
      when(() => gateway.priceCart(any())).thenAnswer((_) async => _quote());
    },
    build: build,
    act: (b) => b.add(const CartStarted()),
    expect: () => [
      isA<CartV2Loaded>()
          .having((s) => s.totals.grandTotal, 'grandTotal', '138'),
    ],
  );

  blocTest<CartV2Bloc, CartV2State>(
    'Coupon apply forwards to gateway and surfaces invalid message',
    setUp: () async {
      await store.addLine(_line('a'));
      when(() => gateway.priceCart(any())).thenAnswer((_) async {
        return const PriceCartResult(
          lines: [],
          totals: CheckoutTotals(
            subtotal: '120',
            discount: '0',
            tax: '18',
            shipping: '0',
            grandTotal: '138',
            currency: 'SAR',
          ),
          couponValid: false,
          couponMessage: 'Coupon not recognized',
        );
      });
    },
    build: build,
    seed: () => CartV2Loaded(
      snapshot: store.snapshot,
      totals: _quote().totals,
    ),
    act: (b) => b.add(const CartCouponApplied('BADCODE')),
    expect: () => [
      // First emission is the optimistic "quote in flight" copy of the
      // current loaded state — couponError still null until the preview
      // returns.
      isA<CartV2Loaded>().having((s) => s.isQuoteInFlight, 'inflight', true),
      isA<CartV2Loaded>()
          .having((s) => s.couponError, 'couponError', 'Coupon not recognized'),
    ],
    verify: (_) {
      expect(store.snapshot.couponCode, 'BADCODE');
    },
  );

  blocTest<CartV2Bloc, CartV2State>(
    'Proceed creates a session and emits Proceeding',
    setUp: () async {
      await store.addLine(_line('a'));
      when(() => gateway.priceCart(any())).thenAnswer((_) async => _quote());
      when(() => gateway.createSession(any()))
          .thenAnswer((_) async => CreateSessionResult(
                sessionId: 'sess-x',
                expiresAt: DateTime.utc(2026, 6, 1),
                summary: CheckoutSummary(
                  sessionId: 'sess-x',
                  expiresAt: DateTime.utc(2026, 6, 1),
                  lines: const [],
                  shipping: const CheckoutShippingInfo(),
                  payment: const CheckoutPaymentInfo(),
                  totals: _quote().totals,
                  availableMethods: const ['cod'],
                  stepStatus: const CheckoutStepStatusMap(),
                ),
                availableSteps: const ['address'],
              ));
    },
    build: build,
    seed: () => CartV2Loaded(
      snapshot: store.snapshot,
      totals: _quote().totals,
    ),
    act: (b) => b.add(const CartProceedRequested()),
    expect: () => [
      isA<CartV2Proceeding>().having((s) => s.sessionId, 'sessionId', 'sess-x'),
    ],
  );
}
