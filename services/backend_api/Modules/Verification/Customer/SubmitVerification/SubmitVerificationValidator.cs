using System.Text.Json;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// US5 / T101 dynamic validation: walks the active schema's
    /// <c>required_fields</c> jsonb spec and validates the request against it.
    /// Replaces the prior hardcoded profession/regulator checks so a per-market
    /// schema bump (new field, tighter regex, different enum) takes effect
    /// without a code change. Stateless — caller supplies the snapshotted
    /// jsonb string.
    ///
    /// <para>Field semantics handled:</para>
    /// <list type="bullet">
    ///   <item><c>profession</c> + <c>regulator_identifier</c> are mapped from the
    ///         strongly-typed request DTO. Other required fields (future schema
    ///         bumps) cause a soft-fail with <c>required_field_missing</c> until
    ///         the request DTO carries them — call site keeps the validator
    ///         honest about what it actually checks.</item>
    ///   <item><c>kind=enum</c> rejects values not in <c>enumValues</c>.</item>
    ///   <item><c>kind=text|tel</c> with <c>pattern</c> rejects non-matching values.</item>
    ///   <item>Empty / whitespace-only values fail when <c>required=true</c>.</item>
    /// </list>
    /// </summary>
    public static (bool ok, VerificationReasonCode? reason, string? detail) ValidateAgainstSchema(
        SubmitVerificationRequest request,
        string requiredFieldsJson)
    {
        if (string.IsNullOrWhiteSpace(requiredFieldsJson))
        {
            return (true, null, null);
        }

        IReadOnlyList<RequiredFieldSpec> specs;
        try
        {
            var deserialized = JsonSerializer.Deserialize<List<RequiredFieldSpec>>(
                requiredFieldsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            specs = deserialized ?? new List<RequiredFieldSpec>();
        }
        catch (JsonException)
        {
            // Malformed schema — fail closed (Principle 5): caller surfaces as
            // MarketUnsupported so ops know to fix the schema config.
            return (false, VerificationReasonCode.MarketUnsupported,
                "Schema required_fields jsonb is malformed.");
        }

        foreach (var spec in specs)
        {
            var value = ResolveFieldValue(request, spec.Name);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (spec.Required)
                {
                    return (false,
                        IsRegulatorField(spec.Name)
                            ? VerificationReasonCode.RegulatorIdentifierInvalid
                            : VerificationReasonCode.RequiredFieldMissing,
                        $"{spec.Name} is required by the active market schema.");
                }
                continue;
            }

            switch (spec.Kind)
            {
                case "enum":
                    if (spec.EnumValues is { Count: > 0 }
                        && !spec.EnumValues.Contains(value, StringComparer.Ordinal))
                    {
                        return (false, VerificationReasonCode.RequiredFieldMissing,
                            $"{spec.Name} value '{value}' is not in the schema's allowed values.");
                    }
                    break;

                case "text":
                case "tel":
                    if (!string.IsNullOrEmpty(spec.Pattern))
                    {
                        Regex regex;
                        try
                        {
                            regex = new Regex(spec.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(50));
                        }
                        catch (ArgumentException)
                        {
                            return (false, VerificationReasonCode.MarketUnsupported,
                                $"Schema pattern for field '{spec.Name}' is malformed.");
                        }
                        if (!regex.IsMatch(value))
                        {
                            return (false,
                                IsRegulatorField(spec.Name)
                                    ? VerificationReasonCode.RegulatorIdentifierInvalid
                                    : VerificationReasonCode.RequiredFieldMissing,
                                $"{spec.Name} does not match the schema's pattern.");
                        }
                    }
                    break;
            }
        }

        return (true, null, null);
    }

    private static string? ResolveFieldValue(SubmitVerificationRequest request, string fieldName) =>
        fieldName switch
        {
            "profession" => request.Profession,
            "regulator_identifier" => request.RegulatorIdentifier,
            _ => null,
        };

    private static bool IsRegulatorField(string fieldName) =>
        string.Equals(fieldName, "regulator_identifier", StringComparison.Ordinal);
}
