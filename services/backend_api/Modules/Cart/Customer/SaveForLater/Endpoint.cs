using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Customer.SaveForLater;

public sealed record MoveToSavedRequest(string MarketCode, Guid ProductId);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapSaveForLaterEndpoints(this IEndpointRouteBuilder builder)
    {
        var authz = new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" };
        // Contract paths — cart-contract.md §POST /v1/customer/cart/saved-items and §POST .../{productId}/restore
        builder.MapPost("/saved-items", MoveAsync).RequireAuthorization(authz);
        builder.MapPost("/saved-items/{productId:guid}/restore", RestoreAsync).RequireAuthorization(authz);
        return builder;
    }

    private static async Task<IResult> MoveAsync(
        MoveToSavedRequest request,
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
        var accountId = CustomerCartResponseFactory.ResolveAccountId(context);
        if (accountId is null) return CustomerCartResponseFactory.Problem(context, 401, "cart.auth_required", "Auth required", "");
        if (request.ProductId == Guid.Empty) return CustomerCartResponseFactory.Problem(context, 400, "cart.invalid_items", "productId required", "");

        var nowUtc = DateTimeOffset.UtcNow;
        var cart = await resolver.LookupAsync(db, accountId, suppliedToken: null, request.MarketCode, nowUtc, ct);
        if (cart is null) return CustomerCartResponseFactory.Problem(context, 404, "cart.not_found", "Cart not found", "");

        var line = await db.CartLines.SingleOrDefaultAsync(l => l.CartId == cart.Id && l.ProductId == request.ProductId, ct);
        if (line is null) return CustomerCartResponseFactory.Problem(context, 404, "cart.line.not_found", "Line not found", "");

        // Release reservation (saved items do not reserve — R8).
        if (line.ReservationId.HasValue)
        {
            await inventoryOrchestrator.TryReleaseAsync(inventoryDb, line.ReservationId.Value, accountId.Value, "cart.line.saved_for_later", ct);
        }

        // Upsert saved item — preserve qty so restore can bring it back at the same quantity.
        var saved = await db.CartSavedItems.SingleOrDefaultAsync(s => s.CartId == cart.Id && s.ProductId == request.ProductId, ct);
        if (saved is null)
        {
            db.CartSavedItems.Add(new CartSavedItem
            {
                CartId = cart.Id,
                MarketCode = cart.MarketCode,
                ProductId = request.ProductId,
                Qty = line.Qty,
                SavedAt = nowUtc,
            });
        }
        else
        {
            saved.Qty = line.Qty;
            saved.SavedAt = nowUtc;
        }
        db.CartLines.Remove(line);
        cart.LastTouchedAt = nowUtc;
        cart.UpdatedAt = nowUtc;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return CustomerCartResponseFactory.ConcurrencyConflict(context, "Cart was modified by another request.");
        }

        return Results.Ok(await BuildViewAsync(db, catalogDb, viewBuilder, customerContextResolver, cart, accountId, nowUtc, ct));
    }

    private static async Task<IResult> RestoreAsync(
        Guid productId,
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
        var accountId = CustomerCartResponseFactory.ResolveAccountId(context);
        if (accountId is null) return CustomerCartResponseFactory.Problem(context, 401, "cart.auth_required", "Auth required", "");
        if (string.IsNullOrWhiteSpace(market)) return CustomerCartResponseFactory.Problem(context, 400, "cart.market_required", "Market required", "");

        var nowUtc = DateTimeOffset.UtcNow;
        var cart = await resolver.LookupAsync(db, accountId, suppliedToken: null, market, nowUtc, ct);
        if (cart is null) return CustomerCartResponseFactory.Problem(context, 404, "cart.not_found", "Cart not found", "");

        var saved = await db.CartSavedItems.SingleOrDefaultAsync(s => s.CartId == cart.Id && s.ProductId == productId, ct);
        if (saved is null) return CustomerCartResponseFactory.Problem(context, 404, "cart.saved.not_found", "Saved item not found", "");

        var product = await catalogDb.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null || !string.Equals(product.Status, "published", StringComparison.OrdinalIgnoreCase))
        {
            return CustomerCartResponseFactory.Problem(context, 404, "cart.product.not_found", "Product not found", "");
        }

        var bounds = QtyBoundsValidator.Validate(product, saved.Qty);
        if (!bounds.Ok)
        {
            return CustomerCartResponseFactory.Problem(context, 400, bounds.ReasonCode!, "Qty out of bounds", bounds.Detail ?? "");
        }

        var reservation = await inventoryOrchestrator.TryReserveAsync(
            inventoryDb, catalogDb, productId, saved.Qty, cart.MarketCode, accountId, cart.Id, nowUtc, ct);
        if (!reservation.IsSuccess)
        {
            return CustomerCartResponseFactory.Problem(
                context, reservation.StatusCode, reservation.ReasonCode!, "Reservation failed", reservation.Detail ?? "",
                reservation.Extensions);
        }

        db.CartSavedItems.Remove(saved);
        db.CartLines.Add(new CartLine
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            MarketCode = cart.MarketCode,
            ProductId = productId,
            Qty = saved.Qty,
            ReservationId = reservation.ReservationId,
            Restricted = product.Restricted,
            RestrictionReasonCode = product.RestrictionReasonCode,
            AddedAt = nowUtc,
            UpdatedAt = nowUtc,
        });
        cart.LastTouchedAt = nowUtc;
        cart.UpdatedAt = nowUtc;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
        {
            if (reservation.ReservationId is { } rid)
            {
                await inventoryOrchestrator.TryReleaseAsync(inventoryDb, rid, accountId.Value, "cart.concurrency_retry", ct);
            }
            return CustomerCartResponseFactory.ConcurrencyConflict(context, "Cart line insert race.");
        }

        return Results.Ok(await BuildViewAsync(db, catalogDb, viewBuilder, customerContextResolver, cart, accountId, nowUtc, ct));
    }

    private static async Task<CartView> BuildViewAsync(
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
        return await viewBuilder.BuildAsync(cart, lines, saved, b2b, catalogDb, ctxInfo.VerifiedForRestriction, ctxInfo.IsB2B, nowUtc, ct);
    }
}
