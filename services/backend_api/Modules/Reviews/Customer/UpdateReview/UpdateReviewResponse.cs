namespace BackendApi.Modules.Reviews.Customer.UpdateReview;

public sealed record UpdateReviewResponse(
    Guid Id,
    string State,
    uint RowVersion,
    DateTimeOffset UpdatedAtUtc,
    int EditCount,
    bool PendingReview);
