using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Orders.Entities;

namespace BackendApi.Modules.Orders.Admin.Fulfillment.Common;

/// <summary>
/// Helpers shared across the admin fulfillment slices. Keeps the audit + transition + outbox
/// emission consistent across StartPicking / MarkPacked / CreateShipment / MarkHandedToCarrier
/// / MarkDelivered.
/// </summary>
internal static class FulfillmentOps
{
    public static OrderStateTransition NewTransition(
        Guid orderId, string machine, string from, string to, Guid? actor, string trigger, string? reason, DateTimeOffset nowUtc) =>
        new()
        {
            OrderId = orderId,
            Machine = machine,
            FromState = from,
            ToState = to,
            ActorAccountId = actor,
            Trigger = trigger,
            Reason = reason,
            OccurredAt = nowUtc,
        };

    public static OrdersOutboxEntry NewOutbox(Entities.Order order, string eventType, object? extra = null) =>
        new()
        {
            EventType = eventType,
            AggregateId = order.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                orderNumber = order.OrderNumber,
                extra,
            }),
            CommittedAt = DateTimeOffset.UtcNow,
            DispatchedAt = null,
        };

    /// <summary>
    /// FR-019 / Principle 25 — every admin mutation writes an audit row. Spec 003's audit
    /// publisher is the shared seam (research R12).
    /// </summary>
    public static async Task EmitAdminAuditAsync(
        IAuditEventPublisher auditPublisher,
        Guid orderId,
        Guid actorAccountId,
        string action,
        object? before,
        object? after,
        string? reason,
        CancellationToken ct)
    {
        try
        {
            await auditPublisher.PublishAsync(
                new AuditEvent(
                    ActorId: actorAccountId,
                    ActorRole: "admin",
                    Action: action,
                    EntityType: "orders.order",
                    EntityId: orderId,
                    BeforeState: before,
                    AfterState: after,
                    Reason: reason),
                ct);
        }
        catch
        {
            // Audit failures must never roll back the customer-facing mutation. The local
            // order_state_transitions row is still the canonical state-machine trace.
        }
    }
}
