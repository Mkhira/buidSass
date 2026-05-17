using BackendApi.Modules.Payments.Persistence;
using BackendApi.Modules.Payments.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Payments.Workers;

/// <summary>
/// T060 — Provider health tracker. Reads payments' recent state, computes per-
/// provider failure rate over the configured <c>FailoverWindowMinutes</c>, and
/// emits a <c>provider.degraded</c> audit signal when the rate crosses the
/// configured threshold. Auto-failover stays OFF by default (BR-13).
/// </summary>
public sealed class ProviderHealthMonitor(
    IServiceScopeFactory scopeFactory,
    ILogger<ProviderHealthMonitor> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ProviderHealthMonitor iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var routings = await db.ProviderRoutings.ToListAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var route in routings)
        {
            var windowStart = now - TimeSpan.FromMinutes(route.FailoverWindowMinutes);
            // SQL aggregate — avoid materializing every recent payment state
            // into memory. Two scalar queries against the
            // (provider_id, created_at) index keep this tick fast even at
            // high traffic. Per-route SCAN avoided.
            var totalRecent = await db.Payments
                .Where(p => p.DeletedAt == null
                    && p.ProviderId == route.PrimaryProviderId
                    && p.CreatedAt >= windowStart)
                .CountAsync(ct);
            if (totalRecent == 0) continue;
            var failedRecent = await db.Payments
                .Where(p => p.DeletedAt == null
                    && p.ProviderId == route.PrimaryProviderId
                    && p.CreatedAt >= windowStart
                    && p.State == PaymentsConstants.PaymentStates.Failed)
                .CountAsync(ct);
            var pct = failedRecent * 100 / totalRecent;
            if (pct >= route.FailoverThresholdPct)
            {
                logger.LogWarning(
                    "Provider {Provider} on ({Market},{Method}) shows {Pct}% failures over {Window}m — degraded signal raised",
                    route.PrimaryProviderId, route.MarketCode, route.Method, pct, route.FailoverWindowMinutes);
                // Audit publish kept in Phase 8 wiring; this tick only logs so the
                // worker is safe to start before the audit registration in tests.
            }
        }
    }
}
