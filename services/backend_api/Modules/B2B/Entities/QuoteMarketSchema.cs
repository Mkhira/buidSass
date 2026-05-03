namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 per-market policy row (data-model §2.9). Versioned via composite PK
/// <c>(market_code, version)</c>. At most one active row per market — partial unique
/// index on <c>(market_code) WHERE effective_to IS NULL</c>.
///
/// Schema changes ship as a new <see cref="Version"/> row from the seeder, never as an
/// UPDATE on the active row (so historical quotes can resolve their snapshot via the
/// composite FK on <c>quotes.schema_version</c>).
/// </summary>
public sealed class QuoteMarketSchema
{
    /// <summary>Lowercase: <c>eg</c> or <c>ksa</c>. Composite PK part 1.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>Composite PK part 2.</summary>
    public int Version { get; set; }

    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>NULL = currently active.</summary>
    public DateTimeOffset? EffectiveTo { get; set; }

    public int ValidityDays { get; set; } = 14;

    public int RateLimitPerCustomerPerHour { get; set; } = 10;

    public int RateLimitPerCompanyPerHour { get; set; } = 50;

    /// <summary>Default <c>false</c> per Clarifications Q2.</summary>
    public bool CompanyVerificationRequired { get; set; }

    public decimal TaxPreviewDriftThresholdPct { get; set; } = 5.00m;

    public int SlaDecisionBusinessDays { get; set; } = 2;

    public int SlaWarningBusinessDays { get; set; } = 1;

    public int InvitationTtlDays { get; set; } = 14;

    /// <summary>jsonb array: <c>["YYYY-MM-DD", ...]</c>. Default <c>[]</c>.</summary>
    public string HolidaysListJson { get; set; } = "[]";
}
