namespace BackendApi.Modules.B2B.Entities;

/// <summary>
/// Spec 021 PDF artifact (data-model §2.7). Two rows per <see cref="QuoteVersion"/>:
/// one EN, one AR. Storage owned by <c>Modules/Storage</c>; this row tracks the
/// (version, locale) → storage key mapping.
/// </summary>
public sealed class QuoteVersionDocument
{
    public Guid Id { get; set; }
    public Guid QuoteVersionId { get; set; }

    /// <summary>Two-letter market code (mirrors the parent <c>QuoteVersion.MarketCode</c>); ADR-010 partitioning.</summary>
    public string MarketCode { get; set; } = string.Empty;

    /// <summary><c>en</c> or <c>ar</c>.</summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>Opaque storage key — resolved by <c>IStorageService</c>.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/pdf";

    public DateTimeOffset GeneratedAt { get; set; }
}
