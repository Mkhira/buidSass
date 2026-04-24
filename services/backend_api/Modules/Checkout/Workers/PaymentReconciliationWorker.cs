using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Checkout.Workers;

/// <summary>
/// Background worker that pulls `pending_webhook` PaymentAttempts older than a threshold and
/// flips them to a terminal state if the provider has missed its webhook SLA. Today this is a
/// thin stub — spec 010 ships the scaffold so operators can see pending attempts; real
/// reconciliation (bank transfer → finance admin handoff) lands with spec 011's order admin
/// surface which owns the payment_state ledger.
/// </summary>
public sealed class PaymentReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "checkout.reconcile-worker.cycle-failed");
            }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-60);
        var stale = await db.PaymentAttempts
            .Where(a => a.State == PaymentAttemptStates.PendingWebhook && a.UpdatedAt < cutoff)
            .Take(100)
            .ToListAsync(ct);
        if (stale.Count == 0) return 0;
        foreach (var attempt in stale)
        {
            logger.LogWarning(
                "checkout.reconcile.pending_overdue attemptId={AttemptId} sessionId={SessionId} since={Since} — flagged for operator review.",
                attempt.Id, attempt.SessionId, attempt.UpdatedAt);
        }
        return stale.Count;
    }
}
