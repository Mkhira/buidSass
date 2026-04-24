using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Cart.Workers;

/// <summary>
/// Moves archived carts past ArchivedCartRetentionDays to `purged` status. Archived carts
/// SHOULD already have their reservations released (archive paths do this on transition), but
/// as defence in depth the reaper releases any reservation still attached before hard-deleting
/// the dependent rows. (C1)
/// </summary>
public sealed class ArchivedCartReaperWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CartOptions> options,
    ILogger<ArchivedCartReaperWorker> logger) : BackgroundService
{
    private static readonly Guid WorkerActorId = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc3");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "cart.archived-reaper-worker.cycle-failed");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.ArchivedReaperWorkerIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var inventoryOrchestrator = scope.ServiceProvider.GetRequiredService<CartInventoryOrchestrator>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.Value.ArchivedCartRetentionDays);
        var stale = await db.Carts
            .Where(c => c.Status == "archived" && c.ArchivedAt != null && c.ArchivedAt < cutoff)
            .Take(500)
            .ToListAsync(ct);

        if (stale.Count == 0) return 0;

        var ids = stale.Select(c => c.Id).ToArray();
        var reservationIds = await db.CartLines
            .AsNoTracking()
            .Where(l => ids.Contains(l.CartId) && l.ReservationId != null)
            .Select(l => l.ReservationId!.Value)
            .ToListAsync(ct);

        foreach (var reservationId in reservationIds)
        {
            await inventoryOrchestrator.TryReleaseAsync(
                inventoryDb, reservationId, WorkerActorId, "cart.archived_reaper_purge", ct);
        }

        await db.CartLines.Where(l => ids.Contains(l.CartId)).ExecuteDeleteAsync(ct);
        await db.CartSavedItems.Where(s => ids.Contains(s.CartId)).ExecuteDeleteAsync(ct);
        await db.CartB2BMetadata.Where(b => ids.Contains(b.CartId)).ExecuteDeleteAsync(ct);
        await db.Set<Entities.CartAbandonedEmission>().Where(e => ids.Contains(e.CartId)).ExecuteDeleteAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var c in stale)
        {
            c.Status = "purged";
            c.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "cart.archived-reaper-worker.purged count={Count} reservationsReleased={ResCount}",
            stale.Count, reservationIds.Count);
        return stale.Count;
    }
}
