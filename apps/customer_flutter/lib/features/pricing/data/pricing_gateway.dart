import 'models/pricing_models.dart';

/// Centralized pricing engine client. Backs Phase 2 PDP (single-item
/// preview) and Phase 4 cart/checkout (multi-line preview + finalization).
///
/// Endpoint (per `services/backend_api/openapi.pricing.json`):
///
///   * POST `/customer/pricing/price-cart` → [preview]
///
/// All errors throw a typed `Failure` from `core/error/failure.dart`. The
/// engine returns 422 for invalid carts (empty lines, unknown product),
/// which maps to [ValidationFailure].
abstract class PricingGateway {
  Future<PriceQuote> preview(PricingRequest request);
}
