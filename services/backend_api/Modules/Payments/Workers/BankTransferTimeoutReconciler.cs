using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Payments.Workers;

/// <summary>
/// T037 — auto-expires <c>pending_bank_transfer</c> payments past the 72h
/// window (BR-10 / AC-20). Each expiry emits <c>payment.expired</c> and the
/// downstream order is moved to cancelled via spec 011's flow.
/// </summary>
public sealed class BankTransferTimeoutReconciler(
    IServiceScopeFactory scopeFactory,
    ILogger<BankTransferTimeoutReconciler> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "BankTransferTimeoutReconciler iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var transitions = scope.ServiceProvider.GetRequiredService<PaymentTransitionService>();

        var cutoff = clock.GetUtcNow() - PaymentsConstants.Windows.BankTransferTtl;
        var stale = await db.Payments
            .Where(p => p.DeletedAt == null
                && p.State == PaymentsConstants.PaymentStates.PendingBankTransfer
                && p.CreatedAt < cutoff)
            .Take(50)
            .ToListAsync(ct);

        foreach (var p in stale)
        {
            await transitions.MarkExpiredAsync(db, p, PaymentsConstants.ExpiredReasons.BankTransferTimeout, ct);
        }
    }
}
