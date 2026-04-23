namespace BackendApi.Modules.Identity.Primitives;

public interface IRateLimitAuditSink
{
    Task RecordRejectedAsync(string policyCode, string scopeKey, string surface, CancellationToken cancellationToken);
}
