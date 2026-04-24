using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Cart.Customer.Restore;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapRestoreEndpoint(this IEndpointRouteBuilder builder)
    {
        // Spec contract path: POST /v1/customer/cart/restore/{archivedCartId}
        builder.MapPost("/restore/{archivedCartId:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid archivedCartId,
        HttpContext context,
        CartDbContext db,
        CatalogDbContext catalogDb,
        InventoryDbContext inventoryDb,
        CartViewBuilder viewBuilder,
        CartInventoryOrchestrator inventoryOrchestrator,
        CustomerContextResolver customerContextResolver,
        IOptions<CartOptions> cartOptions,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var accountId = CustomerCartResponseFactory.ResolveAccountId(context);
        if (accountId is null) return CustomerCartResponseFactory.Problem(context, 401, "cart.auth_required", "Auth required", "");

        var archived = await db.Carts.SingleOrDefaultAsync(c => c.Id == archivedCartId && c.AccountId == accountId, ct);
        if (archived is null)
        {
            return CustomerCartResponseFactory.Problem(context, 404, "cart.restore.not_found", "Archived cart not found", "");
        }
        if (!string.Equals(archived.Status, CartStatuses.Archived, StringComparison.OrdinalIgnoreCase))
        {
            return CustomerCartResponseFactory.Problem(context, 409, "cart.not_archived", "Cart not archived", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var retention = cartOptions.Value.ArchivedCartRetentionDays;
        if (archived.ArchivedAt is { } archivedAt && (nowUtc - archivedAt).TotalDays > retention)
        {
            return CustomerCartResponseFactory.Problem(
                context, 410, "cart.restore.expired",
                "Restore window expired", $"Archived carts are only restorable within {retention} days.");
        }

        // Partial unique index enforces one active cart per (account, market). Archive any
        // existing active cart in the same market — state-machine flip first, inventory
        // releases second (so if SaveChanges below throws concurrency we haven't already
        // mutated stock for the old cart without a record of it in the DB).
        var existingActive = await db.Carts.SingleOrDefaultAsync(
            c => c.AccountId == accountId && c.MarketCode == archived.MarketCode && c.Status == CartStatuses.Active, ct);

        List<Entities.CartLine> existingLines = new();
        if (existingActive is not null)
        {
            existingLines = await db.CartLines.Where(l => l.CartId == existingActive.Id).ToListAsync(ct);
            CartStatuses.TryTransition(existingActive, CartStatuses.Archived, "superseded_by_restore", nowUtc);
        }

        if (!CartStatuses.TryTransition(archived, CartStatuses.Active, null, nowUtc))
        {
            return CustomerCartResponseFactory.Problem(
                context, 409, "cart.invalid_state_transition",
                "Invalid state transition", $"Cannot activate cart in status {archived.Status}.");
        }
        archived.LastTouchedAt = nowUtc;

        // Persist the cart-state transitions BEFORE touching inventory. If SaveChanges fails
        // (partial-unique-index race, concurrency) we haven't yet released or re-reserved
        // anything — no compensation to chase.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
        {
            return CustomerCartResponseFactory.ConcurrencyConflict(context, "Restore race with another cart.");
        }

        // Cart state committed — now mutate inventory. Releases of the superseded cart first,
        // then re-reservations on the restored cart. Track everything so any mid-flight failure
        // can compensate (re-reserve released, release newly-reserved). The cart-state flip is
        // already durable, so on catastrophic failure we prefer a loud log + partial-state 409
        // over silently masking the inconsistency — operators reconcile via the stock-levels
        // consistency check (SC-007).
        var logger = loggerFactory.CreateLogger("Cart.Restore");
        var releasedReservations = new List<(Guid ReservationId, Guid ProductId, int Qty)>();
        var createdReservations = new List<Guid>();

        try
        {
            foreach (var line in existingLines.Where(l => l.ReservationId.HasValue))
            {
                var released = await inventoryOrchestrator.TryReleaseAsync(
                    inventoryDb, line.ReservationId!.Value, accountId.Value, "cart.superseded_by_restore", ct);
                if (!released)
                {
                    // Compensate the releases that DID succeed before this one so the
                    // superseded cart's reservations aren't left half-gone.
                    await CompensateReleasedAsync(
                        inventoryDb, catalogDb, inventoryOrchestrator,
                        releasedReservations, existingActive!, accountId.Value, nowUtc, logger, ct);
                    logger.LogWarning(
                        "cart.restore.release_failed archivedCartId={ArchivedCartId} reservationId={ReservationId} released_before_failure={ReleasedCount}",
                        archived.Id, line.ReservationId, releasedReservations.Count);
                    return CustomerCartResponseFactory.Problem(
                        context, 409, "cart.concurrency_conflict",
                        "Reservation release conflict",
                        "Could not release a reservation on the superseded cart; the prior releases were compensated. Retry.");
                }
                releasedReservations.Add((line.ReservationId.Value, line.ProductId, line.Qty));
            }

            var lines = await db.CartLines.Where(l => l.CartId == archived.Id).ToListAsync(ct);
            foreach (var line in lines)
            {
                var reservation = await inventoryOrchestrator.TryReserveAsync(
                    inventoryDb, catalogDb, line.ProductId, line.Qty, archived.MarketCode, accountId, archived.Id, nowUtc, ct);
                if (reservation.IsSuccess)
                {
                    line.ReservationId = reservation.ReservationId;
                    line.Unavailable = false;
                    line.StockChanged = false;
                    if (reservation.ReservationId is { } rid) createdReservations.Add(rid);
                }
                else
                {
                    line.ReservationId = null;
                    line.Unavailable = true;
                    line.StockChanged = true;
                }
                line.UpdatedAt = nowUtc;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mid-loop failure — compensate BOTH sides: release any newly-created reservations
            // on the restored cart AND re-reserve the released ones on the superseded cart.
            foreach (var rid in createdReservations)
            {
                try
                {
                    await inventoryOrchestrator.TryReleaseAsync(
                        inventoryDb, rid, accountId.Value, "cart.restore_reserve_rollback", ct);
                }
                catch { /* best-effort */ }
            }
            await CompensateReleasedAsync(
                inventoryDb, catalogDb, inventoryOrchestrator,
                releasedReservations, existingActive!, accountId.Value, nowUtc, logger, ct);
            throw;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var rid in createdReservations)
            {
                try
                {
                    await inventoryOrchestrator.TryReleaseAsync(
                        inventoryDb, rid, accountId.Value, "cart.restore_conflict_rollback", ct);
                }
                catch { /* best-effort */ }
            }
            if (ex is DbUpdateException dbEx && CustomerCartResponseFactory.IsConcurrencyConflict(dbEx))
            {
                return CustomerCartResponseFactory.ConcurrencyConflict(context, "Restore race with another cart.");
            }
            throw;
        }

        var ctxInfo = await customerContextResolver.ResolveAsync(accountId, ct);
        var savedItems = await db.CartSavedItems.AsNoTracking().Where(s => s.CartId == archived.Id).ToListAsync(ct);
        var b2b = await db.CartB2BMetadata.AsNoTracking().SingleOrDefaultAsync(b => b.CartId == archived.Id, ct);
        var cartLines = await db.CartLines.AsNoTracking().Where(l => l.CartId == archived.Id).OrderBy(l => l.AddedAt).ToListAsync(ct);
        var view = await viewBuilder.BuildAsync(archived, cartLines, savedItems, b2b, catalogDb, ctxInfo.VerifiedForRestriction, ctxInfo.IsB2B, nowUtc, ct);
        return Results.Ok(view);
    }

    /// <summary>
    /// Re-reserves reservations we previously released on the superseded cart. Best-effort —
    /// failures are logged so operators can reconcile, but we don't throw because the caller
    /// is already on a failure path.
    /// </summary>
    private static async Task CompensateReleasedAsync(
        InventoryDbContext inventoryDb,
        CatalogDbContext catalogDb,
        CartInventoryOrchestrator inventoryOrchestrator,
        List<(Guid ReservationId, Guid ProductId, int Qty)> releasedReservations,
        Entities.Cart supersededCart,
        Guid accountId,
        DateTimeOffset nowUtc,
        ILogger logger,
        CancellationToken ct)
    {
        foreach (var (_, productId, qty) in releasedReservations)
        {
            try
            {
                await inventoryOrchestrator.TryReserveAsync(
                    inventoryDb, catalogDb, productId, qty,
                    supersededCart.MarketCode, accountId, supersededCart.Id, nowUtc, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "cart.restore.compensation_failed supersededCartId={CartId} productId={ProductId} qty={Qty}",
                    supersededCart.Id, productId, qty);
            }
        }
    }
}
