import 'models/checkout_models.dart';

/// Customer-facing checkout endpoints per
/// `services/backend_api/openapi.checkout.json` + Phase 4 data-model.md.
/// All methods throw a typed [Failure] (from `core/error/failure.dart`)
/// on transport / HTTP error; 409 surfaces as a [CheckoutDriftException]
/// so `CheckoutBaseBloc.handleConflict` can branch on it.
abstract class CheckoutGateway {
  Future<CreateSessionResult> createSession(CreateSessionRequest request);

  Future<CheckoutSummary> getSummary(String sessionId);

  Future<List<ShippingQuoteOption>> getShippingQuotes(String sessionId);

  Future<CheckoutSummary> patchAddress({
    required String sessionId,
    required CheckoutAddressDto address,
  });

  Future<CheckoutSummary> patchShipping({
    required String sessionId,
    required String method,
  });

  Future<CheckoutSummary> patchPaymentMethod({
    required String sessionId,
    required String method,
    String? providerToken,
    String? bankTransferReference,
  });

  /// Submit the order. Caller MUST provide the same [idempotencyKey] on
  /// retries — `IdempotencyInterceptor` forwards it as
  /// `Idempotency-Key`. Re-using the key across distinct user intents is
  /// a Constitution Principle 13 violation.
  Future<SubmitResult> submit({
    required String sessionId,
    required String idempotencyKey,
  });

  Future<CheckoutSummary> acceptDrift(String sessionId);

  /// Price preview only (BR-2). Cart UI calls this on every change; the
  /// backend never persists state from it.
  Future<PriceCartResult> priceCart(PriceCartRequest request);
}
