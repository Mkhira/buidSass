using BackendApi.Modules.Orders.Customer.Common;
using BackendApi.Modules.Orders.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Customer.Quotations.ListQuotations;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapCustomerListQuotationsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>FR-011. Customer's own quotations.</summary>
    private static async Task<IResult> HandleAsync(
        HttpContext context,
        OrdersDbContext db,
        string? status,
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
        var q = db.Quotations.AsNoTracking().Where(o => o.AccountId == accountId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(o => o.Status == status);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.CreatedAt)
            .Skip((p - 1) * ps).Take(ps)
            .Select(o => new { o.Id, o.QuoteNumber, o.Status, o.ValidUntil, o.CreatedAt, o.MarketCode, o.ConvertedOrderId })
            .ToListAsync(ct);
        return Results.Ok(new
        {
            quotations = rows.Select(o => new
            {
                quotationId = o.Id,
                quoteNumber = o.QuoteNumber,
                status = o.Status,
                validUntil = o.ValidUntil,
                createdAt = o.CreatedAt,
                market = o.MarketCode,
                convertedOrderId = o.ConvertedOrderId,
            }),
            total,
            page = p,
            pageSize = ps,
        });
    }
}
