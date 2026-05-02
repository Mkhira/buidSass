namespace BackendApi.Modules.Shared;

/// <summary>
/// Cross-module read contract used by spec 024 CMS for live-resolving
/// featured-section product references and validating banner CTA targets.
/// Provenance: declared by spec 005 if shipped; otherwise newly declared
/// here per spec 024 data-model §7.
/// </summary>
public interface ICatalogProductReadContract
{
    Task<CatalogProductRead> ReadAsync(Guid productId, string marketCode, CancellationToken ct);
}

public sealed record CatalogProductRead(
    Guid ProductId,
    string MarketCode,
    string DisplayNameAr,
    string DisplayNameEn,
    Guid? VendorId,
    bool IsAvailable,
    LinkedEntityUnavailableReason? UnavailableReason);

public enum LinkedEntityUnavailableReason
{
    Archived,
    SoftDeleted,
    MarketMismatched,
    NotFound,
}
