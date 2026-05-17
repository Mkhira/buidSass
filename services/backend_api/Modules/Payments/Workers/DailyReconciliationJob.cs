using System.Text.Json;
using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Payments.Domain;
using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Providers;
using BackendApi.Modules.Payments.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Payments.Workers;

/// <summary>
/// T052 — daily reconciliation orchestrator. Runs once per day at 03:00 KSA
/// (00:00 UTC). For each active provider, fetches yesterday's settlement
/// ledger (via API or CSV per <see cref="IPaymentProvider.SupportsLedgerApi"/>),
/// invokes <see cref="ReconciliationMatcher"/>, persists matched + exception
/// rows, and emits audit events at start + completion.
/// </summary>
public sealed class DailyReconciliationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<DailyReconciliationJob> logger,
    TimeProvider clock) : BackgroundService
{
    // Run check every 10 minutes; only kick off if we're inside the daily window AND
    // we haven't already started a run for the target date.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "DailyReconciliationJob tick failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    /// <summary>Single tick: kicks off a reconciliation if it's the daily window and a run hasn't started yet.</summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow();
        // 03:00 KSA = 00:00 UTC. Window: 00:00–00:15 UTC.
        if (nowUtc.Hour != 0 || nowUtc.Minute >= 15) return;

        var date = DateOnly.FromDateTime(nowUtc.UtcDateTime).AddDays(-1);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        // Only a SUCCESSFULLY COMPLETED run blocks a retry. A previous failed
        // attempt (one provider's ledger fetcher down, etc.) MUST stay
        // retryable so a single transient outage cannot permanently skip a
        // day's reconciliation.
        var alreadyCompleted = await db.ReconciliationRuns.AnyAsync(
            r => r.DateRangeStart == date
                && r.DateRangeEnd == date
                && r.Status == PaymentsConstants.ReconciliationRunStatuses.Completed, ct);
        if (alreadyCompleted) return;

        await RunOnceAsync(date, ct);
    }

    /// <summary>Executes one reconciliation pass for the given date — exposed for test + admin trigger.</summary>
    public async Task RunOnceAsync(DateOnly date, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var providers = scope.ServiceProvider.GetRequiredService<ProviderRegistry>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventPublisher>();
        var matcher = scope.ServiceProvider.GetRequiredService<ReconciliationMatcher>();

        var run = new ReconciliationRun
        {
            Id = Guid.NewGuid(),
            StartedAt = clock.GetUtcNow(),
            DateRangeStart = date,
            DateRangeEnd = date,
            Status = PaymentsConstants.ReconciliationRunStatuses.Running,
            ProvidersProcessedJson = JsonSerializer.Serialize(providers.All.Select(p => p.ProviderId).ToArray()),
            CreatedAt = clock.GetUtcNow(),
        };
        db.ReconciliationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: Guid.Empty, ActorRole: "system",
            Action: PaymentsConstants.AuditActions.ReconciliationRunStarted,
            EntityType: "ReconciliationRun", EntityId: run.Id,
            BeforeState: null, AfterState: new { date }, Reason: null), ct);

        int totalLedgerRows = 0, totalMatched = 0, totalExceptions = 0;
        int providerErrorCount = 0;
        foreach (var provider in providers.All)
        {
            try
            {
                var ledger = await provider.FetchSettlementLedgerAsync(date, ct);
                var (matched, exceptions) = await matcher.MatchAsync(provider, ledger, run.Id, ct);
                totalLedgerRows += ledger.Rows.Count;
                totalMatched += matched;
                totalExceptions += exceptions;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ledger fetch failed for {Provider}", provider.ProviderId);
                providerErrorCount++;
            }
        }

        var internalCount = await db.Payments
            .CountAsync(p => p.DeletedAt == null
                && p.State == PaymentsConstants.PaymentStates.Captured
                && p.CapturedAt != null
                && DateOnly.FromDateTime(p.CapturedAt!.Value.UtcDateTime) == date, ct);

        run.InternalPaymentsCount = internalCount;
        run.ProviderLedgerRowsCount = totalLedgerRows;
        run.MatchedCount = totalMatched;
        run.ExceptionsCount = totalExceptions;
        run.CompletedAt = clock.GetUtcNow();
        // Partial-success policy: any provider error → status=failed so the
        // run does not block tomorrow's retry attempt (see alreadyCompleted
        // gate above). Successful matches + exceptions are still persisted
        // for the operator queue.
        run.Status = providerErrorCount == 0
            ? PaymentsConstants.ReconciliationRunStatuses.Completed
            : PaymentsConstants.ReconciliationRunStatuses.Failed;
        await db.SaveChangesAsync(ct);

        await audit.PublishAsync(new AuditEvent(
            ActorId: Guid.Empty, ActorRole: "system",
            Action: PaymentsConstants.AuditActions.ReconciliationRunCompleted,
            EntityType: "ReconciliationRun", EntityId: run.Id,
            BeforeState: null,
            AfterState: new { internalCount, totalLedgerRows, totalMatched, totalExceptions },
            Reason: null), ct);
    }
}
