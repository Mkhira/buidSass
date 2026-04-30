namespace BackendApi.Modules.Reviews.Customer.SubmitReview;

/// <summary>Request body for POST /api/customer/reviews per contract §2.1.</summary>
public sealed record SubmitReviewRequest(
    Guid ProductId,
    int Rating,
    string Headline,
    string Body,
    string Locale,
    IReadOnlyList<string>? MediaUrls);
