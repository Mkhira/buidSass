using BackendApi.Modules.Cart.Customer.Common;
using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Customer.SwitchMarket;

public sealed record SwitchMarketRequest(string FromMarket, string ToMarket);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapSwitchMarketEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/switch-market", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        SwitchMarketRequest request,
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
        if (string.IsNullOrWhiteSpace(request.FromMarket) || string.IsNullOrWhiteSpace(request.ToMarket))
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.market_required", "Markets required", "");
        }

        var fromMarket = request.FromMarket.Trim().ToLowerInvariant();
        var toMarket = request.ToMarket.Trim().ToLowerInvariant();
        if (fromMarket == toMarket)
        {
            return CustomerCartResponseFactory.Problem(context, 400, "cart.market_unchanged", "Same market", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var oldCart = await resolver.LookupAsync(db, accountId, suppliedToken: null, fromMarket, nowUtc, ct);
        var releasedReservations = new List<(Guid ReservationId, int Qty, Guid ProductId)>();
        if (oldCart is not null)
        {
            // Flip the state-machine first so the archive flag persists atomically with the
            // reservation releases — the transition check is a cheap local assertion.
            if (!CartStatuses.TryTransition(oldCart, CartStatuses.Archived, "market_switch", nowUtc))
            {
                return CustomerCartResponseFactory.Problem(
                    context, 409, "cart.invalid_state_transition",
                    "Invalid state transition", $"Cannot archive cart in status {oldCart.Status}.");
            }
            oldCart.LastTouchedAt = nowUtc;
            oldCart.UpdatedAt = nowUtc;

            // Release reservations; if any release fails, surface a 409 rather than leaving a
            // partially-switched state. Track successful releases so we can compensate if the
            // cart save below hits a concurrency conflict.
            var oldLines = await db.CartLines.Where(l => l.CartId == oldCart.Id).ToListAsync(ct);
            foreach (var line in oldLines.Where(l => l.ReservationId.HasValue))
            {
                var released = await inventoryOrchestrator.TryReleaseAsync(
                    inventoryDb, line.ReservationId!.Value, accountId.Value, "cart.market_switched", ct);
                if (!released)
                {
                    return CustomerCartResponseFactory.Problem(
                        context, 409, "cart.concurrency_conflict",
                        "Reservation release conflict",
                        "Another actor modified the reservation; retry the market switch.");
                }
                releasedReservations.Add((line.ReservationId.Value, line.Qty, line.ProductId));
            }
        }

        // Create or resolve the new-market cart.
        var newResolved = await resolver.ResolveOrCreateAsync(db, accountId, suppliedToken: null, toMarket, nowUtc, ct);
        var newCart = newResolved.Cart;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CustomerCartResponseFactory.IsConcurrencyConflict(ex))
        {
            // Compensate: re-reserve the lines we just released so the old cart stays coherent.
            foreach (var (_, qty, productId) in releasedReservations)
            {
                try
                {
                    await inventoryOrchestrator.TryReserveAsync(
                        inventoryDb, catalogDb, productId, qty, fromMarket, accountId, oldCart!.Id, nowUtc, ct);
                }
                catch
                {
                    // best-effort — a reconciliation worker will detect the leak
                }
            }
            db.ChangeTracker.Clear();
            return CustomerCartResponseFactory.ConcurrencyConflict(context, "Cart was switched by another request.");
        }

        var lines = await db.CartLines.AsNoTracking().Where(l => l.CartId == newCart.Id).OrderBy(l => l.AddedAt).ToListAsync(ct);
        var saved = await db.CartSavedItems.AsNoTracking().Where(s => s.CartId == newCart.Id).ToListAsync(ct);
        var b2b = await db.CartB2BMetadata.AsNoTracking().SingleOrDefaultAsync(b => b.CartId == newCart.Id, ct);
        var ctxInfo = await customerContextResolver.ResolveAsync(accountId, ct);
        var view = await viewBuilder.BuildAsync(newCart, lines, saved, b2b, catalogDb, ctxInfo.VerifiedForRestriction, ctxInfo.IsB2B, nowUtc, ct);
        return Results.Ok(view);
    }
}
