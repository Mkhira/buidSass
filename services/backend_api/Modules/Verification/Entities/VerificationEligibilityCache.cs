namespace BackendApi.Modules.Verification.Entities;

/// <summary>
/// One row per customer — denormalized projection that the eligibility query
/// reads on every hot-path call. Rebuilt inside the same Tx as every state
/// transition by <c>EligibilityCacheInvalidator.RebuildAsync</c>. See spec 020
/// data-model §2.6.
/// </summary>
public sealed class VerificationEligibilityCache
{
    /// <summary>PK — one row per customer.</summary>
    public Guid CustomerId { get; set; }

    public string MarketCode { get; set; } = string.Empty;

    /// <summary>One of eligible / ineligible / unrestricted_only.</summary>
    public string EligibilityClass { get; set; } = "ineligible";

    /// <summary>Mirrors the active approval's <c>expires_at</c> when class is eligible.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>One of <c>EligibilityReasonCode</c> wire values; null when class is eligible.</summary>
    public string? ReasonCode { get; set; }

    /// <summary>jsonb — set of approved professions (e.g. <c>["dentist"]</c>). Empty when ineligible.</summary>
    public string ProfessionsJson { get; set; } = "[]";

    public DateTimeOffset ComputedAt { get; set; }
}
