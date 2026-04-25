using BackendApi.Modules.Orders.Customer.Common;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Customer.ListOrders;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapListOrdersEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>FR-009 / FR-020. Customers see only their own orders.</summary>
    private static async Task<IResult> HandleAsync(
        HttpContext context,
        OrdersDbContext db,
        string? status,
        string? market,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var accountId = CustomerOrdersResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerOrdersResponseFactory.Problem(context, 401, "orders.requires_auth", "Auth required", "");
        }
        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 20, 1, 100);

        var q = db.Orders.AsNoTracking().Where(o => o.AccountId == accountId);
        if (!string.IsNullOrWhiteSpace(market)) q = q.Where(o => o.MarketCode == market);
        if (from is not null) q = q.Where(o => o.PlacedAt >= from);
        if (to is not null) q = q.Where(o => o.PlacedAt <= to);

        // Status filter is applied client-side after high-level projection because the projector
        // is a pure C# function combining four columns; pushing it into SQL would diverge over time.
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
                o.OrderState,
                o.PaymentState,
                o.FulfillmentState,
                o.RefundState,
            })
            .ToListAsync(ct);

        var projected = rows.Select(o =>
        {
            var hls = HighLevelStatusProjector.Project(o.OrderState, o.PaymentState, o.FulfillmentState, o.RefundState);
            return new
            {
                orderId = o.Id,
                orderNumber = o.OrderNumber,
                placedAt = o.PlacedAt,
                grandTotalMinor = o.GrandTotalMinor,
                currency = o.Currency,
                highLevelStatus = hls,
            };
        });

        if (!string.IsNullOrWhiteSpace(status))
        {
            projected = projected.Where(o => string.Equals(o.highLevelStatus, status, StringComparison.OrdinalIgnoreCase));
        }

        return Results.Ok(new
        {
            orders = projected,
            total,
            page = p,
            pageSize = ps,
        });
    }
}
