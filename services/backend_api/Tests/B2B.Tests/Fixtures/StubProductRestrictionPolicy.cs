using BackendApi.Modules.Shared;

namespace B2B.Tests.Fixtures;

/// <summary>
/// Spec 021 — test-only stub for <see cref="IProductRestrictionPolicy"/>. Owned by
/// spec 005 (Catalog) in production; replaced here so B2B tests don't depend on spec 005
/// being at DoD on main.
///
/// Default behavior: every SKU returns an unrestricted policy (no required profession,
/// no per-market restriction). Tests that need a restricted SKU pre-seed
/// <see cref="PolicyBySku"/> with the desired <see cref="ProductRestrictionPolicy"/>
/// shape. The verification-eligibility flow (FR-036) keys off
/// <see cref="ProductRestrictionPolicy.RestrictedInMarkets"/> being non-empty, so the
/// default unrestricted result yields "no eligibility check required" — the right
/// safe default for happy-path tests.
/// </summary>
public sealed class StubProductRestrictionPolicy : IProductRestrictionPolicy
{
    /// <summary>SKUs absent here resolve to an unrestricted policy.</summary>
    public Dictionary<string, ProductRestrictionPolicy> PolicyBySku { get; } = new();

    /// <summary>Captured invocations — tests assert per-line restriction snapshotting.</summary>
    public List<string> Invocations { get; } = new();

    public ValueTask<ProductRestrictionPolicy> GetForSkuAsync(string sku, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        Invocations.Add(sku);

        if (PolicyBySku.TryGetValue(sku, out var seeded))
        {
            return ValueTask.FromResult(seeded);
        }

        var unrestricted = new ProductRestrictionPolicy(
            Sku: sku,
            RestrictedInMarkets: new HashSet<string>(),
            RequiredProfession: null,
            VendorId: null);
        return ValueTask.FromResult(unrestricted);
    }
}
