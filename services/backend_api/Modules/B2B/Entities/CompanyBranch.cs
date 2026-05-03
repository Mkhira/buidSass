namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 company branch (data-model §2.3). Optional sub-entity allowing companies to
/// scope shipping / billing / quote attribution to a sub-location. Branchless companies
/// are valid; branches never auto-created.
/// </summary>
public sealed class CompanyBranch
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>Two-letter market code (mirrors the parent <c>Company.MarketCode</c>); ADR-010 partitioning.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>Bilingual name jsonb: <c>{ "en": "...", "ar": "..." }</c>.</summary>
    public string NameJson { get; set; } = string.Empty;

    public string AddressJson { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
}
