using BackendApi.Modules.Shared;

namespace B2B.Tests.Fixtures;

/// <summary>
/// Spec 021 — test-only stub for <see cref="IProductCatalogQuery"/>. Owned by
/// spec 005 (Catalog) in production. Two narrow probes consumed here:
/// <list type="bullet">
///   <item><c>IsActiveAsync(sku)</c> — admin authoring uses this to flag
///         <c>archived_sku_lines</c> advisories on the quote-detail surface.</item>
///   <item><c>IsQuotableAsync(productId)</c> — the quote-from-product intake (US2)
///         rejects with <c>quote.product_not_quotable</c> when this returns false.</item>
/// </list>
///
/// Defaults: every SKU is active, every product is quotable. Tests opt into negative
/// paths by seeding <see cref="InactiveSkus"/> or <see cref="NonQuotableProductIds"/>.
/// </summary>
public sealed class StubProductCatalogQuery : IProductCatalogQuery
{
    public HashSet<string> InactiveSkus { get; } = new();
    public HashSet<Guid> NonQuotableProductIds { get; } = new();

    public ValueTask<bool> IsActiveAsync(string sku, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        return ValueTask.FromResult(!InactiveSkus.Contains(sku));
    }

    public ValueTask<bool> IsQuotableAsync(Guid productId, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(!NonQuotableProductIds.Contains(productId));
    }
}
