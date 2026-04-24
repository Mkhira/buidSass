using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Customer.UpdateLine;

public sealed record UpdateLineRequest(string MarketCode, int Qty);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapUpdateLineEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPatch("/lines/{lineId:guid}", HandleAsync);
        builder.MapDelete("/lines/{lineId:guid}", DeleteAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid lineId,
        UpdateLineRequest request,
        HttpContext context,
        CartDbContext db,
        CatalogDbContext catalogDb,
        InventoryDbContext inventoryDb,
        CartResolver resolver,
        CartViewBuilder viewBuilder,
        CartInventoryOrchestrator inventoryOrchestrator,
        CustomerContextResolver customerContextResolver,
        CancellationToken ct)
    {
        if (request.Qty < 0)
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.below_min_qty", "Qty must be >= 0", "Use qty=0 to remove.");
        }
        var nowUtc = DateTimeOffset.UtcNow;
        var accountId = await CustomerCartResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var suppliedToken = GetCart.Endpoint.ResolveToken(context);
        var cart = await resolver.LookupAsync(db, accountId, suppliedToken, request.MarketCode, nowUtc, ct);
        if (cart is null)
        {
            return CustomerCartResponseFactory.Problem(context, 404, "cart.not_found", "Cart not found", "");
        }
        var line = await db.CartLines.SingleOrDefaultAsync(l => l.Id == lineId && l.CartId == cart.Id, ct);
        if (line is null)
        {
            return CustomerCartResponseFactory.Problem(context, 404, "cart.line.not_found", "Line not found", "");
        }

        if (request.Qty == 0)
        {
            return await RemoveInternalAsync(db, catalogDb, inventoryDb, viewBuilder, customerContextResolver, cart, line, inventoryOrchestrator, accountId, nowUtc, ct, context);
        }

        // FR-007 bounds on the updated qty (only when the product still exists; if it's been
        // archived we still permit zero-ing out but reject positive updates).
        var product = await catalogDb.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == line.ProductId, ct);
        if (product is null)
        {
            return CustomerCartResponseFactory.Problem(context, 404, "cart.product.not_found", "Product missing", "");
        }
        var bounds = QtyBoundsValidator.Validate(product, request.Qty);
        if (!bounds.Ok)
        {
            return CustomerCartResponseFactory.Problem(context, 400, bounds.ReasonCode!, "Qty out of bounds", bounds.Detail ?? "");
        }

        // L8: release prior reservation BEFORE reserving new qty.
        Guid? priorReservationId = line.ReservationId;
        var priorQty = line.Qty;
        if (priorReservationId is { } prId)
        {
            await inventoryOrchestrator.TryReleaseAsync(inventoryDb, prId, CartSystemActors.ResolveActor(accountId), "cart.line.qty_updated", ct);
        }

        var reservation = await inventoryOrchestrator.TryReserveAsync(
            inventoryDb, catalogDb, line.ProductId, request.Qty, cart.MarketCode, accountId, cart.Id, nowUtc, ct);
        if (!reservation.IsSuccess)
        {
            // Best-effort rollback: restore the prior reservation so the line stays consistent.
            // If the rollback itself fails (stock moved, catalog drift, concurrent write), clear
            // the stale reservation pointer + flag StockChanged so the next read surfaces the
            // inventory problem rather than the cart silently pointing at released stock.
            if (priorReservationId is not null)
            {
                try
                {
                    var restore = await inventoryOrchestrator.TryReserveAsync(
                        inventoryDb, catalogDb, line.ProductId, priorQty, cart.MarketCode, accountId, cart.Id, nowUtc, ct);
                    if (restore.IsSuccess)
                    {
                        line.ReservationId = restore.ReservationId;
                        line.StockChanged = false;
                    }
                    else
                    {
                        line.ReservationId = null;
                        line.StockChanged = true;
                    }
                    line.UpdatedAt = nowUtc;
                    try { await db.SaveChangesAsync(ct); } catch { /* best-effort */ }
                }
                catch
                {
                    line.ReservationId = null;
                    line.StockChanged = true;
                    line.UpdatedAt = nowUtc;
                    try { await db.SaveChangesAsync(ct); } catch { /* best-effort */ }
                }
            }
            return CustomerCartResponseFactory.Problem(
                context, reservation.StatusCode, reservation.ReasonCode!, "Reservation failed", reservation.Detail ?? "",
                reservation.Extensions);
        }

        line.Qty = request.Qty;
        line.ReservationId = reservation.ReservationId;
        line.StockChanged = false;
        line.UpdatedAt = nowUtc;
        cart.LastTouchedAt = nowUtc;
        cart.UpdatedAt = nowUtc;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CustomerCartResponseFactory.ConcurrencyConflict(context, "Cart line was modified by another request.");
        }

        return await BuildViewResultAsync(db, catalogDb, viewBuilder, customerContextResolver, cart, accountId, nowUtc, ct);
    }

    private static async Task<IResult> DeleteAsync(
        Guid lineId,
        string? market,
        HttpContext context,
        CartDbContext db,
        CatalogDbContext catalogDb,
        InventoryDbContext inventoryDb,
        CartResolver resolver,
        CartViewBuilder viewBuilder,
        CartInventoryOrchestrator inventoryOrchestrator,
        CustomerContextResolver customerContextResolver,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(market))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.market_required", "Market required", "");
        }
        var normalizedMarket = market.Trim().ToLowerInvariant();
        var nowUtc = DateTimeOffset.UtcNow;
        var accountId = await CustomerCartResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var suppliedToken = GetCart.Endpoint.ResolveToken(context);
        var cart = await resolver.LookupAsync(db, accountId, suppliedToken, normalizedMarket, nowUtc, ct);
        if (cart is null)
        {
            return CustomerCartResponseFactory.Problem(context, 404, "cart.not_found", "Cart not found", "");
        }
        var line = await db.CartLines.SingleOrDefaultAsync(l => l.Id == lineId && l.CartId == cart.Id, ct);
        if (line is null)
        {
            return CustomerCartResponseFactory.Problem(context, 404, "cart.line.not_found", "Line not found", "");
        }
        return await RemoveInternalAsync(db, catalogDb, inventoryDb, viewBuilder, customerContextResolver, cart, line, inventoryOrchestrator, accountId, nowUtc, ct, context);
    }

    private static async Task<IResult> RemoveInternalAsync(
        CartDbContext db,
        CatalogDbContext catalogDb,
        InventoryDbContext inventoryDb,
        CartViewBuilder viewBuilder,
        CustomerContextResolver customerContextResolver,
        Entities.Cart cart,
        Entities.CartLine line,
        CartInventoryOrchestrator inventoryOrchestrator,
        Guid? accountId,
        DateTimeOffset nowUtc,
        CancellationToken ct,
        HttpContext context)
    {
        if (line.ReservationId.HasValue)
        {
            await inventoryOrchestrator.TryReleaseAsync(inventoryDb, line.ReservationId.Value, CartSystemActors.ResolveActor(accountId), "cart.line.removed", ct);
        }
        db.CartLines.Remove(line);
        cart.LastTouchedAt = nowUtc;
        cart.UpdatedAt = nowUtc;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CustomerCartResponseFactory.ConcurrencyConflict(context, "Cart line was modified by another request.");
        }
        return await BuildViewResultAsync(db, catalogDb, viewBuilder, customerContextResolver, cart, accountId, nowUtc, ct);
    }

    private static async Task<IResult> BuildViewResultAsync(
        CartDbContext db,
        CatalogDbContext catalogDb,
        CartViewBuilder viewBuilder,
        CustomerContextResolver customerContextResolver,
        Entities.Cart cart,
        Guid? accountId,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var ctxInfo = await customerContextResolver.ResolveAsync(accountId, ct);
        var lines = await db.CartLines.AsNoTracking().Where(l => l.CartId == cart.Id).OrderBy(l => l.AddedAt).ToListAsync(ct);
        var saved = await db.CartSavedItems.AsNoTracking().Where(s => s.CartId == cart.Id).ToListAsync(ct);
        var b2b = await db.CartB2BMetadata.AsNoTracking().SingleOrDefaultAsync(b => b.CartId == cart.Id, ct);
        return Results.Ok(await viewBuilder.BuildAsync(cart, lines, saved, b2b, catalogDb, ctxInfo.VerifiedForRestriction, ctxInfo.IsB2B, nowUtc, ct));
    }
}
