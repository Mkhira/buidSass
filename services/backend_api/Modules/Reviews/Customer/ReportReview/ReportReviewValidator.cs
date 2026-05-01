using BackendApi.Modules.Reviews.Primitives;

namespace BackendApi.Modules.Reviews.Customer.ReportReview;

/// <summary>
/// Field-level validator for the report-review request. The 5 fixed reasons
/// match the CHECK constraint on <c>review_flags.reason</c> (data-model §2.4).
/// </summary>
public static class ReportReviewValidator
{
    public static readonly IReadOnlyList<string> AllowedReasons = new[]
    {
        "inappropriate_language",
        "spam_or_irrelevant",
        "personal_attack",
        "false_or_misleading",
        "other_with_required_note",
    };

    public static (bool ok, string? reasonCode, string? detail) Validate(ReportReviewRequest? body)
    {
        if (body is null) return (false, ReviewReasonCode.ReportReasonInvalid, "Request body is required.");

        if (string.IsNullOrWhiteSpace(body.Reason) || !AllowedReasons.Contains(body.Reason))
        {
            return (false, ReviewReasonCode.ReportReasonInvalid, "Invalid report reason.");
        }

        if (body.Reason == "other_with_required_note")
        {
            if (string.IsNullOrWhiteSpace(body.Note) || body.Note.Trim().Length < 10)
            {
                return (false, ReviewReasonCode.ReportNoteRequired,
                    "A note of at least 10 characters is required for this reason.");
            }
        }

        return (true, null, null);
    }
}
