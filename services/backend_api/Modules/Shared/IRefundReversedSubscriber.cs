namespace BackendApi.Modules.Shared;

/// <summary>
/// Subscriber contract for spec 013's refund-reversed event. Reversals are rare
/// + manual; spec 022 surfaces a "needs review" advisory on previously-hidden
/// reviews and does NOT auto-reinstate per FR-032.
/// </summary>
public interface IRefundReversedSubscriber
{
    Task OnRefundReversedAsync(RefundReversedEvent evt, CancellationToken ct);
}

public interface IRefundReversedPublisher
{
    Task PublishAsync(RefundReversedEvent evt, CancellationToken ct);
}

public sealed record RefundReversedEvent(
    Guid OrderLineId,
    Guid CustomerId,
    DateTimeOffset ReversedAtUtc,
    Guid ActorId,
    string ReasonNote);
