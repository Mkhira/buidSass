using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Cart.Workers;

/// <summary>
/// Purges guest carts (AccountId IS NULL) untouched for GuestCartPurgeDays. Every cart line's
/// attached inventory reservation is explicitly released BEFORE delete so stock.Reserved doesn't
/// drift. Auth-owned carts are never touched here — their lifecycle flows through the archive
/// reaper. (SC-005 / C1)
/// </summary>
public sealed class GuestCartCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CartOptions> options,
    ILogger<GuestCartCleanupWorker> logger) : BackgroundService
{
    private static readonly Guid WorkerActorId = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc2");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "cart.guest-cleanup-worker.cycle-failed");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.GuestCleanupWorkerIntervalSeconds)), stoppingToken);
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

        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.Value.GuestCartPurgeDays);
        var stale = await db.Carts
            .AsNoTracking()
            .Where(c => c.AccountId == null && c.LastTouchedAt < cutoff)
            .Take(500)
            .ToListAsync(ct);

        if (stale.Count == 0) return 0;

        var ids = stale.Select(c => c.Id).ToArray();
        // Release all attached reservations first so inventory stays consistent.
        var reservationIds = await db.CartLines
            .AsNoTracking()
            .Where(l => ids.Contains(l.CartId) && l.ReservationId != null)
            .Select(l => l.ReservationId!.Value)
            .ToListAsync(ct);

        foreach (var reservationId in reservationIds)
        {
            await inventoryOrchestrator.TryReleaseAsync(
                inventoryDb, reservationId, WorkerActorId, "cart.guest_cleanup_purge", ct);
        }

        await db.CartLines.Where(l => ids.Contains(l.CartId)).ExecuteDeleteAsync(ct);
        await db.CartSavedItems.Where(s => ids.Contains(s.CartId)).ExecuteDeleteAsync(ct);
        await db.CartB2BMetadata.Where(b => ids.Contains(b.CartId)).ExecuteDeleteAsync(ct);
        await db.Set<Entities.CartAbandonedEmission>().Where(e => ids.Contains(e.CartId)).ExecuteDeleteAsync(ct);
        await db.Carts.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "cart.guest-cleanup-worker.purged count={Count} reservationsReleased={ResCount}",
            stale.Count, reservationIds.Count);
        return stale.Count;
    }
}
