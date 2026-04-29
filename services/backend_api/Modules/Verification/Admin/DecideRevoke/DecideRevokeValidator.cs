using BackendApi.Modules.Verification.Admin.Common;
using BackendApi.Modules.Verification.Primitives;

namespace BackendApi.Modules.Verification.Admin.DecideRevoke;

public static class DecideRevokeValidator
{
    public static (bool ok, VerificationReasonCode? reason, string? detail) Validate(
        DecideRevokeRequest? request)
    {
        if (request is null)
        {
            return (false, VerificationReasonCode.ReviewReasonRequired,
                "Request body is required.");
        }
        return ReviewerReasonValidator.Validate(request.Reason);
    }
}
