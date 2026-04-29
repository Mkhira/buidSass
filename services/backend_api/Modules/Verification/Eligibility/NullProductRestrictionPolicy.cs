using BackendApi.Modules.Shared;

namespace BackendApi.Modules.Verification.Eligibility;

/// <summary>
/// Null fallback for <see cref="IProductRestrictionPolicy"/> until spec 005
/// (Catalog) ships its production binding. Returns an unrestricted result for
/// every SKU — meaning V1 catalogs without restriction metadata behave as if
/// no SKU is restricted.
///
/// <para>Registered via <c>TryAddSingleton</c> so spec 005's binding wins once
/// it lands. Test fixtures override this with
/// <see cref="StubProductRestrictionPolicy"/>.</para>
/// </summary>
public sealed class NullProductRestrictionPolicy : IProductRestrictionPolicy
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    public ValueTask<ProductRestrictionPolicy> GetForSkuAsync(string sku, CancellationToken ct)
        => ValueTask.FromResult(new ProductRestrictionPolicy(sku, Empty, RequiredProfession: null, VendorId: null));
}
