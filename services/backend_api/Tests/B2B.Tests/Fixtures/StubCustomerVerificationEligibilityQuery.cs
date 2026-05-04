using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Primitives;

namespace B2B.Tests.Fixtures;

/// <summary>
/// Spec 021 — test-only stub for <see cref="ICustomerVerificationEligibilityQuery"/>.
/// Owned by spec 020 (Verification) in production. Consumed by:
/// <list type="bullet">
///   <item>The Submit-Acceptance handler (T070, FR-036) for restricted-SKU lines on a quote.</item>
///   <item>The Admin Quote-Detail handler (T086) for the <c>verification_warnings</c> advisory block.</item>
/// </list>
///
/// Default behavior: every (customer, sku) pair is <see cref="EligibilityClass.Unrestricted"/>
/// — the silent-pass path that matches the unrestricted default of
/// <see cref="StubProductRestrictionPolicy"/>. Tests opt into <see cref="EligibilityClass.Eligible"/>
/// or <see cref="EligibilityClass.Ineligible"/> by seeding <see cref="ResultBySkuAndCustomer"/>.
///
/// Tests do not need to fake the latency budget — the stub returns synchronously.
/// </summary>
public sealed class StubCustomerVerificationEligibilityQuery : ICustomerVerificationEligibilityQuery
{
    /// <summary>
    /// Tuple key: <c>(customerId, sku)</c>. Customer-current-market is informational
    /// at the stub layer — production keys eligibility cache reads off it; the stub
    /// ignores it to keep test setup terse.
    /// </summary>
    public Dictionary<(Guid customerId, string sku), EligibilityResult> ResultBySkuAndCustomer { get; } = new();

    public List<(Guid customerId, string market, string sku)> SingleInvocations { get; } = new();
    public List<(Guid customerId, string market, IReadOnlyCollection<string> skus)> BatchInvocations { get; } = new();

    public ValueTask<EligibilityResult> EvaluateAsync(
        Guid customerId,
        string customerCurrentMarket,
        string sku,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerCurrentMarket);
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        SingleInvocations.Add((customerId, customerCurrentMarket, sku));

        if (ResultBySkuAndCustomer.TryGetValue((customerId, sku), out var seeded))
        {
            return ValueTask.FromResult(seeded);
        }
        return ValueTask.FromResult(Unrestricted());
    }

    public ValueTask<IReadOnlyDictionary<string, EligibilityResult>> EvaluateManyAsync(
        Guid customerId,
        string customerCurrentMarket,
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerCurrentMarket);
        ArgumentNullException.ThrowIfNull(skus);
        BatchInvocations.Add((customerId, customerCurrentMarket, skus));

        var results = new Dictionary<string, EligibilityResult>(skus.Count);
        foreach (var sku in skus)
        {
            results[sku] = ResultBySkuAndCustomer.TryGetValue((customerId, sku), out var seeded)
                ? seeded
                : Unrestricted();
        }
        return ValueTask.FromResult<IReadOnlyDictionary<string, EligibilityResult>>(results);
    }

    private static EligibilityResult Unrestricted() => new(
        Class: EligibilityClass.Unrestricted,
        ReasonCode: EligibilityReasonCode.Unrestricted,
        MessageKey: EligibilityReasonCodeExtensions.ToIcuKey(EligibilityReasonCode.Unrestricted),
        ExpiresAt: null);
}
