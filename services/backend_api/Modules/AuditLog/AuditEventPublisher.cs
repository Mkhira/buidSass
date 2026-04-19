using System.Text.Json;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.AuditLog;

public sealed class AuditEventPublisher(AppDbContext dbContext, IHttpContextAccessor httpContextAccessor) : IAuditEventPublisher
{
    public async Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        Validate(auditEvent);

        var correlationId = ResolveCorrelationId();
        var entry = new AuditLogEntry
        {
            ActorId = auditEvent.ActorId,
            ActorRole = auditEvent.ActorRole,
            Action = auditEvent.Action,
            EntityType = auditEvent.EntityType,
            EntityId = auditEvent.EntityId,
            BeforeState = auditEvent.BeforeState is null ? null : JsonSerializer.Serialize(auditEvent.BeforeState),
            AfterState = auditEvent.AfterState is null ? null : JsonSerializer.Serialize(auditEvent.AfterState),
            CorrelationId = correlationId,
            Reason = auditEvent.Reason,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        dbContext.AuditLogEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Guid ResolveCorrelationId()
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx is null)
        {
            return Guid.NewGuid();
        }

        if (ctx.Items.TryGetValue("CorrelationId", out var correlationItem) &&
            correlationItem is string correlationString &&
            Guid.TryParse(correlationString, out var parsed))
        {
            return parsed;
        }

        if (ctx.Request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out parsed))
        {
            return parsed;
        }

        return Guid.NewGuid();
    }

    private static void Validate(AuditEvent auditEvent)
    {
        if (auditEvent.ActorId == Guid.Empty) throw new ArgumentException("actor_id is required");
        if (auditEvent.EntityId == Guid.Empty) throw new ArgumentException("entity_id is required");
        if (string.IsNullOrWhiteSpace(auditEvent.ActorRole)) throw new ArgumentException("actor_role is required");
        if (string.IsNullOrWhiteSpace(auditEvent.Action)) throw new ArgumentException("action is required");
        if (string.IsNullOrWhiteSpace(auditEvent.EntityType)) throw new ArgumentException("entity_type is required");
    }
}
