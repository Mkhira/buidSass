import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/checkout/bloc/checkout_bloc.dart';
import 'package:customer_flutter/features/checkout/data/checkout_repository.dart';
import 'package:customer_flutter/features/checkout/data/checkout_view_models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockRepo extends Mock implements CheckoutRepository {}

CheckoutSession _session() {
  return const CheckoutSession(
    sessionId: 'sess',
    availableAddresses: [
      CheckoutAddress(
        id: 'a1',
        label: 'Home',
        line1: 'l',
        city: 'c',
        country: 'KSA',
        phone: '+966500000000',
      ),
    ],
    availableQuotes: [
      ShippingQuote(
        id: 'q1',
        labelKey: 'standard',
        amountMinor: 1000,
        currency: 'SAR',
        etaDays: 3,
      ),
    ],
    availablePaymentMethods: [
      PaymentMethod(id: 'm1', kind: 'mada', labelKey: 'Mada'),
    ],
  );
}

void main() {
  late _MockRepo repo;

  setUp(() {
    repo = _MockRepo();
    when(repo.startSession).thenAnswer((_) async => _session());
    when(() => repo.setAddress(
          sessionId: any(named: 'sessionId'),
          addressId: any(named: 'addressId'),
        )).thenAnswer((_) async => _session());
    when(() => repo.setShipping(
          sessionId: any(named: 'sessionId'),
          quoteId: any(named: 'quoteId'),
        )).thenAnswer((_) async => _session());
    when(() => repo.setPayment(
          sessionId: any(named: 'sessionId'),
          methodId: any(named: 'methodId'),
        )).thenAnswer((_) async => _session());
  });

  blocTest<CheckoutBloc, CheckoutState>(
    'CheckoutStarted -> Drafting',
    build: () => CheckoutBloc(repository: repo),
    act: (b) => b.add(const CheckoutStarted()),
    expect: () => [isA<CheckoutDrafting>()],
  );

  blocTest<CheckoutBloc, CheckoutState>(
    'all selectors set -> Ready',
    build: () => CheckoutBloc(repository: repo),
    act: (b) => b
      ..add(const CheckoutStarted())
      ..add(const AddressSelected('a1'))
      ..add(const ShippingSelected('q1'))
      ..add(const PaymentSelected('m1')),
    skip: 3,
    expect: () => [isA<CheckoutReady>()],
  );

  blocTest<CheckoutBloc, CheckoutState>(
    'submit success -> Submitted',
    build: () {
      when(() => repo.submit(
            sessionId: any(named: 'sessionId'),
            idempotencyKey: any(named: 'idempotencyKey'),
          )).thenAnswer((_) async => const CheckoutOutcome(
            orderId: 'ord_1',
            orderState: 'placed',
            paymentState: 'authorized',
            fulfillmentState: 'pending',
            refundState: 'none',
          ));
      return CheckoutBloc(repository: repo);
    },
    seed: () => CheckoutReady(
      session: _session(),
      selectedAddressId: 'a1',
      selectedQuoteId: 'q1',
      selectedPaymentMethodId: 'm1',
      idempotencyKey: 'k1',
    ),
    act: (b) => b.add(const SubmitTapped()),
    expect: () => [isA<CheckoutSubmitting>(), isA<CheckoutSubmitted>()],
  );

  blocTest<CheckoutBloc, CheckoutState>(
    'submit drift -> DriftBlocked',
    build: () {
      when(() => repo.submit(
            sessionId: any(named: 'sessionId'),
            idempotencyKey: any(named: 'idempotencyKey'),
          )).thenThrow(const CheckoutDriftException(
        CheckoutDriftDetails(changedLines: ['a'], priceDeltaMinor: 100),
      ));
      return CheckoutBloc(repository: repo);
    },
    seed: () => CheckoutReady(
      session: _session(),
      selectedAddressId: 'a1',
      selectedQuoteId: 'q1',
      selectedPaymentMethodId: 'm1',
      idempotencyKey: 'k1',
    ),
    act: (b) => b.add(const SubmitTapped()),
    expect: () => [isA<CheckoutSubmitting>(), isA<CheckoutDriftBlocked>()],
  );

  blocTest<CheckoutBloc, CheckoutState>(
    'submit failure then retry reuses idempotency key',
    build: () {
      var calls = 0;
      when(() => repo.submit(
            sessionId: any(named: 'sessionId'),
            idempotencyKey: any(named: 'idempotencyKey'),
          )).thenAnswer((_) async {
        calls++;
        if (calls == 1) throw Exception('5xx');
        return const CheckoutOutcome(
          orderId: 'ord_2',
          orderState: 'placed',
          paymentState: 'authorized',
          fulfillmentState: 'pending',
          refundState: 'none',
        );
      });
      return CheckoutBloc(repository: repo);
    },
    seed: () => CheckoutReady(
      session: _session(),
      selectedAddressId: 'a1',
      selectedQuoteId: 'q1',
      selectedPaymentMethodId: 'm1',
      idempotencyKey: 'k_same',
    ),
    act: (b) => b
      ..add(const SubmitTapped())
      ..add(const RetryTapped()),
    expect: () => [
      isA<CheckoutSubmitting>(),
      isA<CheckoutFailed>(),
      isA<CheckoutSubmitting>(),
      isA<CheckoutSubmitted>(),
    ],
    verify: (_) {
      final captured = verify(() => repo.submit(
            sessionId: any(named: 'sessionId'),
            idempotencyKey: captureAny(named: 'idempotencyKey'),
          )).captured;
      expect(captured, ['k_same', 'k_same']);
    },
  );
}
