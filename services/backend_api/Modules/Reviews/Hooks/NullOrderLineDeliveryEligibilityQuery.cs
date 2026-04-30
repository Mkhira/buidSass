using BackendApi.Modules.Shared;

namespace BackendApi.Modules.Reviews.Hooks;

/// <summary>
/// Fallback implementation of <see cref="IOrderLineDeliveryEligibilityQuery"/>
/// shipped while spec 011 (orders) integrates. Always returns
/// <see cref="OrderLineDeliveryEligibilityResult.Eligible"/>=<see langword="false"/>
/// so submission falls back to the conservative "no delivered purchase" path.
///
/// Spec 011 supplies the production binding via <see cref="ServiceCollectionDescriptorExtensions.TryAddScoped"/>
/// — the runtime swap is automatic once the spec-011 PR lands.
/// </summary>
public sealed class NullOrderLineDeliveryEligibilityQuery : IOrderLineDeliveryEligibilityQuery
{
    public Task<OrderLineDeliveryEligibilityResult> IsEligibleForReviewAsync(
        Guid customerId,
        Guid productId,
        CancellationToken ct)
    {
        return Task.FromResult(new OrderLineDeliveryEligibilityResult(
            Eligible: false,
            ReasonCode: "review.eligibility.no_delivered_purchase",
            DeliveredAt: null,
            OrderLineId: null));
    }
}
