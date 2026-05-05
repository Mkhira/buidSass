using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.B2B.Workers;

/// <summary>
/// Spec 021 task T136. Daily worker that transitions every <c>pending</c> company
/// invitation past its <c>expires_at</c> to <c>expired</c>; publishes
/// <see cref="CompanyInvitationExpired"/> and audits the transition.
///
/// Idempotent on re-run; advisory lock prevents double-execution by parallel
/// instances (research §R7).
/// </summary>
public sealed class InvitationExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<B2BWorkerOptions> options,
    TimeProvider clock,
    ILogger<InvitationExpiryWorker> logger) : BackgroundService
{
    private static readonly Guid SystemActorId = new("00000000-0000-0000-0000-00000B2B0210");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = options.Value.Invitation;
        var firstDelay = schedule.FirstDelay(clock.GetUtcNow());
        if (firstDelay > TimeSpan.Zero)
        {
            try { await Task.Delay(firstDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "InvitationExpiryWorker pass failed; will retry next tick.");
            }

            try { await Task.Delay(schedule.Period, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Single pass; public for test access. Returns the count of expired invitations.</summary>
    public async Task<int> RunPassAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        await using var lockHandle = await PostgresAdvisoryLock.TryAcquireAsync(
            db, PostgresAdvisoryLock.Keys.InvitationExpiryWorker, ct);
        if (!lockHandle.Acquired)
        {
            logger.LogDebug("InvitationExpiryWorker — lock held by peer instance; no-op pass.");
            return 0;
        }

        var nowUtc = clock.GetUtcNow();
        var pendingToken = CompanyInvitationState.Pending.ToToken();

        var dueIds = await db.CompanyInvitations
            .AsNoTracking()
            .Where(i => i.State == pendingToken && i.ExpiresAt <= nowUtc)
            .Select(i => i.Id)
            .ToListAsync(ct);

        var expiredCount = 0;
        foreach (var invitationId in dueIds)
        {
            try
            {
                if (await ExpireOneAsync(scope.ServiceProvider, invitationId, nowUtc, ct))
                {
                    expiredCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to expire invitation {InvitationId}; will retry next tick.", invitationId);
            }
        }

        if (expiredCount > 0)
        {
            logger.LogInformation("InvitationExpiryWorker expired {Count} invitation(s).", expiredCount);
        }
        return expiredCount;
    }

    private async Task<bool> ExpireOneAsync(
        IServiceProvider sp,
        Guid invitationId,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        await using var rowScope = sp.CreateAsyncScope();
        var db = rowScope.ServiceProvider.GetRequiredService<B2BDbContext>();
        var auditPublisher = rowScope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var domainPublisher = rowScope.ServiceProvider.GetRequiredService<IPublisher>();

        var row = await db.CompanyInvitations.SingleOrDefaultAsync(i => i.Id == invitationId, ct);
        if (row is null)
        {
            return false;
        }
        if (!CompanyInvitationStateExtensions.TryParseToken(row.State, out var current)
            || current != CompanyInvitationState.Pending
            || row.ExpiresAt > nowUtc)
        {
            return false;
        }

        var priorState = row.State;
        row.State = CompanyInvitationState.Expired.ToToken();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }

        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: SystemActorId,
                ActorRole: "system",
                Action: "company_invitation.state_changed",
                EntityType: "company_invitation",
                EntityId: invitationId,
                BeforeState: new { state = priorState },
                AfterState: new { state = CompanyInvitationState.Expired.ToToken() },
                Reason: "invitation_expired"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invitation {InvitationId} expired but audit publish failed.", invitationId);
        }

        try
        {
            await domainPublisher.Publish(new CompanyInvitationExpired(
                InvitationId: invitationId,
                CompanyId: row.CompanyId,
                InvitedEmail: row.InvitedEmail,
                ActorId: SystemActorId,
                PerformedAt: nowUtc), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invitation {InvitationId} expired but domain publish failed.", invitationId);
        }

        return true;
    }
}
