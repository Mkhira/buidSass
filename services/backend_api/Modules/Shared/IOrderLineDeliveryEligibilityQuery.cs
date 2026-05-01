namespace BackendApi.Modules.Shared;

/// <summary>
/// Read-side eligibility check used by spec 022 review submission to enforce the
/// verified-buyer gate (Principle 15). Spec 011 (orders) implements; spec 022
/// consumes. The interface lives in <c>Modules/Shared</c> to avoid module
/// dependency cycles.
/// </summary>
public interface IOrderLineDeliveryEligibilityQuery
{
    /// <summary>
    /// Returns the customer's most-recent delivered, non-refunded order line for
    /// <paramref name="productId"/>. The caller compares <see cref="OrderLineDeliveryEligibilityResult.DeliveredAt"/>
    /// against the per-market eligibility window.
    /// </summary>
    Task<OrderLineDeliveryEligibilityResult> IsEligibleForReviewAsync(
        Guid customerId,
        Guid productId,
        CancellationToken ct);
}

/// <summary>
/// Outcome of an eligibility query. <see cref="ReasonCode"/> is one of the
/// <c>review.eligibility.*</c> codes from spec 022 contract §10 when
/// <see cref="Eligible"/> is <see langword="false"/>.
/// </summary>
public sealed record OrderLineDeliveryEligibilityResult(
    bool Eligible,
    string? ReasonCode,
    DateTimeOffset? DeliveredAt,
    Guid? OrderLineId);
