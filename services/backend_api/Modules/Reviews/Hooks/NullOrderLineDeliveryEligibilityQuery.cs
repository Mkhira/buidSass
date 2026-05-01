using BackendApi.Modules.Shared;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Reviews.Hooks;

/// <summary>
/// Fail-closed fallback for <see cref="IOrderLineDeliveryEligibilityQuery"/>
/// shipped while spec 011 (orders) integrates. Always returns
/// <see cref="OrderLineDeliveryEligibilityResult.Eligible"/>=<see langword="false"/>,
/// so submission falls back to <c>review.eligibility.no_delivered_purchase</c>
/// — i.e. NO ONE can submit a review until spec 011's real binding ships.
///
/// <para><b>Production-deploy gate</b>: this binding is the safe default for
/// pre-launch dev environments. Shipping it to production silently disables
/// review submission. Each call emits an <see cref="LogLevel.Warning"/> log
/// so monitoring catches the misconfiguration. Operators MUST replace this
/// binding with spec 011's real <c>OrderLineDeliveryEligibilityQuery</c>
/// before launching the review surface to customers.</para>
///
/// <para>Replacement is automatic on DI resolution: spec 011 registers its
/// implementation via <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped"/>
/// before this fallback's <c>TryAdd</c> short-circuits.</para>
/// </summary>
public sealed class NullOrderLineDeliveryEligibilityQuery : IOrderLineDeliveryEligibilityQuery
{
    private readonly ILogger<NullOrderLineDeliveryEligibilityQuery>? _logger;

    public NullOrderLineDeliveryEligibilityQuery(ILogger<NullOrderLineDeliveryEligibilityQuery>? logger = null)
    {
        _logger = logger;
    }

    public Task<OrderLineDeliveryEligibilityResult> IsEligibleForReviewAsync(
        Guid customerId,
        Guid productId,
        CancellationToken ct)
    {
        // Raw customer/product IDs intentionally omitted — high-cardinality
        // warning noise + unnecessary identifier exposure on every hit.
        _logger?.LogWarning(
            "NullOrderLineDeliveryEligibilityQuery hit — review submission disabled until spec 011 ships its real eligibility binding.");

        return Task.FromResult(new OrderLineDeliveryEligibilityResult(
            Eligible: false,
            ReasonCode: "review.eligibility.no_delivered_purchase",
            DeliveredAt: null,
            OrderLineId: null));
    }
}
