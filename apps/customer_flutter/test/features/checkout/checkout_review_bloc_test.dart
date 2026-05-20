import 'package:bloc_test/bloc_test.dart';
import 'package:customer_flutter/features/checkout/bloc/checkout_review_bloc.dart';
import 'package:customer_flutter/features/checkout/data/checkout_gateway.dart';
import 'package:customer_flutter/features/checkout/data/models/checkout_models.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

class _MockGateway extends Mock implements CheckoutGateway {}

CheckoutSummary _summary(String sessionId) => CheckoutSummary(
      sessionId: sessionId,
      expiresAt: DateTime.utc(2026, 6, 1),
      lines: const [],
      shipping: const CheckoutShippingInfo(),
      payment: const CheckoutPaymentInfo(method: 'cod'),
      totals: const CheckoutTotals(
        subtotal: '100',
        discount: '0',
        tax: '15',
        shipping: '0',
        grandTotal: '115',
        currency: 'SAR',
      ),
      availableMethods: const ['cod'],
      stepStatus: const CheckoutStepStatusMap(),
    );

void main() {
  late _MockGateway gateway;

  setUp(() => gateway = _MockGateway());

  blocTest<CheckoutReviewBloc, CheckoutReviewState>(
    'submit success emits Submitting → Success',
    build: () {
      when(() => gateway.submit(
            sessionId: any(named: 'sessionId'),
            idempotencyKey: any(named: 'idempotencyKey'),
          )).thenAnswer((_) async => const SubmitResult(
            orderId: 'o-1',
            orderNumber: '2026-1',
            paymentState: 'captured',
            fulfillmentState: 'pending',
            redirect: SubmitRedirect(kind: 'none'),
          ));
      return CheckoutReviewBloc(
        gateway: gateway,
        sessionId: 'sess-1',
        initialSummary: _summary('sess-1'),
        idempotencyKeyFactory: () => 'FIXED-KEY',
      );
    },
    act: (b) => b.add(const ReviewSubmitted()),
    expect: () => [
      isA<CheckoutReviewSubmitting>()
          .having((s) => s.idempotencyKey, 'key', 'FIXED-KEY'),
      isA<CheckoutReviewSuccess>(),
    ],
  );

  blocTest<CheckoutReviewBloc, CheckoutReviewState>(
    'retry after failure reuses the same idempotency key (BR-3)',
    build: () {
      var attempts = 0;
      when(() => gateway.submit(
            sessionId: any(named: 'sessionId'),
            idempotencyKey: any(named: 'idempotencyKey'),
          )).thenAnswer((_) async {
        attempts++;
        if (attempts == 1) throw Exception('network');
        return const SubmitResult(
          orderId: 'o-2',
          orderNumber: '2026-2',
          paymentState: 'captured',
          fulfillmentState: 'pending',
          redirect: SubmitRedirect(kind: 'none'),
        );
      });
      return CheckoutReviewBloc(
        gateway: gateway,
        sessionId: 'sess-2',
        initialSummary: _summary('sess-2'),
        idempotencyKeyFactory: () => 'FIXED-KEY-RETRY',
      );
    },
    act: (b) async {
      b.add(const ReviewSubmitted());
      await Future<void>.delayed(const Duration(milliseconds: 5));
      b.add(const ReviewSubmitted());
    },
    wait: const Duration(milliseconds: 50),
    verify: (_) {
      final calls = verify(() => gateway.submit(
            sessionId: 'sess-2',
            idempotencyKey: captureAny(named: 'idempotencyKey'),
          )).captured;
      expect(calls, ['FIXED-KEY-RETRY', 'FIXED-KEY-RETRY']);
    },
  );

  blocTest<CheckoutReviewBloc, CheckoutReviewState>(
    'drift on submit emits Conflict carrying the same idempotency key',
    build: () {
      when(() => gateway.submit(
            sessionId: any(named: 'sessionId'),
            idempotencyKey: any(named: 'idempotencyKey'),
          )).thenThrow(CheckoutDriftException(
        details: const DriftDetails(deltas: [
          DriftDelta(kind: 'price', productId: 'p-1'),
        ]),
        correlationId: 'corr-drift',
      ));
      return CheckoutReviewBloc(
        gateway: gateway,
        sessionId: 'sess-3',
        initialSummary: _summary('sess-3'),
        idempotencyKeyFactory: () => 'K3',
      );
    },
    act: (b) => b.add(const ReviewSubmitted()),
    expect: () => [
      isA<CheckoutReviewSubmitting>(),
      isA<CheckoutReviewConflict>()
          .having((s) => s.conflict.details.deltas.single.kind, 'kind', 'price')
          .having((s) => s.idempotencyKey, 'key reuse', 'K3'),
    ],
  );

  blocTest<CheckoutReviewBloc, CheckoutReviewState>(
    'redirect kind=3ds emits Redirecting',
    build: () {
      when(() => gateway.submit(
            sessionId: any(named: 'sessionId'),
            idempotencyKey: any(named: 'idempotencyKey'),
          )).thenAnswer((_) async => const SubmitResult(
            orderId: 'o-x',
            orderNumber: '2026-x',
            paymentState: 'requires_action',
            fulfillmentState: 'pending',
            redirect:
                SubmitRedirect(kind: '3ds', url: 'https://example.test/3ds'),
          ));
      return CheckoutReviewBloc(
        gateway: gateway,
        sessionId: 'sess-4',
        initialSummary: _summary('sess-4'),
        idempotencyKeyFactory: () => 'K4',
      );
    },
    act: (b) => b.add(const ReviewSubmitted()),
    expect: () => [
      isA<CheckoutReviewSubmitting>(),
      isA<CheckoutReviewRedirecting>()
          .having((s) => s.url, 'url', 'https://example.test/3ds'),
    ],
  );
}
