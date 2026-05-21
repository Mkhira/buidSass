import 'dart:typed_data';

import 'models/quote_models.dart';

/// QuotesGateway — the 13 quote-side ops in `openapi.b2b.json`
/// (quote list / awaiting-approval / detail / 6 actions / document
/// download + 2 create endpoints).
///
/// Create + action endpoints require an `Idempotency-Key` per BR-1 +
/// BR-2 (acceptance two-step). Callers pass the key in directly so
/// screens can lock one key per intent and reuse across retries.
abstract class QuotesGateway {
  Future<QuotesPage> list(QuotesFilter filter);

  Future<QuotesPage> awaitingMyApproval();

  Future<QuoteDetail> getById(String id);

  Future<CreateQuoteResult> createFromCart({
    required CreateQuoteFromCartRequest request,
    required String idempotencyKey,
  });

  Future<CreateQuoteResult> createFromProduct({
    required CreateQuoteFromProductRequest request,
    required String idempotencyKey,
  });

  Future<QuoteDetail> submitAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  });

  Future<QuoteDetail> finalizeAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  });

  Future<QuoteDetail> rejectAcceptance({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  });

  Future<QuoteDetail> requestRevision({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  });

  Future<QuoteDetail> withdraw({
    required String quoteId,
    required QuoteActionNoteRequest request,
    required String idempotencyKey,
  });

  Future<SaveAsTemplateResult> saveAsTemplate({
    required String quoteId,
    required SaveAsTemplateRequest request,
    required String idempotencyKey,
  });

  /// Binary PDF document for one version + locale. Returned as bytes
  /// so the caller can cache or share without managing the HTTP
  /// response lifecycle.
  Future<Uint8List> downloadDocument({
    required String quoteId,
    required String versionId,
    required String locale,
  });
}
