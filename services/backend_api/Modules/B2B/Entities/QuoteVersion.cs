namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 immutable QuoteVersion (data-model §2.6). One row per published draft; never
/// updated after insert (enforced by integration test
/// <c>QuoteVersionImmutabilityTests</c>). Customer-facing PDFs are pinned to a specific
/// version via <see cref="QuoteVersionDocument"/>.
/// </summary>
public sealed class QuoteVersion
{
    public Guid Id { get; set; }
    public Guid QuoteId { get; set; }

    /// <summary>Two-letter market code (mirrors the parent <c>Quote.MarketCode</c>); ADR-010 partitioning.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>Monotonic per <c>(QuoteId, MarketCode)</c>; UNIQUE <c>(quote_id, version_number, market_code)</c>.</summary>
    public int VersionNumber { get; set; }

    /// <summary>Logical FK to admin user identity (the operator who authored this revision).</summary>
    public Guid AuthoredBy { get; set; }

    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>
    /// Array of <c>{ sku, qty, baseline_unit_price, override_unit_price, override_reason: { en?, ar? }?,
    /// line_discount_amount, line_tax_preview, currency }</c>.
    /// </summary>
    public string LineItemsJson { get; set; } = "[]";

    /// <summary>Bilingual terms: <c>{ "en": "...", "ar": "..." }</c> — both required.</summary>
    public string TermsTextJson { get; set; } = """{"en":"","ar":""}""";

    /// <summary>Net-X term in days; <c>0</c> means "due on receipt".</summary>
    public int TermsDays { get; set; }

    /// <summary>True when this revision extended the parent quote's validity (Clarifications Q5).</summary>
    public bool ValidityExtends { get; set; }

    /// <summary><c>{ subtotal, total_discount, total_tax_preview, grand_total, currency }</c>.</summary>
    public string TotalsSummaryJson { get; set; } = "{}";

    /// <summary>Customer revision-request comment that triggered this version. NULL for first publish or admin-initiated revisions.</summary>
    public string? CustomerRevisionCommentJson { get; set; }
}
