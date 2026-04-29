using BackendApi.Modules.Shared;

namespace BackendApi.Modules.Verification.Eligibility;

/// <summary>
/// Test-fixture stub for <see cref="IProductRestrictionPolicy"/>. Spec 020 must
/// be testable without depending on spec 005 (Catalog) — the policy interface
/// lives in <c>Modules/Shared/</c> precisely so 020 can publish without a
/// module cycle. This stub is registered ONLY by test fixtures; the production
/// binding waits for spec 005's PR (per tasks T086).
///
/// <para>Behavior: a SKU prefix dictates the restriction view, so test cases
/// can encode the policy in the SKU string and avoid plumbing setup state:</para>
/// <list type="bullet">
///   <item><c>UN-*</c> — unrestricted (empty <c>RestrictedInMarkets</c>).</item>
///   <item><c>KSA-*</c> — restricted in KSA only.</item>
///   <item><c>EG-*</c> — restricted in EG only.</item>
///   <item><c>BOTH-*</c> — restricted in both markets.</item>
///   <item><c>DENTIST-*</c> — restricted in both markets, requires <c>dentist</c>
///         profession (suffix encodes the market: <c>DENTIST-KSA-*</c>,
///         <c>DENTIST-EG-*</c>, <c>DENTIST-BOTH-*</c>).</item>
///   <item>anything else — unrestricted (silent path).</item>
/// </list>
/// The stub is deterministic and does no I/O — meets the <c>≤ 1 ms p95</c>
/// budget required by <see cref="IProductRestrictionPolicy"/> trivially.
/// </summary>
public sealed class StubProductRestrictionPolicy : IProductRestrictionPolicy
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();
    private static readonly IReadOnlySet<string> Ksa = new HashSet<string> { "ksa" };
    private static readonly IReadOnlySet<string> Eg = new HashSet<string> { "eg" };
    private static readonly IReadOnlySet<string> Both = new HashSet<string> { "ksa", "eg" };

    public ValueTask<ProductRestrictionPolicy> GetForSkuAsync(string sku, CancellationToken ct)
    {
        var policy = sku switch
        {
            { } s when s.StartsWith("UN-", StringComparison.Ordinal)
                => new ProductRestrictionPolicy(s, Empty, RequiredProfession: null, VendorId: null),

            { } s when s.StartsWith("KSA-", StringComparison.Ordinal)
                => new ProductRestrictionPolicy(s, Ksa, RequiredProfession: null, VendorId: null),

            { } s when s.StartsWith("EG-", StringComparison.Ordinal)
                => new ProductRestrictionPolicy(s, Eg, RequiredProfession: null, VendorId: null),

            { } s when s.StartsWith("BOTH-", StringComparison.Ordinal)
                => new ProductRestrictionPolicy(s, Both, RequiredProfession: null, VendorId: null),

            { } s when s.StartsWith("DENTIST-KSA-", StringComparison.Ordinal)
                => new ProductRestrictionPolicy(s, Ksa, RequiredProfession: "dentist", VendorId: null),

            { } s when s.StartsWith("DENTIST-EG-", StringComparison.Ordinal)
                => new ProductRestrictionPolicy(s, Eg, RequiredProfession: "dentist", VendorId: null),

            { } s when s.StartsWith("DENTIST-BOTH-", StringComparison.Ordinal) || s.StartsWith("DENTIST-", StringComparison.Ordinal)
                => new ProductRestrictionPolicy(s, Both, RequiredProfession: "dentist", VendorId: null),

            _ => new ProductRestrictionPolicy(sku, Empty, RequiredProfession: null, VendorId: null),
        };
        return ValueTask.FromResult(policy);
    }
}
