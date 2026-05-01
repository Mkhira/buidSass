namespace BackendApi.Modules.Shared;

/// <summary>
/// Cross-module read contract used by spec 024 CMS for live-resolving
/// featured-section category references and validating banner CTA targets.
/// </summary>
public interface ICatalogCategoryReadContract
{
    Task<CatalogCategoryRead> ReadAsync(Guid categoryId, string marketCode, CancellationToken ct);
}

public sealed record CatalogCategoryRead(
    Guid CategoryId,
    string MarketCode,
    string DisplayNameAr,
    string DisplayNameEn,
    bool IsAvailable,
    LinkedEntityUnavailableReason? UnavailableReason);
