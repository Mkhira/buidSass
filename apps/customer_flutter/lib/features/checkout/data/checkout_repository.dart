import 'checkout_view_models.dart';

abstract class CheckoutRepository {
  Future<CheckoutSession> startSession();

  Future<CheckoutSession> setAddress({
    required String sessionId,
    required String addressId,
  });

  Future<CheckoutSession> setShipping({
    required String sessionId,
    required String quoteId,
  });

  Future<CheckoutSession> setPayment({
    required String sessionId,
    required String methodId,
  });

  Future<CheckoutOutcome> submit({
    required String sessionId,
    required String idempotencyKey,
  });
}

class StubCheckoutRepository implements CheckoutRepository {
  @override
  Future<CheckoutSession> startSession() async {
    throw const CheckoutGapException();
  }

  @override
  Future<CheckoutSession> setAddress({
    required String sessionId,
    required String addressId,
  }) async {
    throw const CheckoutGapException();
  }

  @override
  Future<CheckoutSession> setShipping({
    required String sessionId,
    required String quoteId,
  }) async {
    throw const CheckoutGapException();
  }

  @override
  Future<CheckoutSession> setPayment({
    required String sessionId,
    required String methodId,
  }) async {
    throw const CheckoutGapException();
  }

  @override
  Future<CheckoutOutcome> submit({
    required String sessionId,
    required String idempotencyKey,
  }) async {
    throw const CheckoutGapException();
  }
}

class CheckoutGapException implements Exception {
  const CheckoutGapException();
  @override
  String toString() => 'Checkout client gap — escalate to spec 010 (FR-031).';
}

class CheckoutDriftException implements Exception {
  const CheckoutDriftException(this.details);
  final CheckoutDriftDetails details;
}

class CheckoutSessionExpiredException implements Exception {
  const CheckoutSessionExpiredException();
  @override
  String toString() => 'checkout.session_expired';
}
