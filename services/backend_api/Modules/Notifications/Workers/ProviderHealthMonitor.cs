using BackendApi.Modules.Notifications.Persistence;
using BackendApi.Modules.Notifications.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Notifications.Workers;

/// <summary>
/// T047 — 5-minute sliding-window failure-rate calculator per
/// (provider_id, market_code, channel). When the failure rate over the
/// configured window crosses the threshold AND
/// <see cref="Domain.ProviderRouting.AutoFailoverEnabled"/> is true, the
/// monitor swaps Primary ↔ Backup on the matching ProviderRouting row.
/// Always emits a <c>provider.degraded</c> log when the threshold is
/// breached, regardless of auto-failover setting, so operators see the
/// signal even when failover is manual.
/// </summary>
public sealed class ProviderHealthMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ProviderHealthMonitor> _logger;
    private readonly TimeProvider _clock;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    public ProviderHealthMonitor(
        IServiceScopeFactory scopes,
        ILogger<ProviderHealthMonitor> logger,
        TimeProvider clock)
    {
        _scopes = scopes;
        _logger = logger;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ProviderHealthMonitor iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var now = _clock.GetUtcNow();

        var routings = await db.ProviderRoutings.ToListAsync(ct);
        foreach (var routing in routings)
        {
            var since = now - TimeSpan.FromMinutes(routing.FailoverWindowMinutes);
            var batch = await db.Notifications.AsNoTracking()
                .Where(n => n.ProviderId == routing.PrimaryProviderId
                    && n.MarketCode == routing.MarketCode
                    && n.Channel == routing.Channel
                    && n.UpdatedAt >= since
                    && (n.State == NotificationsConstants.NotificationStates.Delivered
                        || n.State == NotificationsConstants.NotificationStates.Failed
                        || n.State == NotificationsConstants.NotificationStates.DeadLetter))
                .Select(n => new { n.State })
                .ToListAsync(ct);

            if (batch.Count == 0) continue;

            var failed = batch.Count(b => b.State != NotificationsConstants.NotificationStates.Delivered);
            var failureRate = (failed * 100) / batch.Count;
            if (failureRate < routing.FailoverThresholdPct) continue;

            _logger.LogWarning(
                "provider.degraded provider={Provider} market={Market} channel={Channel} window_min={Window} rate_pct={Rate}",
                routing.PrimaryProviderId, routing.MarketCode, routing.Channel, routing.FailoverWindowMinutes, failureRate);

            if (!routing.AutoFailoverEnabled || string.IsNullOrEmpty(routing.BackupProviderId)) continue;

            (routing.PrimaryProviderId, routing.BackupProviderId) = (routing.BackupProviderId!, routing.PrimaryProviderId);
            routing.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }
}
