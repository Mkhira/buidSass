using BackendApi.Modules.Verification.Primitives;

namespace BackendApi.Modules.Shared;

/// <summary>
/// The single authoritative answer to "may this customer purchase this restricted SKU
/// right now?" — declared here so spec 020 can publish without a module-cycle to its
/// consumers.
///
/// Consumers: Catalog (005), Cart (009), Checkout (010), Quotes-and-B2B (021),
/// Support-Tickets (023), Admin Customers (019). Implementations MUST NOT reimplement
/// this policy in their own modules (FR-024 of spec 020).
///
/// Latency budget (locked by spec 020 SC-004):
/// <list type="bullet">
///   <item>p95 ≤ 5 ms,</item>
///   <item>p99 ≤ 15 ms.</item>
/// </list>
///
/// Result is deterministic for <c>(customerId, sku, point-in-time)</c>; the underlying
/// implementation reads from <c>verification_eligibility_cache</c> joined against
/// <c>IProductRestrictionPolicy.GetForSkuAsync</c>.
/// </summary>
public interface ICustomerVerificationEligibilityQuery
{
    /// <summary>
    /// Single-SKU evaluation. See <see cref="ICustomerVerificationEligibilityQuery"/>
    /// for the latency contract.
    /// </summary>
    ValueTask<EligibilityResult> EvaluateAsync(
        Guid customerId,
        string sku,
        CancellationToken cancellationToken);

    /// <summary>
    /// Bulk variant for catalog list pages. Same per-SKU semantics, batched.
    /// Latency budget: p95 ≤ 15 ms for batches up to 50 SKUs.
    /// </summary>
    ValueTask<IReadOnlyDictionary<string, EligibilityResult>> EvaluateManyAsync(
        Guid customerId,
        IReadOnlyCollection<string> skus,
        CancellationToken cancellationToken);
}
