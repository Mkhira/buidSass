using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Admin.ListOrders;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminListOrdersEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.read");
        return builder;
    }

    /// <summary>FR-010. Admin list with filters.</summary>
    private static async Task<IResult> HandleAsync(
        OrdersDbContext db,
        string? state,
        string? market,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 20, 1, 200);
        var q = db.Orders.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(market)) q = q.Where(o => o.MarketCode == market);
        if (from is not null) q = q.Where(o => o.PlacedAt >= from);
        if (to is not null) q = q.Where(o => o.PlacedAt <= to);
        if (!string.IsNullOrWhiteSpace(state))
        {
            // Single-state column filter: match against the four columns. UI layer can filter
            // further by high-level status using ListOrders' projection on the client.
            q = q.Where(o =>
                o.OrderState == state || o.PaymentState == state || o.FulfillmentState == state || o.RefundState == state);
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(o => o.PlacedAt)
            .Skip((p - 1) * ps)
            .Take(ps)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.PlacedAt,
                o.GrandTotalMinor,
                o.Currency,
                o.AccountId,
                o.MarketCode,
                o.OrderState,
                o.PaymentState,
                o.FulfillmentState,
                o.RefundState,
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            orders = rows.Select(o => new
            {
                orderId = o.Id,
                orderNumber = o.OrderNumber,
                placedAt = o.PlacedAt,
                accountId = o.AccountId,
                market = o.MarketCode,
                grandTotalMinor = o.GrandTotalMinor,
                currency = o.Currency,
                states = new
                {
                    order = o.OrderState,
                    payment = o.PaymentState,
                    fulfillment = o.FulfillmentState,
                    refund = o.RefundState,
                },
                highLevelStatus = HighLevelStatusProjector.Project(
                    o.OrderState, o.PaymentState, o.FulfillmentState, o.RefundState),
            }),
            total,
            page = p,
            pageSize = ps,
        });
    }
}
