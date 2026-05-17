using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using BackendApi.Modules.Payments.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Payments.Workers;

/// <summary>
/// T033 — ages out payments stuck in <c>pending_external_redirect</c> (BNPL +
/// some Apple Pay) past the per-flow TTL, plus <c>pending_authorization</c>
/// rows that abandoned hosted-fields (30 min default) and <c>capture_failed</c>
/// rows past the 24h auto-void window.
/// </summary>
public sealed class PendingExpirationReconciler(
    IServiceScopeFactory scopeFactory,
    ILogger<PendingExpirationReconciler> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PendingExpirationReconciler iteration failed");
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

        var now = clock.GetUtcNow();
        var redirectCutoff = now - PaymentsConstants.Windows.ExternalRedirectTtl;
        var authCutoff = now - PaymentsConstants.Windows.AuthorizationPendingTtl;
        var captureCutoff = now - PaymentsConstants.Windows.CaptureFailedAutoVoid;

        var stale = await db.Payments
            .Where(p => p.DeletedAt == null && (
                (p.State == PaymentsConstants.PaymentStates.PendingExternalRedirect && p.CreatedAt < redirectCutoff) ||
                (p.State == PaymentsConstants.PaymentStates.PendingAuthorization && p.CreatedAt < authCutoff) ||
                (p.State == PaymentsConstants.PaymentStates.CaptureFailed && p.UpdatedAt < captureCutoff)))
            .Take(50)
            .ToListAsync(ct);

        foreach (var p in stale)
        {
            var reason = p.State switch
            {
                PaymentsConstants.PaymentStates.PendingExternalRedirect => PaymentsConstants.ExpiredReasons.RedirectTimeout,
                PaymentsConstants.PaymentStates.PendingAuthorization => PaymentsConstants.ExpiredReasons.AuthorizationStale,
                PaymentsConstants.PaymentStates.CaptureFailed => PaymentsConstants.ExpiredReasons.CapturedNotResolved,
                _ => PaymentsConstants.ExpiredReasons.AuthorizationStale,
            };
            await transitions.MarkExpiredAsync(db, p, reason, ct);
        }
    }
}
