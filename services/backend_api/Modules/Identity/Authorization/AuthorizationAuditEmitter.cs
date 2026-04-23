using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;

namespace BackendApi.Modules.Identity.Authorization;

public sealed class AuthorizationAuditEmitter(
    IAuditEventPublisher auditEventPublisher,
    PolicyEvaluator policyEvaluator,
    IdentityDbContext identityDbContext,
    ILogger<AuthorizationAuditEmitter> logger) : IAuthorizationAuditEmitter
{
    private readonly IAuditEventPublisher _auditEventPublisher = auditEventPublisher;
    private readonly PolicyEvaluator _policyEvaluator = policyEvaluator;
    private readonly IdentityDbContext _identityDbContext = identityDbContext;
    private readonly ILogger<AuthorizationAuditEmitter> _logger = logger;
    private static readonly Guid AnonymousAuthorizationActorId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public async Task EmitDecisionAsync(AuthorizationAuditDecision decision, CancellationToken cancellationToken)
    {
        if (string.Equals(decision.Decision, "allow", StringComparison.OrdinalIgnoreCase)
            && !_policyEvaluator.AllowSamplingStrategy())
        {
            return;
        }

        _identityDbContext.AuthorizationAudits.Add(new AuthorizationAudit
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            AccountId = decision.AccountId,
            Surface = decision.Surface == Primitives.SurfaceKind.Admin ? "admin" : "customer",
            PermissionCode = decision.PermissionCode,
            Decision = decision.Decision,
            ReasonCode = decision.ReasonCode,
            CorrelationId = decision.CorrelationId,
        });
        await _identityDbContext.SaveChangesAsync(cancellationToken);

        var auditEvent = new AuditEvent(
            ActorId: decision.AccountId ?? AnonymousAuthorizationActorId,
            ActorRole: "identity.authorization",
            Action: "identity.authorization.decision",
            EntityType: "AuthorizationPolicy",
            EntityId: Guid.NewGuid(),
            BeforeState: null,
            AfterState: new
            {
                decision.Surface,
                decision.PermissionCode,
                decision.Decision,
                decision.ReasonCode,
                decision.CorrelationId,
            },
            Reason: decision.Decision);

        try
        {
            await _auditEventPublisher.PublishAsync(auditEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish cross-cutting authorization audit event.");
        }
    }
}
