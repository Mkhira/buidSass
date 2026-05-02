namespace BackendApi.Modules.Shared;

/// <summary>
/// Cross-module read contract used by spec 024 CMS for live-resolving
/// featured-section bundle references and validating banner CTA targets.
/// </summary>
public interface ICatalogBundleReadContract
{
    Task<CatalogBundleRead> ReadAsync(Guid bundleId, string marketCode, CancellationToken ct);
}

public sealed record CatalogBundleRead(
    Guid BundleId,
    string MarketCode,
    string DisplayNameAr,
    string DisplayNameEn,
    bool IsAvailable,
    LinkedEntityUnavailableReason? UnavailableReason);
