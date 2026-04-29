using BackendApi.Modules.Verification.Primitives;

namespace BackendApi.Modules.Verification.Customer.SubmitVerification;

/// <summary>
/// Stateless shape validation for <see cref="SubmitVerificationRequest"/>. Returns
/// the first failing reason code (handlers map to a Problem Details envelope).
/// Per-market schema field validation runs INSIDE the handler (after the schema
/// row is loaded); this validator covers shape-only checks.
/// </summary>
public static class SubmitVerificationValidator
{
    public const int RegulatorIdMaxLength = 64;
    public const int ProfessionMaxLength = 64;

    public static (bool ok, VerificationReasonCode? reason, string? detail) Validate(
        SubmitVerificationRequest? request)
    {
        if (request is null)
        {
            return (false, VerificationReasonCode.RequiredFieldMissing, "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Profession))
        {
            return (false, VerificationReasonCode.RequiredFieldMissing, "profession is required.");
        }

        if (request.Profession.Length > ProfessionMaxLength)
        {
            return (false, VerificationReasonCode.RequiredFieldMissing,
                $"profession exceeds {ProfessionMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(request.RegulatorIdentifier))
        {
            return (false, VerificationReasonCode.RegulatorIdentifierInvalid,
                "regulator_identifier is required.");
        }

        if (request.RegulatorIdentifier.Length > RegulatorIdMaxLength)
        {
            return (false, VerificationReasonCode.RegulatorIdentifierInvalid,
                $"regulator_identifier exceeds {RegulatorIdMaxLength} characters.");
        }

        if (request.DocumentIds is null)
        {
            return (false, VerificationReasonCode.DocumentsInvalid,
                "document_ids is required (use an empty array if none uploaded).");
        }

        return (true, null, null);
    }
}
