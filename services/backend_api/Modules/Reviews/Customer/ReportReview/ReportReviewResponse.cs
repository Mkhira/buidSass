namespace BackendApi.Modules.Reviews.Customer.ReportReview;

/// <summary>Response body for POST /api/customer/reviews/{id}/report per contract §2.5.</summary>
public sealed record ReportReviewResponse(
    Guid FlagId,
    bool Qualified,
    ThresholdProgress ThresholdProgress);

/// <summary>
/// Surfaces "where this review stands toward the auto-flag threshold" so the
/// caller can render UI hints; values reflect the count after the just-inserted
/// flag was committed.
/// </summary>
public sealed record ThresholdProgress(int QualifiedCount, int Threshold);
