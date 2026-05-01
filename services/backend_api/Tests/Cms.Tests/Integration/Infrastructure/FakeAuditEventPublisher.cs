using System.Collections.Concurrent;
using BackendApi.Modules.AuditLog;

namespace Cms.Tests.Integration.Infrastructure;

public sealed class FakeAuditEventPublisher : IAuditEventPublisher
{
    public ConcurrentBag<AuditEvent> Events { get; } = new();

    public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}
