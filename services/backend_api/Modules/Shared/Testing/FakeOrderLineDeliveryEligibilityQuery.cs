namespace BackendApi.Modules.Shared.Testing;

/// <summary>
/// In-process fake of <see cref="IOrderLineDeliveryEligibilityQuery"/> for use
/// by integration tests that exercise spec 022's review submission paths
/// without requiring spec 011 to be at DoD on <c>main</c>. Defaults to
/// "eligible with delivery 2 days ago" — override via the constructor for
/// negative-path scenarios.
/// </summary>
public sealed class FakeOrderLineDeliveryEligibilityQuery : IOrderLineDeliveryEligibilityQuery
{
    private readonly bool _eligible;
    private readonly string? _reasonCode;
    private readonly DateTimeOffset? _deliveredAt;
    private readonly Guid? _orderLineId;

    public FakeOrderLineDeliveryEligibilityQuery(
        bool eligible = true,
        string? reasonCode = null,
        DateTimeOffset? deliveredAt = null,
        Guid? orderLineId = null)
    {
        _eligible = eligible;
        _reasonCode = reasonCode ?? (eligible ? null : "review.eligibility.no_delivered_purchase");
        _deliveredAt = deliveredAt ?? (eligible ? DateTimeOffset.UtcNow.AddDays(-2) : null);
        _orderLineId = orderLineId ?? (eligible ? Guid.NewGuid() : null);
    }

    public Task<OrderLineDeliveryEligibilityResult> IsEligibleForReviewAsync(
        Guid customerId,
        Guid productId,
        CancellationToken ct) =>
        Task.FromResult(new OrderLineDeliveryEligibilityResult(
            _eligible, _reasonCode, _deliveredAt, _orderLineId));
}
