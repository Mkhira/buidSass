using BackendApi.Modules.Payments.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Payments.Workers;

/// <summary>
/// Sweeps expired <c>idempotency_keys</c> cache rows (24h post-terminal-state
/// TTL per data-model.md §idempotency_keys). The Payments table retains
/// its own active unique index; this table is purely a fast-path cache.
/// </summary>
public sealed class IdempotencyKeySweeperWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<IdempotencyKeySweeperWorker> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "IdempotencyKeySweeperWorker iteration failed");
            }
            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var now = clock.GetUtcNow();
        var expired = await db.IdempotencyKeys
            .Where(k => k.ExpiresAt != null && k.ExpiresAt < now)
            .Take(500)
            .ToListAsync(ct);
        if (expired.Count == 0) return;
        db.IdempotencyKeys.RemoveRange(expired);
        await db.SaveChangesAsync(ct);
    }
}
