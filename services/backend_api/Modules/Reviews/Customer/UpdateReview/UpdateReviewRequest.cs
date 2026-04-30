namespace BackendApi.Modules.Reviews.Customer.UpdateReview;

/// <summary>
/// Partial-update request for PATCH /api/customer/reviews/{id} per contract §2.2.
/// Any subset of the patchable fields may be omitted; <c>null</c> is "leave unchanged".
/// </summary>
public sealed record UpdateReviewRequest(
    int? Rating,
    string? Headline,
    string? Body,
    string? Locale,
    IReadOnlyList<string>? MediaUrls);
