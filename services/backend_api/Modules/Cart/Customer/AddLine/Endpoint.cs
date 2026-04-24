using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Cart.Customer.AddLine;

public sealed record AddLineRequest(string MarketCode, Guid ProductId, int Qty);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAddLineEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/lines", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        AddLineRequest request,
        HttpContext context,
        CartDbContext db,
        CatalogDbContext catalogDb,
        InventoryDbContext inventoryDb,
        CartResolver resolver,
        CartViewBuilder viewBuilder,
        CartInventoryOrchestrator inventoryOrchestrator,
        CustomerContextResolver customerContextResolver,
        IOptions<CartOptions> cartOptions,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.MarketCode))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.market_required", "Market required", "marketCode is required.");
        }
        if (request.ProductId == Guid.Empty)
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.invalid_items", "Invalid product", "productId is required.");
        }
        if (request.Qty < 1)
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.below_min_qty", "Qty too low", "qty must be at least 1.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var accountId = await CustomerCartResponseFactory.TryResolveAuthenticatedAccountAsync(context);
        var suppliedToken = GetCart.Endpoint.ResolveToken(context);

        // Validate product + market
        var product = await catalogDb.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null || !string.Equals(product.Status, "published", StringComparison.OrdinalIgnoreCase))
        {
            return CustomerCartResponseFactory.Problem(context, 404, "cart.product.not_found", "Product not found", "Product is not available.");
        }
        var marketCode = request.MarketCode.Trim().ToLowerInvariant();
        if (!product.MarketCodes.Any(m => string.Equals(m, marketCode, StringComparison.OrdinalIgnoreCase)))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.product_market_mismatch", "Product not in market", "Product is not sold in the requested market.");
        }

        var options = cartOptions.Value;
        var logger = loggerFactory.CreateLogger("Cart.AddLine");

        // Retry loop for concurrency conflicts on cart creation (partial unique index
        // (account, market, active)) AND on cart_line insert (unique (cart_id, product_id)).
        // One retry suffices for the common two-tab race; beyond that we surface 409.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var resolved = await resolver.ResolveOrCreateAsync(db, accountId, suppliedToken, marketCode, nowUtc, ct);
            var cart = resolved.Cart;
            try
            {
                await db.SaveChangesAsync(ct); // materialise cart
            }
            catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
            {
                db.ChangeTracker.Clear();
                if (attempt == 0) continue;
                return CustomerCartResponseFactory.ConcurrencyConflict(context, "Cart creation race; retry.");
            }

            var existingLineCount = await db.CartLines.CountAsync(l => l.CartId == cart.Id, ct);
            var existingLine = await db.CartLines
                .SingleOrDefaultAsync(l => l.CartId == cart.Id && l.ProductId == request.ProductId, ct);
            if (existingLine is null && existingLineCount >= options.MaxLinesPerCart)
            {
                return CustomerCartResponseFactory.Problem(context, 413, "cart.too_many_lines", "Cart too large", $"Cart cannot exceed {options.MaxLinesPerCart} distinct lines.");
            }

            var targetQty = (existingLine?.Qty ?? 0) + request.Qty;

            // FR-007 bounds (min_order_qty + max_per_order + hard ceiling).
            var bounds = QtyBoundsValidator.Validate(product, targetQty);
            if (!bounds.Ok)
            {
                return CustomerCartResponseFactory.Problem(context, 400, bounds.ReasonCode!, "Qty out of bounds", bounds.Detail ?? "");
            }

            // L8: release old reservation BEFORE reserving new qty so stock.Reserved doesn't
            // transiently double-count. If the new reservation then fails, re-reserve old qty
            // to leave the line stable.
            Guid? priorReservationId = existingLine?.ReservationId;
            int? priorQty = existingLine?.Qty;
            if (priorReservationId is { } prId)
            {
                await inventoryOrchestrator.TryReleaseAsync(
                    inventoryDb, prId, CartSystemActors.ResolveActor(accountId), "cart.line.qty_updated", ct);
            }

            var reservation = await inventoryOrchestrator.TryReserveAsync(
                inventoryDb, catalogDb, request.ProductId, targetQty, marketCode, accountId, cart.Id, nowUtc, ct);
            if (!reservation.IsSuccess)
            {
                // Restore the prior reservation so the existing line stays consistent. If the
                // restore itself fails (catalog drift, stock moved, …) we MUST clear the line's
                // stale ReservationId — otherwise the cart row points at a reservation that has
                // already been released and downstream checkout operates on phantom inventory.
                // Flag StockChanged so the UI surfaces the invalidation (FR-022 / SC-007).
                if (existingLine is not null && priorReservationId is not null && priorQty is { } pq)
                {
                    try
                    {
                        var restore = await inventoryOrchestrator.TryReserveAsync(
                            inventoryDb, catalogDb, request.ProductId, pq, marketCode, accountId, cart.Id, nowUtc, ct);
                        if (restore.IsSuccess)
                        {
                            existingLine.ReservationId = restore.ReservationId;
                            existingLine.StockChanged = false;
                        }
                        else
                        {
                            existingLine.ReservationId = null;
                            existingLine.StockChanged = true;
                        }
                    }
                    catch
                    {
                        existingLine.ReservationId = null;
                        existingLine.StockChanged = true;
                    }
                    existingLine.UpdatedAt = nowUtc;
                    try { await db.SaveChangesAsync(ct); } catch { /* best-effort */ }
                }
                return CustomerCartResponseFactory.Problem(
                    context, reservation.StatusCode, reservation.ReasonCode!, "Reservation failed", reservation.Detail ?? "",
                    reservation.Extensions);
            }

            if (existingLine is null)
            {
                existingLine = new CartLine
                {
                    Id = Guid.NewGuid(),
                    CartId = cart.Id,
                    MarketCode = marketCode,
                    ProductId = request.ProductId,
                    Qty = request.Qty,
                    ReservationId = reservation.ReservationId,
                    Restricted = product.Restricted,
                    RestrictionReasonCode = product.RestrictionReasonCode,
                    AddedAt = nowUtc,
                    UpdatedAt = nowUtc,
                };
                db.CartLines.Add(existingLine);
            }
            else
            {
                existingLine.Qty = targetQty;
                existingLine.ReservationId = reservation.ReservationId;
                existingLine.Restricted = product.Restricted;
                existingLine.RestrictionReasonCode = product.RestrictionReasonCode;
                existingLine.StockChanged = false;
                existingLine.UpdatedAt = nowUtc;
            }

            cart.LastTouchedAt = nowUtc;
            cart.UpdatedAt = nowUtc;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
            {
                // Release the reservation we just created — a concurrent insert will re-drive
                // and re-reserve on its own attempt.
                if (reservation.ReservationId is { } rid)
                {
                    await inventoryOrchestrator.TryReleaseAsync(inventoryDb, rid, CartSystemActors.ResolveActor(accountId), "cart.concurrency_retry", ct);
                }
                db.ChangeTracker.Clear();
                if (attempt == 0) continue;
                return CustomerCartResponseFactory.ConcurrencyConflict(context, "Cart line insert race; retry.");
            }

            if (resolved.IssuedToken is not null)
            {
                GetCart.Endpoint.AttachTokenCookie(context, resolved.IssuedToken, options.TokenLifetimeDays);
            }

            logger.LogInformation(
                "cart.line_added cartId={CartId} accountId={AccountId} productId={ProductId} qty={Qty}",
                cart.Id, accountId, request.ProductId, targetQty);

            return await BuildViewResultAsync(db, catalogDb, viewBuilder, customerContextResolver, cart, accountId, nowUtc, ct);
        }
        return CustomerCartResponseFactory.ConcurrencyConflict(context, "Retry exhausted.");
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
        var view = await viewBuilder.BuildAsync(cart, lines, saved, b2b, catalogDb, ctxInfo.VerifiedForRestriction, ctxInfo.IsB2B, nowUtc, ct);
        return Results.Ok(view);
    }
}
