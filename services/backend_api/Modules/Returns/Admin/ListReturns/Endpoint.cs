using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Common;
using BackendApi.Modules.Returns.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.ListReturns;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminListReturnsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.read");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ReturnsDbContext db,
        string? market,
        string? state,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 20, 1, 200);

        var q = db.ReturnRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(market)) q = q.Where(r => r.MarketCode == market);
        if (!string.IsNullOrWhiteSpace(state)) q = q.Where(r => r.State == state);
        if (from is { } f) q = q.Where(r => r.SubmittedAt >= f);
        if (to is { } t) q = q.Where(r => r.SubmittedAt <= t);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(r => r.SubmittedAt)
            .Skip((p - 1) * ps).Take(ps)
            .Select(r => new
            {
                id = r.Id,
                returnNumber = r.ReturnNumber,
                orderId = r.OrderId,
                accountId = r.AccountId,
                marketCode = r.MarketCode,
                state = r.State,
                reasonCode = r.ReasonCode,
                submittedAt = r.SubmittedAt,
                forceRefund = r.ForceRefund,
                lineCount = r.Lines.Count,
            })
            .ToListAsync(ct);

        return Results.Ok(new { page = p, pageSize = ps, total, items });
    }
}
