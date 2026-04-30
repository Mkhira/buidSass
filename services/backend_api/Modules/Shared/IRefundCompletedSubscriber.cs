namespace BackendApi.Modules.Shared;

/// <summary>
/// Subscriber contract for spec 013's refund-completed event. Spec 013 publishes
/// after a return-request reaches the <c>completed</c> terminal state and the
/// payment refund settles; spec 022 subscribes to auto-hide affected reviews
/// (FR-030).
/// </summary>
public interface IRefundCompletedSubscriber
{
    Task OnRefundCompletedAsync(RefundCompletedEvent evt, CancellationToken ct);
}

public interface IRefundCompletedPublisher
{
    Task PublishAsync(RefundCompletedEvent evt, CancellationToken ct);
}

/// <summary>
/// Emitted exactly once per (order_line, refund) reconciliation. Subscribers
/// MUST be idempotent — the in-process bus may redeliver after crash recovery.
/// </summary>
public sealed record RefundCompletedEvent(
    Guid OrderLineId,
    Guid CustomerId,
    DateTimeOffset CompletedAtUtc,
    Guid ActorId);
