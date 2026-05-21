import 'models/legacy_quotation_models.dart';

/// LegacyQuotationsGateway — the 4 `/v1/customer/quotations` ops on
/// `openapi.orders.json`. Read-only flow with accept/reject actions.
/// Server may eventually deprecate; the bloc handles 404 gracefully
/// by hiding the menu entry.
abstract class LegacyQuotationsGateway {
  /// May throw a NotFoundFailure for migrated accounts that never had
  /// legacy quotes — the caller surfaces that as an empty list / hides
  /// the menu (BR-8).
  Future<List<LegacyQuotationListItem>> list();

  Future<LegacyQuotationDetail> getById(String id);

  Future<LegacyQuotationDetail> accept({
    required String id,
    required LegacyQuotationActionRequest request,
    required String idempotencyKey,
  });

  Future<LegacyQuotationDetail> reject({
    required String id,
    required LegacyQuotationActionRequest request,
    required String idempotencyKey,
  });
}
