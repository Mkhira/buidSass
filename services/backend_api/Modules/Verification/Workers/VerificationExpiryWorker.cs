using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Verification.Eligibility;
using BackendApi.Modules.Verification.Entities;
using BackendApi.Modules.Verification.Persistence;
using BackendApi.Modules.Verification.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Verification.Workers;

/// <summary>
/// Spec 020 task T093 / research §R12. Daily worker that transitions every
/// <see cref="VerificationState.Approved"/> row whose
/// <c>expires_at &lt;= now</c> to <see cref="VerificationState.Expired"/>.
///
/// <para>Per pass:</para>
/// <list type="number">
///   <item>Take a Postgres advisory lock; another instance holding the lock means no-op cleanly.</item>
///   <item>Find every approved row whose <c>expires_at &lt;= now</c>.</item>
///   <item>For each, in its own Tx:
///     <list type="bullet">
///       <item>Flip state → <see cref="VerificationState.Expired"/>;</item>
///       <item>Append the state-transition ledger row;</item>
///       <item>Set <c>purge_after = now + market.retention_months</c> on every
///             non-purged document (T096 retention wiring);</item>
///       <item>Rebuild the eligibility cache;</item>
///       <item>Publish audit event + <see cref="VerificationDomainEvents.VerificationExpired"/>.</item>
///     </list>
///   </item>
/// </list>
///
/// <para>Idempotent on re-run — the WHERE clause excludes already-expired rows.</para>
/// </summary>
public sealed class VerificationExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<VerificationWorkerOptions> options,
    TimeProvider clock,
    ILogger<VerificationExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = options.Value.Expiry.Period;
        // First-tick delay aligns the worker to the configured StartUtc (or runs
        // immediately when StartUtc is zero / behind the wall clock).
        var firstDelay = options.Value.Expiry.FirstDelay(clock.GetUtcNow());
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
                logger.LogWarning(ex, "VerificationExpiryWorker pass failed; will retry next tick.");
            }

            try { await Task.Delay(period, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Single pass; public for test access. Exposes the count of expired rows.</summary>
    public async Task<int> RunPassAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VerificationDbContext>();

        await using var lockHandle = await PostgresAdvisoryLock.TryAcquireAsync(
            db, PostgresAdvisoryLock.Keys.ExpiryWorker, ct);
        if (!lockHandle.Acquired)
        {
            logger.LogDebug("VerificationExpiryWorker — lock held by peer instance; no-op pass.");
            return 0;
        }

        var auditPublisher = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var domainPublisher = scope.ServiceProvider.GetRequiredService<IVerificationDomainEventPublisher>();
        var invalidator = scope.ServiceProvider.GetRequiredService<EligibilityCacheInvalidator>();

        var nowUtc = clock.GetUtcNow();

        // Find every approved row whose expiry has passed. The set is bounded by the
        // ExpiresAt index (filter "State = 'approved'") so this is a cheap scan.
        var dueIds = await db.Verifications
            .AsNoTracking()
            .Where(v => v.State == VerificationState.Approved
                     && v.ExpiresAt != null
                     && v.ExpiresAt <= nowUtc)
            .Select(v => v.Id)
            .ToListAsync(ct);

        var expiredCount = 0;
        foreach (var verificationId in dueIds)
        {
            try
            {
                if (await ExpireOneAsync(scope.ServiceProvider, verificationId, nowUtc, auditPublisher, domainPublisher, invalidator, ct))
                {
                    expiredCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to expire verification {VerificationId}; will retry next tick.", verificationId);
            }
        }

        if (expiredCount > 0)
        {
            logger.LogInformation("VerificationExpiryWorker expired {Count} verification(s).", expiredCount);
        }
        return expiredCount;
    }

    private async Task<bool> ExpireOneAsync(
        IServiceProvider sp,
        Guid verificationId,
        DateTimeOffset nowUtc,
        IAuditEventPublisher auditPublisher,
        IVerificationDomainEventPublisher domainPublisher,
        EligibilityCacheInvalidator invalidator,
        CancellationToken ct)
    {
        // Fresh DbContext per row keeps change-tracker lean and isolates failures.
        await using var rowScope = sp.CreateAsyncScope();
        var db = rowScope.ServiceProvider.GetRequiredService<VerificationDbContext>();

        var row = await db.Verifications.SingleOrDefaultAsync(v => v.Id == verificationId, ct);
        if (row is null || row.State != VerificationState.Approved
            || row.ExpiresAt is null || row.ExpiresAt > nowUtc)
        {
            // Idempotent guard — skip rows that another instance / tick already expired.
            return false;
        }

        var schema = await db.MarketSchemas
            .AsNoTracking()
            .Where(s => s.MarketCode == row.MarketCode && s.Version == row.SchemaVersion)
            .SingleOrDefaultAsync(ct);

        var priorState = row.State;
        row.State = VerificationState.Expired;
        row.UpdatedAt = nowUtc;

        // T096 retention wiring: stamp purge_after on every non-purged document.
        var retentionMonths = schema?.RetentionMonths ?? 24;
        var purgeAfter = nowUtc.AddMonths(retentionMonths);
        var documents = await db.Documents
            .Where(d => d.VerificationId == verificationId && d.PurgedAt == null && d.PurgeAfter == null)
            .ToListAsync(ct);
        foreach (var doc in documents)
        {
            doc.PurgeAfter = purgeAfter;
        }

        db.StateTransitions.Add(new VerificationStateTransition
        {
            Id = Guid.NewGuid(),
            VerificationId = verificationId,
            MarketCode = row.MarketCode,
            PriorState = priorState.ToWireValue(),
            NewState = VerificationState.Expired.ToWireValue(),
            ActorKind = VerificationActorKind.System.ToWireValue(),
            ActorId = null,
            Reason = "verification_expired",
            MetadataJson = "{}",
            OccurredAt = nowUtc,
        });

        await invalidator.RebuildAsync(row.CustomerId, row.MarketCode, db, ct);
        await db.SaveChangesAsync(ct);

        // Best-effort publishes — don't roll back the expiry on subscriber failure.
        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: Guid.Empty,
                ActorRole: "system",
                Action: "verification.state_changed",
                EntityType: "verification",
                EntityId: verificationId,
                BeforeState: new { state = priorState.ToWireValue() },
                AfterState: new { state = VerificationState.Expired.ToWireValue() },
                Reason: "verification_expired"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Verification {VerificationId} expired but audit publish failed.", verificationId);
        }

        try
        {
            await domainPublisher.PublishAsync(new VerificationDomainEvents.VerificationExpired(
                VerificationId: verificationId,
                CustomerId: row.CustomerId,
                MarketCode: row.MarketCode,
                LocaleHint: "en"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Verification {VerificationId} expired but domain publish failed.", verificationId);
        }

        return true;
    }
}
