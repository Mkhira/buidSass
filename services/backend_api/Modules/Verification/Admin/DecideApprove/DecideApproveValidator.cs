using BackendApi.Modules.Verification.Primitives;

namespace BackendApi.Modules.Verification.Admin.DecideApprove;

/// <summary>
/// Validates the reviewer-supplied bilingual reason payload (FR-033). At least
/// one locale MUST be present and non-blank.
/// </summary>
public static class DecideApproveValidator
{
    public const int ReasonMaxLength = 1000;

    public static (bool ok, VerificationReasonCode? reason, string? detail) Validate(
        DecideApproveRequest? request)
    {
        if (request is null)
        {
            return (false, VerificationReasonCode.ReviewReasonRequired,
                "Request body is required.");
        }

        if (request.Reason is null
            || (string.IsNullOrWhiteSpace(request.Reason.En)
                && string.IsNullOrWhiteSpace(request.Reason.Ar)))
        {
            return (false, VerificationReasonCode.ReviewReasonRequired,
                "reason MUST include at least one of 'en' or 'ar'.");
        }

        if (request.Reason.En is { Length: > ReasonMaxLength })
        {
            return (false, VerificationReasonCode.ReviewReasonRequired,
                $"reason.en exceeds {ReasonMaxLength} characters.");
        }

        if (request.Reason.Ar is { Length: > ReasonMaxLength })
        {
            return (false, VerificationReasonCode.ReviewReasonRequired,
                $"reason.ar exceeds {ReasonMaxLength} characters.");
        }

        return (true, null, null);
    }
}
