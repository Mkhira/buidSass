namespace BackendApi.Modules.Reviews.Customer.ReportReview;

/// <summary>Request body for POST /api/customer/reviews/{id}/report per contract §2.5.</summary>
public sealed record ReportReviewRequest(string Reason, string? Note);
