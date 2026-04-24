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
                    // Cart state flip is already durable (line 82). Returning a retryable 409
                    // would be misleading — the next call would fail because the target is
                    // already Active. Compensate the earlier releases (re-reserve + persist
                    // new ids on the superseded cart's lines) and treat the restore as
                    // authoritative. The one failed release stays attached to its stale
                    // reservation id; SC-007's reservation-consistency check surfaces it
                    // for operator reconciliation.
                    var unrecovered = await CompensateReleasedAsync(
                        db, inventoryDb, catalogDb, inventoryOrchestrator,
                        releasedReservations, existingActive!, accountId.Value, nowUtc, logger, ct);
                    logger.LogWarning(
                        "cart.restore.partial_release archivedCartId={ArchivedCartId} failedReservationId={ReservationId} releasedBeforeFailure={ReleasedCount} unrecovered={Unrecovered}",
                        archived.Id, line.ReservationId, releasedReservations.Count, unrecovered);
                    releasedReservations.Clear(); // compensation has re-reserved these
                    break;
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
                db, inventoryDb, catalogDb, inventoryOrchestrator,
                releasedReservations, existingActive!, accountId.Value, nowUtc, logger, ct);
            throw;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Release the reservations we just created — they'd leak otherwise since the
            // cart-line updates (which would have pointed at them) got rolled back.
            foreach (var rid in createdReservations)
            {
                try
                {
                    await inventoryOrchestrator.TryReleaseAsync(
                        inventoryDb, rid, accountId.Value, "cart.restore_conflict_rollback", ct);
                }
                catch { /* best-effort */ }
            }
            // The archive → active state flip committed at the earlier save (line 82) is
            // durable. A retryable 409 would mislead the caller because the next attempt
            // would see a non-archived cart and fail at line 43's state check. Instead, log
            // loud — the lines are StockChanged-flagged in memory but not persisted — and
            // reload the cart + lines fresh so the response reflects the durable state.
            logger.LogWarning(ex,
                "cart.restore.post_reserve_save_failed archivedCartId={ArchivedCartId} — returning degraded view; restored cart is active but line reservation refs weren't persisted.",
                archived.Id);
            if (ex is not DbUpdateException dbEx || !CustomerCartResponseFactory.IsConcurrencyConflict(dbEx))
            {
                throw;
            }
            // Fall through to the response-build below — reload archived so the view
            // reflects durable DB state (the line updates we tried to persist are gone).
            db.ChangeTracker.Clear();
            archived = await db.Carts.AsNoTracking().SingleAsync(c => c.Id == archived.Id, ct);
        }

        var ctxInfo = await customerContextResolver.ResolveAsync(accountId, ct);
        var savedItems = await db.CartSavedItems.AsNoTracking().Where(s => s.CartId == archived.Id).ToListAsync(ct);
        var b2b = await db.CartB2BMetadata.AsNoTracking().SingleOrDefaultAsync(b => b.CartId == archived.Id, ct);
        var cartLines = await db.CartLines.AsNoTracking().Where(l => l.CartId == archived.Id).OrderBy(l => l.AddedAt).ToListAsync(ct);
        var view = await viewBuilder.BuildAsync(archived, cartLines, savedItems, b2b, catalogDb, ctxInfo.VerifiedForRestriction, ctxInfo.IsB2B, nowUtc, ct);
        return Results.Ok(view);
    }

    /// <summary>
    /// Re-reserves reservations we previously released on the superseded cart, writes the
    /// replacement ReservationId onto the matching CartLine, and saves. Logs + returns the
    /// count of lines that couldn't be re-reserved so the caller can surface it. Failures
    /// throwing inside the orchestrator are swallowed (operator reconciles via SC-007).
    /// </summary>
    private static async Task<int> CompensateReleasedAsync(
        CartDbContext cartDb,
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
        if (releasedReservations.Count == 0) return 0;

        var supersededLines = await cartDb.CartLines
            .Where(l => l.CartId == supersededCart.Id)
            .ToListAsync(ct);
        var unrecoveredLines = 0;
        foreach (var (_, productId, qty) in releasedReservations)
        {
            var targetLine = supersededLines.FirstOrDefault(l => l.ProductId == productId);
            try
            {
                var result = await inventoryOrchestrator.TryReserveAsync(
                    inventoryDb, catalogDb, productId, qty,
                    supersededCart.MarketCode, accountId, supersededCart.Id, nowUtc, ct);
                if (result.IsSuccess && result.ReservationId is { } newRid)
                {
                    if (targetLine is not null)
                    {
                        targetLine.ReservationId = newRid;
                        targetLine.UpdatedAt = nowUtc;
                    }
                }
                else
                {
                    unrecoveredLines++;
                    if (targetLine is not null)
                    {
                        // Stale pointer now — clear it so the next read doesn't dereference
                        // a released reservation, and flag StockChanged for UI visibility.
                        targetLine.ReservationId = null;
                        targetLine.StockChanged = true;
                        targetLine.UpdatedAt = nowUtc;
                    }
                    logger.LogError(
                        "cart.restore.compensation_reserve_failed supersededCartId={CartId} productId={ProductId} qty={Qty} reason={Reason}",
                        supersededCart.Id, productId, qty, result.ReasonCode);
                }
            }
            catch (Exception ex)
            {
                unrecoveredLines++;
                // Same stale-pointer hygiene as the !IsSuccess branch — the exception path
                // must not leave the cart pointing at a reservation we definitely released.
                if (targetLine is not null)
                {
                    targetLine.ReservationId = null;
                    targetLine.StockChanged = true;
                    targetLine.UpdatedAt = nowUtc;
                }
                logger.LogError(ex,
                    "cart.restore.compensation_threw supersededCartId={CartId} productId={ProductId} qty={Qty}",
                    supersededCart.Id, productId, qty);
            }
        }

        try
        {
            await cartDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "cart.restore.compensation_persist_failed supersededCartId={CartId} — superseded cart lines still reference stale reservation ids.",
                supersededCart.Id);
        }
        return unrecoveredLines;
    }
}
