using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.TaxInvoices.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.TaxInvoices.Admin.ListInvoices;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminListInvoicesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("invoices.read");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        InvoicesDbContext db,
        string? market,
        string? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        if (from is not null && to is not null && from > to)
        {
            return Results.Json(new { error = "from must be on or before to" }, statusCode: 400);
        }
        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 20, 1, 200);
        var q = db.Invoices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(market)) q = q.Where(i => i.MarketCode == market);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(i => i.State == status);
        if (from is not null) q = q.Where(i => i.IssuedAt >= from);
        if (to is not null) q = q.Where(i => i.IssuedAt <= to);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(i => i.IssuedAt)
            .Skip((p - 1) * ps).Take(ps)
            .Select(i => new
            {
                invoiceId = i.Id,
                invoiceNumber = i.InvoiceNumber,
                orderId = i.OrderId,
                accountId = i.AccountId,
                market = i.MarketCode,
                currency = i.Currency,
                issuedAt = i.IssuedAt,
                grandTotalMinor = i.GrandTotalMinor,
                state = i.State,
            })
            .ToListAsync(ct);
        return Results.Ok(new { invoices = rows, total, page = p, pageSize = ps });
    }
}
