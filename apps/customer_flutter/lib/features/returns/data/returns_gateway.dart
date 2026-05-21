import 'dart:typed_data';

import 'models/return_models.dart';

/// ReturnsGateway — the 4 customer-tagged ops in `openapi.returns.json`
/// + the orders return-eligibility read (reused from Phase 5 via
/// `OrdersGateway.getReturnEligibility`).
///
/// All write operations send an `Idempotency-Key` header — callers pass
/// the key in directly so the wizard can reuse one wizard-create key
/// across retries (BR-3) and one per-tile key across photo-upload
/// retries (BR-2).
abstract class ReturnsGateway {
  Future<ReturnListPage> list(ReturnsListFilter filter);

  Future<ReturnDetail> getById(String id);

  /// Upload a single photo. The caller MUST supply [clientPhotoKey] — a
  /// UUID v4 generated on tile add and reused across retries for that
  /// tile. The server dedupes by `(clientPhotoKey, checksum)` and
  /// returns the same `photoId` for retries. See data-model.md.
  Future<ReturnPhotoUploadResult> uploadPhoto({
    required Uint8List bytes,
    required String filename,
    required String clientPhotoKey,
  });

  /// Create a return. The caller MUST supply [idempotencyKey] — a UUID
  /// v4 generated once per wizard intent and reused across submit
  /// retries (BR-3 + data-model.md).
  Future<CreateReturnResult> create({
    required String orderId,
    required CreateReturnRequest request,
    required String idempotencyKey,
  });
}
