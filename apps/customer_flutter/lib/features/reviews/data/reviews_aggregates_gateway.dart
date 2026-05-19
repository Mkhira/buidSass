import 'models/reviews_aggregate_models.dart';

/// Public, read-only review aggregate surface consumed by Phase 2
/// (catalog list cards, PDP rating block). Phase 7 will extend this
/// module with the customer-write endpoints (`/v1/customer/reviews/*`).
///
/// Endpoints (per `services/backend_api/openapi.reviews.json`):
///
///   * GET `/v1/public/reviews/aggregates?product_ids=…&market_code=…`
///     → [getAggregatesBatch]
///   * GET `/v1/public/reviews/aggregates/{product_id}?market_code=…`
///     → [getAggregate]
abstract class ReviewsAggregatesGateway {
  Future<List<ReviewsAggregate>> getAggregatesBatch({
    required List<String> productIds,
    required String marketCode,
  });

  Future<ReviewsAggregate?> getAggregate({
    required String productId,
    required String marketCode,
  });
}
