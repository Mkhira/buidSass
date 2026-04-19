namespace BackendApi.Modules.AuditLog;

public interface IAuditEventPublisher
{
    Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}
