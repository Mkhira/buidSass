using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Primitives;

namespace BackendApi.Modules.Identity.Authorization;

public interface IAuthorizationAuditEmitter
{
    Task EmitDecisionAsync(AuthorizationAuditDecision decision, CancellationToken cancellationToken);
}

public sealed record AuthorizationAuditDecision(
    Guid? AccountId,
    SurfaceKind Surface,
    string PermissionCode,
    string Decision,
    string ReasonCode,
    Guid CorrelationId);

// Implementation note:
// the concrete emitter (T034) forwards these decisions into IAuditEventPublisher.
