using System.Security.Cryptography;
using System.Text;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Entities;
using BackendApi.Modules.Identity.Persistence;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class RateLimitAuditSink(
    IdentityDbContext dbContext,
    IAuditEventPublisher auditEventPublisher) : IRateLimitAuditSink
{
    private readonly IdentityDbContext _dbContext = dbContext;
    private readonly IAuditEventPublisher _auditEventPublisher = auditEventPublisher;

    public async Task RecordRejectedAsync(
        string policyCode,
        string scopeKey,
        string surface,
        CancellationToken cancellationToken)
    {
        var scopeKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(scopeKey));
        var entry = new RateLimitEvent
        {
            Id = Guid.NewGuid(),
            PolicyCode = policyCode,
            ScopeKeyHash = scopeKeyHash,
            BlockedAt = DateTimeOffset.UtcNow,
            Surface = surface,
        };
        _dbContext.RateLimitEvents.Add(entry);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditEventPublisher.PublishAsync(
            new AuditEvent(
                ActorId: IdentityAuditActors.AnonymousActorId,
                ActorRole: surface,
                Action: "rate_limit.rejected",
                EntityType: nameof(RateLimitEvent),
                EntityId: entry.Id,
                BeforeState: null,
                AfterState: new
                {
                    entry.PolicyCode,
                    entry.Surface,
                    ScopeKeyHash = Convert.ToHexString(scopeKeyHash),
                    entry.BlockedAt,
                },
                Reason: policyCode),
            cancellationToken);
    }
}
