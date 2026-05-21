import 'models/review_models.dart';

/// ReviewsCustomerGateway — the 6 customer-tagged ops in
/// `openapi.reviews.json`.
///
/// `submit` requires `Idempotency-Key` per BR-7. Edit (`PATCH`) is
/// non-idempotent-keyed but server-gated by `editableUntil` per BR-10.
abstract class ReviewsCustomerGateway {
  Future<CreateReviewResult> submit({
    required CreateReviewRequest request,
    required String idempotencyKey,
  });

  Future<MyReviewsPage> listMine(MyReviewsFilter filter);

  Future<MyReviewDetail> getMine(String reviewId);

  Future<MyReviewDetail> edit({
    required String reviewId,
    required EditReviewRequest request,
  });

  /// Per-market report reasons (BR-9). Cached server-side; the bloc
  /// fetches once on screen mount.
  Future<List<ReportReason>> getReportReasons();

  Future<ReportReviewResult> report({
    required String reviewId,
    required ReportReviewRequest request,
  });
}
