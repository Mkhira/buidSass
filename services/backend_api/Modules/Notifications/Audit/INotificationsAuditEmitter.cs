using BackendApi.Modules.AuditLog;

namespace BackendApi.Modules.Notifications.Audit;

/// <summary>
/// Thin wrapper around <see cref="IAuditEventPublisher"/> that enforces
/// Notifications-module conventions:
/// 1. EntityType is always <c>notification.&lt;subdomain&gt;</c> so
///    audit-log consumers can filter on a stable prefix.
/// 2. BeforeState / AfterState are passed through verbatim — the audit
///    publisher will serialize them.
/// 3. Reason is mandatory for operator-action audits
///    (Discard / Failover / Unsubscribe-by-operator).
///
/// Handlers call this rather than <see cref="IAuditEventPublisher"/> directly
/// so emitter swap or test-fake injection is one-line.
/// </summary>
public interface INotificationsAuditEmitter
{
    Task EmitAsync(
        string eventKind,
        Guid actorId,
        string actorRole,
        Guid entityId,
        object? beforeState,
        object? afterState,
        string? reason,
        CancellationToken cancellationToken);
}

public sealed class NotificationsAuditEmitter : INotificationsAuditEmitter
{
    private readonly IAuditEventPublisher _publisher;

    public NotificationsAuditEmitter(IAuditEventPublisher publisher)
    {
        _publisher = publisher;
    }

    public Task EmitAsync(
        string eventKind,
        Guid actorId,
        string actorRole,
        Guid entityId,
        object? beforeState,
        object? afterState,
        string? reason,
        CancellationToken cancellationToken)
    {
        var entityType = eventKind.StartsWith("notifications.")
            ? eventKind[..eventKind.IndexOf('.', "notifications.".Length)]
            : "notification";
        var evt = new AuditEvent(
            ActorId: actorId,
            ActorRole: actorRole,
            Action: eventKind,
            EntityType: entityType,
            EntityId: entityId,
            BeforeState: beforeState,
            AfterState: afterState,
            Reason: reason);
        return _publisher.PublishAsync(evt, cancellationToken);
    }
}
