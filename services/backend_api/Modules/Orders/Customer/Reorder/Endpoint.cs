using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Orders.Customer.Common;
using BackendApi.Modules.Orders.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Customer.Reorder;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapReorderEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/reorder", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>
    /// FR-021. Seeds a new cart from a past order's lines. Unavailable products are skipped
    /// and listed in the response. Restricted products are still added (Principle 8) with a
    /// flag surfaced. Never mutates the original order.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        OrdersDbContext ordersDb,
        CartDbContext cartDb,
        CatalogDbContext catalogDb,
        CancellationToken ct)
    {
        var accountId = CustomerOrdersResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerOrdersResponseFactory.Problem(context, 401, "orders.requires_auth", "Auth required", "");
        }

        var order = await ordersDb.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null || order.AccountId != accountId)
        {
            return CustomerOrdersResponseFactory.Problem(context, 404, "order.not_found", "Order not found", "");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var marketCode = order.MarketCode.Trim().ToLowerInvariant();

        // Reuse the customer's active cart if one exists for this market; create otherwise.
        // Mirrors CartResolver.ResolveOrCreateAsync but for the authenticated path only —
        // reorder is auth-required so the anon-token branch never applies.
        var cart = await cartDb.Carts
            .FirstOrDefaultAsync(c => c.AccountId == accountId && c.MarketCode == marketCode
                && c.Status == CartStatuses.Active, ct);
        if (cart is null)
        {
            cart = new BackendApi.Modules.Cart.Entities.Cart
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                MarketCode = marketCode,
                Status = CartStatuses.Active,
                LastTouchedAt = nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
                OwnerId = "platform",
            };
            cartDb.Carts.Add(cart);
        }
        else
        {
            cart.LastTouchedAt = nowUtc;
            cart.UpdatedAt = nowUtc;
        }

        // Validate which products are still purchasable.
        var orderProductIds = order.Lines.Select(l => l.ProductId).Distinct().ToArray();
        var liveProducts = await catalogDb.Products.AsNoTracking()
            .Where(p => orderProductIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Restricted, p.Status })
            .ToDictionaryAsync(p => p.Id, ct);

        // Lines already in the cart aren't duplicated; the existing add-line endpoint enforces
        // (cart_id, product_id) unique. Here we just append with a contains check.
        var existingProductIds = await cartDb.CartLines
            .Where(l => l.CartId == cart.Id)
            .Select(l => l.ProductId)
            .ToListAsync(ct);

        var added = 0;
        var skipped = new List<object>();
        foreach (var ol in order.Lines)
        {
            if (existingProductIds.Contains(ol.ProductId))
            {
                skipped.Add(new { productId = ol.ProductId, reason = "already_in_cart" });
                continue;
            }
            if (!liveProducts.TryGetValue(ol.ProductId, out var product))
            {
                skipped.Add(new { productId = ol.ProductId, reason = "product_unavailable" });
                continue;
            }
            if (!string.Equals(product.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new { productId = ol.ProductId, reason = "product_inactive" });
                continue;
            }
            var qty = ol.Qty - ol.CancelledQty - ol.ReturnedQty;
            if (qty <= 0)
            {
                skipped.Add(new { productId = ol.ProductId, reason = "no_remaining_qty" });
                continue;
            }
            cartDb.CartLines.Add(new BackendApi.Modules.Cart.Entities.CartLine
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                MarketCode = marketCode,
                ProductId = ol.ProductId,
                Qty = qty,
                Restricted = product.Restricted,
                AddedAt = nowUtc,
                UpdatedAt = nowUtc,
            });
            added++;
        }

        if (added == 0 && cart.Id != Guid.Empty && skipped.Count == order.Lines.Count)
        {
            return CustomerOrdersResponseFactory.Problem(context, 400, "order.reorder.no_eligible_lines",
                "No order lines are currently re-orderable.", "",
                new Dictionary<string, object?> { ["skippedLines"] = skipped });
        }

        await cartDb.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            cartId = cart.Id,
            addedLineCount = added,
            skippedLines = skipped,
        });
    }
}
