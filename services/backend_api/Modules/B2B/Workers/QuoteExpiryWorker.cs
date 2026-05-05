using BackendApi.Modules.AuditLog;
using BackendApi.Modules.B2B.Entities;
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
/// Spec 021 task T135. Daily worker that transitions every non-terminal quote
/// (<c>requested</c>, <c>drafted</c>, <c>revised</c>, <c>pending-approver</c>) whose
/// <c>expires_at &lt;= now</c> to <c>expired</c>. Publishes <see cref="QuoteExpired"/>,
/// audits the transition, and writes a <see cref="QuoteStateTransition"/> ledger row.
///
/// <para>Per pass:</para>
/// <list type="number">
///   <item>Take a Postgres advisory lock; another instance holding the lock means no-op cleanly.</item>
///   <item>Find every non-terminal quote with <c>ExpiresAt &lt;= now</c>.</item>
///   <item>For each, in its own scope:
///     <list type="bullet">
///       <item>Transition state → <c>expired</c>;</item>
///       <item>Append the state-transition ledger row;</item>
///       <item>Publish audit event + <see cref="QuoteExpired"/> domain event.</item>
///     </list>
///   </item>
/// </list>
///
/// <para>Idempotent on re-run — the WHERE clause excludes already-expired rows.</para>
/// </summary>
public sealed class QuoteExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<B2BWorkerOptions> options,
    TimeProvider clock,
    ILogger<QuoteExpiryWorker> logger) : BackgroundService
{
    private static readonly Guid SystemActorId = new("00000000-0000-0000-0000-00000B2B0210");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = options.Value.Expiry;
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
                logger.LogWarning(ex, "QuoteExpiryWorker pass failed; will retry next tick.");
            }

            try { await Task.Delay(schedule.Period, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Single pass; public for test access. Returns the count of expired rows.</summary>
    public async Task<int> RunPassAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        await using var lockHandle = await PostgresAdvisoryLock.TryAcquireAsync(
            db, PostgresAdvisoryLock.Keys.QuoteExpiryWorker, ct);
        if (!lockHandle.Acquired)
        {
            logger.LogDebug("QuoteExpiryWorker — lock held by peer instance; no-op pass.");
            return 0;
        }

        var nowUtc = clock.GetUtcNow();

        // Bounded by index on (state, expires_at). Non-terminal states only.
        var dueIds = await db.Quotes
            .AsNoTracking()
            .Where(q => (q.State == "requested"
                      || q.State == "drafted"
                      || q.State == "revised"
                      || q.State == "pending-approver")
                     && q.ExpiresAt != null
                     && q.ExpiresAt <= nowUtc)
            .Select(q => q.Id)
            .ToListAsync(ct);

        var expiredCount = 0;
        foreach (var quoteId in dueIds)
        {
            try
            {
                if (await ExpireOneAsync(scope.ServiceProvider, quoteId, nowUtc, ct))
                {
                    expiredCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to expire quote {QuoteId}; will retry next tick.", quoteId);
            }
        }

        if (expiredCount > 0)
        {
            logger.LogInformation("QuoteExpiryWorker expired {Count} quote(s).", expiredCount);
        }
        return expiredCount;
    }

    private async Task<bool> ExpireOneAsync(
        IServiceProvider sp,
        Guid quoteId,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        await using var rowScope = sp.CreateAsyncScope();
        var db = rowScope.ServiceProvider.GetRequiredService<B2BDbContext>();
        var auditPublisher = rowScope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var domainPublisher = rowScope.ServiceProvider.GetRequiredService<IPublisher>();

        var quote = await db.Quotes.SingleOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote is null)
        {
            return false;
        }

        // Idempotent guard — skip rows that another instance / tick already expired or moved terminal.
        if (!QuoteStateExtensions.TryParseToken(quote.State, out var currentState) || currentState.IsTerminal())
        {
            return false;
        }
        if (quote.ExpiresAt is null || quote.ExpiresAt > nowUtc)
        {
            return false;
        }

        var priorState = quote.State;
        quote.State = QuoteState.Expired.ToToken();
        quote.TerminalAt = nowUtc;
        quote.TerminalReason = QuoteReasonCode.QuoteExpired.ToToken();

        db.QuoteStateTransitions.Add(new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quoteId,
            MarketCode = quote.MarketCode,
            PriorState = priorState,
            NewState = QuoteState.Expired.ToToken(),
            ActorKind = QuoteActorKind.System.ToToken(),
            ActorId = null,
            ReasonJson = null,
            MetadataJson = "{\"reason\":\"" + QuoteReasonCode.QuoteExpired.ToToken() + "\"}",
            OccurredAt = nowUtc,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another instance / handler raced us. Treat as success (idempotent).
            return false;
        }

        // Best-effort publishes — never roll back the expiry on subscriber failure.
        try
        {
            await auditPublisher.PublishAsync(new AuditEvent(
                ActorId: SystemActorId,
                ActorRole: "system",
                Action: "quote.state_changed",
                EntityType: "quote",
                EntityId: quoteId,
                BeforeState: new { state = priorState },
                AfterState: new { state = QuoteState.Expired.ToToken() },
                Reason: QuoteReasonCode.QuoteExpired.ToToken()), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Quote {QuoteId} expired but audit publish failed.", quoteId);
        }

        try
        {
            await domainPublisher.Publish(new QuoteExpired(
                QuoteId: quoteId,
                CustomerId: quote.CustomerId,
                CompanyId: quote.CompanyId,
                MarketCode: quote.MarketCode,
                LocaleHint: "en"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Quote {QuoteId} expired but domain publish failed.", quoteId);
        }

        return true;
    }
}
