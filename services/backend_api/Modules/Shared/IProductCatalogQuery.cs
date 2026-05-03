namespace BackendApi.Modules.Shared;

/// <summary>
/// Spec 021 → spec 005 catalog probe (tasks T044a). Lightweight read-only contract used
/// by quote authoring + product-quote intake to verify SKU presence + quotability before
/// inserting a quote line.
///
/// Two narrow methods rather than a full <c>IProductReadContract</c>:
/// <list type="bullet">
///   <item><see cref="IsActiveAsync"/> — does this SKU exist and have <c>state=active</c>
///         in the catalog? Used by admin authoring to flag an <c>archived_sku_lines</c>
///         advisory when an existing quote line references a since-archived SKU.</item>
///   <item><see cref="IsQuotableAsync"/> — does this product permit quote requests at all?
///         (E.g. clearance items may be tagged <c>quotable=false</c>.) Used by the
///         quote-from-product intake (US2).</item>
/// </list>
///
/// Lives in <c>Modules/Shared/</c> so spec 021 can compile + run without spec 005 at DoD
/// on main; the production implementation lands with spec 005, and tests stub via
/// <c>FakeProductCatalogQuery</c> in <c>Tests/B2B.Tests/Fixtures/</c>.
/// </summary>
public interface IProductCatalogQuery
{
    ValueTask<bool> IsActiveAsync(string sku, CancellationToken cancellationToken);
    ValueTask<bool> IsQuotableAsync(Guid productId, CancellationToken cancellationToken);
}
