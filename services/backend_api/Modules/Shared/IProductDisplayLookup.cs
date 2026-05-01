namespace BackendApi.Modules.Shared;

/// <summary>
/// Minimal product-name-by-id lookup used by the moderator queue + customer
/// "list my reviews" surface to render product context without taking a hard
/// reference on the catalog module's entities. Spec 005 (catalog) implements;
/// loose-coupling pattern from specs 020 / 021 / 007-b.
/// </summary>
public interface IProductDisplayLookup
{
    Task<ProductDisplay?> GetAsync(Guid productId, string marketCode, string locale, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, ProductDisplay>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        string marketCode,
        string locale,
        CancellationToken ct);
}

public sealed record ProductDisplay(Guid ProductId, string Name, string ImageUrl, string MarketCode);
