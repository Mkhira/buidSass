using BackendApi.Modules.AuditLog;

namespace BackendApi.Tests.Notifications.Support;

/// <summary>
/// Test double for <see cref="IAuditEventPublisher"/> that captures every
/// published event in-memory so assertions can verify both presence and
/// payload shape without standing up the real AppDbContext / audit pipeline.
/// </summary>
public sealed class FakeAuditEventPublisher : IAuditEventPublisher
{
    public List<AuditEvent> Published { get; } = new();

    public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        Published.Add(auditEvent);
        return Task.CompletedTask;
    }
}
