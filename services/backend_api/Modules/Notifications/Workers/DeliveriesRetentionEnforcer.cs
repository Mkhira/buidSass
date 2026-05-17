using BackendApi.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Notifications.Workers;

/// <summary>
/// T060a — 90-day retention enforcement for <c>notifications.deliveries</c>.
/// Audit-log entries are owned by spec 003 and retain ≥365 days — this worker
/// MUST NOT touch them. Daily tick; deletes deliveries older than 90 days in
/// bounded batches to avoid long-running transactions on the hot path.
/// Verifies User Story 7 acceptance scenario 2.
/// </summary>
public sealed class DeliveriesRetentionEnforcer : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DeliveriesRetentionEnforcer> _logger;
    private readonly TimeProvider _clock;
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetainFor = TimeSpan.FromDays(90);
    private const int BatchSize = 500;

    public DeliveriesRetentionEnforcer(
        IServiceScopeFactory scopes,
        ILogger<DeliveriesRetentionEnforcer> logger,
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
                _logger.LogError(ex, "DeliveriesRetentionEnforcer iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var cutoff = _clock.GetUtcNow() - RetainFor;

        int totalDeleted = 0;
        while (!ct.IsCancellationRequested)
        {
            var batch = await db.Deliveries
                .Where(d => d.CreatedAt < cutoff)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;
            db.Deliveries.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            totalDeleted += batch.Count;
        }
        if (totalDeleted > 0)
            _logger.LogInformation("DeliveriesRetentionEnforcer deleted {Count} delivery rows older than 90d", totalDeleted);
    }
}
