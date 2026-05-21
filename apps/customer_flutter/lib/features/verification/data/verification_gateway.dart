import 'dart:typed_data';

import 'models/verification_models.dart';

/// VerificationGateway — the 8 customer-tagged ops in
/// `openapi.verification.json`.
///
/// Write operations that map onto user intents (`submit`, `resubmit`,
/// `renew`) require an `Idempotency-Key`. Callers pass the key in
/// directly so screens can reuse one key across submit retries (BR-2,
/// BR-4a, BR-5a) and regenerate it when the user re-enters the screen.
abstract class VerificationGateway {
  Future<VerificationListPage> list();

  Future<VerificationActive> getActive();

  Future<VerificationSchema> getSchema();

  Future<VerificationDetail> getById(String id);

  /// Submit a fresh verification case (BR-2).
  Future<SubmitVerificationResult> submit({
    required SubmitVerificationRequest request,
    required String idempotencyKey,
  });

  /// Upload a single document for a slot on an existing case (BR-3).
  /// One call per document; multi-document submissions run in parallel
  /// with bounded concurrency (≤2) at the bloc layer (S-7.3 AC).
  Future<DocumentUploadResult> uploadDocument({
    required String verificationId,
    required String slotKey,
    required Uint8List bytes,
    required String filename,
  });

  /// Resubmit after admin requested info (BR-4 + BR-4a).
  Future<VerificationDetail> resubmit({
    required String verificationId,
    required ResubmitVerificationRequest request,
    required String idempotencyKey,
  });

  /// Renew an approved-near-expiry case (BR-5 + BR-5a). Creates a fresh
  /// case linked to the prior one.
  Future<SubmitVerificationResult> renew({
    required RenewVerificationRequest request,
    required String idempotencyKey,
  });
}
