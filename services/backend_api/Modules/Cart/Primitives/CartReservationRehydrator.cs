using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cart.Primitives;

/// <summary>
/// Spec 009 edge case 4: "Reservation lost between reads (TTL expired) → on next read, cart
/// attempts re-reservation; if insufficient, line is flagged stockChanged=true for UI."
///
/// This primitive runs at GetCart time. It:
///   1. Batches the cart's current reservation ids and queries inventory for their live state.
///   2. For every line whose reservation is missing / not-active / expired, calls the inventory
///      orchestrator to re-reserve the requested qty.
///   3. Writes the new reservation id (or null + StockChanged=true when stock can't cover) back
///      onto the line in-place, then persists once at the end.
/// </summary>
public sealed class CartReservationRehydrator(
    CartInventoryOrchestrator orchestrator,
    ILogger<CartReservationRehydrator> logger)
{
    public async Task RehydrateAsync(
        CartDbContext cartDb,
        InventoryDbContext inventoryDb,
        CatalogDbContext catalogDb,
        Entities.Cart cart,
        IReadOnlyList<CartLine> trackedLines,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (trackedLines.Count == 0) return;

        var reservationIds = trackedLines
            .Where(l => l.ReservationId.HasValue)
            .Select(l => l.ReservationId!.Value)
            .Distinct()
            .ToArray();

        var liveReservationsById = reservationIds.Length == 0
            ? new Dictionary<Guid, (string Status, DateTimeOffset ExpiresAt)>()
            : await inventoryDb.InventoryReservations.AsNoTracking()
                .Where(r => reservationIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Status, r.ExpiresAt })
                .ToDictionaryAsync(x => x.Id, x => (x.Status, x.ExpiresAt), ct);

        var changed = false;
        foreach (var line in trackedLines)
        {
            if (line.Unavailable) continue;

            var needsRehydrate = !line.ReservationId.HasValue;
            if (!needsRehydrate && line.ReservationId is { } rid)
            {
                if (!liveReservationsById.TryGetValue(rid, out var state))
                {
                    // Inventory row missing — TTL reaper or manual cleanup ran.
                    needsRehydrate = true;
                }
                else if (!string.Equals(state.Status, "active", StringComparison.OrdinalIgnoreCase)
                         || state.ExpiresAt <= nowUtc)
                {
                    needsRehydrate = true;
                }
            }
            if (!needsRehydrate) continue;

            var result = await orchestrator.TryReserveAsync(
                inventoryDb, catalogDb, line.ProductId, line.Qty, cart.MarketCode,
                cart.AccountId, cart.Id, nowUtc, ct);
            if (result.IsSuccess)
            {
                line.ReservationId = result.ReservationId;
                line.StockChanged = false;
                line.UpdatedAt = nowUtc;
                changed = true;
            }
            else
            {
                // Can't cover the requested qty right now — keep the line visible but flag so the
                // UI surfaces the issue and EligibilityEvaluator blocks checkout.
                line.ReservationId = null;
                line.StockChanged = true;
                line.UpdatedAt = nowUtc;
                changed = true;
                logger.LogInformation(
                    "cart.reservation.rehydrate_failed cartId={CartId} productId={ProductId} qty={Qty} reason={Reason}",
                    cart.Id, line.ProductId, line.Qty, result.ReasonCode);
            }
        }

        if (!changed) return;
        try
        {
            await cartDb.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex is DbUpdateConcurrencyException
                || (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505"))
        {
            // Another mutation raced with us — the rehydrate wrote transient in-memory fields
            // only, so the read view still renders correctly from the reload below. Best-effort.
            cartDb.ChangeTracker.Clear();
        }
    }
}
