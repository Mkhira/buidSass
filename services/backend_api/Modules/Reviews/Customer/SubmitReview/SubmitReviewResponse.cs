namespace BackendApi.Modules.Reviews.Customer.SubmitReview;

/// <summary>Response body for POST /api/customer/reviews per contract §2.1.</summary>
public sealed record SubmitReviewResponse(
    Guid Id,
    string State,
    uint RowVersion,
    DateTimeOffset CreatedAtUtc,
    bool PendingReview);
