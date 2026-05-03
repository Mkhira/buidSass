using BackendApi.Modules.B2B.Entities;

namespace BackendApi.Modules.B2B.Primitives;

/// <summary>
/// Spec 021 market-aware policy value object (data-model §2.9). Resolved from a single
/// <see cref="QuoteMarketSchema"/> row; immutable after construction.
///
/// All per-market knobs that gate handler behavior live here so user-story slices never
/// need to read <see cref="QuoteMarketSchema"/> directly — they take a
/// <see cref="QuoteMarketPolicy"/> dependency. This keeps Principle 5 (market behavior
/// driven by configuration, not hardcoded business logic) encapsulated.
/// </summary>
public sealed record QuoteMarketPolicy(
    string MarketCode,
    int SchemaVersion,
    int ValidityDays,
    int RateLimitPerCustomerPerHour,
    int RateLimitPerCompanyPerHour,
    bool CompanyVerificationRequired,
    decimal TaxPreviewDriftThresholdPct,
    int SlaDecisionBusinessDays,
    int SlaWarningBusinessDays,
    int InvitationTtlDays,
    string HolidaysListJson)
{
    public static QuoteMarketPolicy FromEntity(QuoteMarketSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new QuoteMarketPolicy(
            MarketCode: schema.MarketCode,
            SchemaVersion: schema.Version,
            ValidityDays: schema.ValidityDays,
            RateLimitPerCustomerPerHour: schema.RateLimitPerCustomerPerHour,
            RateLimitPerCompanyPerHour: schema.RateLimitPerCompanyPerHour,
            CompanyVerificationRequired: schema.CompanyVerificationRequired,
            TaxPreviewDriftThresholdPct: schema.TaxPreviewDriftThresholdPct,
            SlaDecisionBusinessDays: schema.SlaDecisionBusinessDays,
            SlaWarningBusinessDays: schema.SlaWarningBusinessDays,
            InvitationTtlDays: schema.InvitationTtlDays,
            HolidaysListJson: schema.HolidaysListJson);
    }
}
