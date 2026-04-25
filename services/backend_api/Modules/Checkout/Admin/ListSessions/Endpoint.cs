using BackendApi.Modules.Checkout.Admin.Common;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Identity.Authorization.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Checkout.Admin.ListSessions;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminListSessionsEndpoint(this IEndpointRouteBuilder builder)
    {
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        builder.MapGet("/sessions", HandleAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission("checkout.read");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        CheckoutDbContext db,
        Guid? accountId,
        string? state,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var effectivePage = Math.Max(page ?? 1, 1);
        var effectivePageSize = Math.Clamp(pageSize ?? 25, 1, 200);
        var offset = ((long)effectivePage - 1) * effectivePageSize;
        if (offset > int.MaxValue)
        {
            return AdminCheckoutResponseFactory.Problem(context, 400, "checkout.pagination_out_of_range", "Page too large", "");
        }

        var query = db.Sessions.AsNoTracking().AsQueryable();
        if (accountId is { } aid) query = query.Where(s => s.AccountId == aid);
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(s => s.State == state);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(s => s.LastTouchedAt)
            .ThenByDescending(s => s.Id)
            .Skip((int)offset)
            .Take(effectivePageSize)
            .Select(s => new
            {
                id = s.Id,
                cartId = s.CartId,
                accountId = s.AccountId,
                marketCode = s.MarketCode,
                state = s.State,
                paymentMethod = s.PaymentMethod,
                lastTouchedAt = s.LastTouchedAt,
                expiresAt = s.ExpiresAt,
                orderId = s.OrderId,
                failureReasonCode = s.FailureReasonCode,
            })
            .ToListAsync(ct);
        return Results.Ok(new { items, total, page = effectivePage, pageSize = effectivePageSize });
    }
}
