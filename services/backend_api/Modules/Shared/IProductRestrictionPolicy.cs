namespace BackendApi.Modules.Shared;

/// <summary>
/// Returns the restriction policy for a SKU. Declared in <c>Modules/Shared/</c> so
/// spec 020 can reference without cycling on spec 005 (project-memory rule).
///
/// Implementation owned by spec 005 (Catalog) — given a SKU, returns:
/// <list type="bullet">
///   <item>which markets restrict the product,</item>
///   <item>the required profession (if any) of the buyer,</item>
///   <item>vendor scoping (V1: always null; reserved for Phase 2 multi-vendor).</item>
/// </list>
///
/// V1 contract: the implementation MUST be deterministic for
/// <c>(sku, point-in-time)</c> and SHOULD complete in ≤ 1 ms p95 (read-side cache).
/// </summary>
public interface IProductRestrictionPolicy
{
    /// <summary>
    /// Returns the restriction policy for a SKU. May return an
    /// <c>Unrestricted</c> result (empty <see cref="ProductRestrictionPolicy.RestrictedInMarkets"/>)
    /// for products that are not subject to professional verification.
    /// </summary>
    ValueTask<ProductRestrictionPolicy> GetForSkuAsync(string sku, CancellationToken ct);
}

/// <param name="Sku">The SKU under evaluation.</param>
/// <param name="RestrictedInMarkets">Empty = unrestricted; otherwise the set of market codes ("eg", "ksa") in which the product requires verification.</param>
/// <param name="RequiredProfession">When set, only customers approved with this profession may purchase. Null = any verified profession suffices.</param>
/// <param name="VendorId">V1: always <c>null</c>. Reserved for Phase 2 multi-vendor.</param>
public sealed record ProductRestrictionPolicy(
    string Sku,
    IReadOnlySet<string> RestrictedInMarkets,
    string? RequiredProfession,
    Guid? VendorId);
