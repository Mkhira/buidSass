using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Primitives;

namespace BackendApi.Modules.Verification.Admin.DecideApprove;

/// <summary>
/// Slice-level validator wrapper. Delegates to the shared
/// <see cref="ReviewerReasonValidator"/> so the approve / reject / request-info
/// trio stay in sync on FR-033 reason validation.
/// </summary>
public static class DecideApproveValidator
{
    public static (bool ok, VerificationReasonCode? reason, string? detail) Validate(
        DecideApproveRequest? request)
    {
        if (request is null)
        {
            return (false, VerificationReasonCode.ReviewReasonRequired,
                "Request body is required.");
        }

        return ReviewerReasonValidator.Validate(request.Reason);
    }
}
