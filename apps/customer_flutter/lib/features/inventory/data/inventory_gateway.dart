import 'models/inventory_models.dart';

/// Customer-facing availability read. Backs Phase 2 stock badges on lists
/// + PDP; Phase 4 add-to-cart eligibility check reuses this surface.
///
/// Endpoint (per `services/backend_api/openapi.inventory.json`):
///
///   * GET `/v1/customer/inventory/availability?productIds=…&market=…`
///     → [getAvailability] (batch)
///
/// Throws a typed `Failure` on transport/HTTP error.
abstract class InventoryGateway {
  Future<List<InventoryAvailability>> getAvailability({
    required List<String> productIds,
    required String market,
  });
}
