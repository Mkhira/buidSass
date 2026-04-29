using BackendApi.Modules.Verification.Admin.DecideApprove;
using BackendApi.Modules.Verification.Primitives;

namespace BackendApi.Modules.Verification.Admin.Common;

/// <summary>
/// Shared validator for the bilingual <see cref="ReviewerReason"/> body used by
/// all three decide handlers (approve, reject, request-info). Extracted so the
/// three slices stay in sync on FR-033 (at least one locale required, max
/// length, etc).
/// </summary>
public static class ReviewerReasonValidator
{
    public const int ReasonMaxLength = 1000;

    /// <summary>
    /// Validates the reviewer reason. Returns ok=false with
    /// <see cref="VerificationReasonCode.ReviewReasonRequired"/> when neither
    /// locale is present, when both are blank, or when either locale exceeds
    /// the max length.
    /// </summary>
    public static (bool ok, VerificationReasonCode? reason, string? detail) Validate(ReviewerReason? reason)
    {
        if (reason is null)
        {
            return (false, VerificationReasonCode.ReviewReasonRequired,
                "reason is required.");
        }

        if (string.IsNullOrWhiteSpace(reason.En) && string.IsNullOrWhiteSpace(reason.Ar))
        {
            return (false, VerificationReasonCode.ReviewReasonRequired,
                "reason MUST include at least one of 'en' or 'ar'.");
        }

        if (reason.En is { Length: > ReasonMaxLength })
        {
            return (false, VerificationReasonCode.ReviewReasonRequired,
                $"reason.en exceeds {ReasonMaxLength} characters.");
        }

        if (reason.Ar is { Length: > ReasonMaxLength })
        {
            return (false, VerificationReasonCode.ReviewReasonRequired,
                $"reason.ar exceeds {ReasonMaxLength} characters.");
        }

        return (true, null, null);
    }

    /// <summary>
    /// Builds a human-readable ledger summary for the transition row's <c>Reason</c>
    /// column. The full bilingual payload is preserved separately in the
    /// metadata jsonb and the audit event_data.
    /// </summary>
    public static string ComposeLedgerSummary(ReviewerReason reason, string fallbackTrigger)
    {
        if (!string.IsNullOrWhiteSpace(reason.En)) return reason.En!;
        if (!string.IsNullOrWhiteSpace(reason.Ar)) return reason.Ar!;
        return fallbackTrigger;
    }

    /// <summary>
    /// Serializes the reason to the metadata jsonb shape consumed by audit
    /// replay tooling.
    /// </summary>
    public static string SerializeMetadata(ReviewerReason reason, IReadOnlyDictionary<string, object?>? extra = null)
    {
        var payload = new Dictionary<string, object?>(2 + (extra?.Count ?? 0));
        if (reason.En is not null) payload["reason_en"] = reason.En;
        if (reason.Ar is not null) payload["reason_ar"] = reason.Ar;
        if (extra is not null)
        {
            foreach (var (k, v) in extra) payload[k] = v;
        }
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }
}
