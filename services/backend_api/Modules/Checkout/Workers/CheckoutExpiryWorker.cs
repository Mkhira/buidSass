using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Checkout.Workers;

/// <summary>
/// Expires idle checkout sessions every 1 min (FR-025 / SC-006). Sessions at a pre-submit
/// state whose `expires_at` has elapsed transition to `expired` and their cart line
/// reservations are released so stock returns to available-to-sell.
///
/// `submitted` sessions are NOT expirable — the submit handler owns their outcome.
/// </summary>
public sealed class CheckoutExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CheckoutOptions> options,
    ILogger<CheckoutExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "checkout.expiry-worker.cycle-failed");
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.ExpiryWorkerIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> TickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var cartInventoryOrchestrator = scope.ServiceProvider.GetRequiredService<CartInventoryOrchestrator>();
        var cartDb = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Cart.Persistence.CartDbContext>();

        var nowUtc = DateTimeOffset.UtcNow;
        var expirable = new[] {
            CheckoutStates.Init, CheckoutStates.Addressed,
            CheckoutStates.ShippingSelected, CheckoutStates.PaymentSelected,
        };

        var candidates = await db.Sessions
            .Where(s => expirable.Contains(s.State) && s.ExpiresAt < nowUtc)
            .OrderBy(s => s.ExpiresAt)
            .Take(200)
            .ToListAsync(ct);
        if (candidates.Count == 0) return 0;

        foreach (var session in candidates)
        {
            CheckoutStates.TryTransition(session, CheckoutStates.Expired, nowUtc);
            session.FailureReasonCode = "checkout.session.expired";
            // Release the cart's reservations so stock returns to sale.
            var reservations = await cartDb.CartLines.AsNoTracking()
                .Where(l => l.CartId == session.CartId && l.ReservationId != null)
                .Select(l => l.ReservationId!.Value)
                .ToListAsync(ct);
            foreach (var rid in reservations)
            {
                try
                {
                    await cartInventoryOrchestrator.TryReleaseAsync(
                        inventoryDb, rid, CartSystemActors.Anonymous, "checkout.session.expired", ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "checkout.expiry.release_failed sessionId={SessionId} reservationId={Rid}", session.Id, rid);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("checkout.expiry-worker.expired count={Count}", candidates.Count);
        return candidates.Count;
    }
}
