using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Identity.Authorization.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Cart.Admin.ListAbandoned;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminListAbandonedEndpoint(this IEndpointRouteBuilder builder)
    {
        var adminAuth = new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" };
        builder.MapGet("/abandoned", HandleAsync)
            .RequireAuthorization(adminAuth)
            .RequirePermission("cart.admin.read");
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        CartDbContext db,
        IOptions<CartOptions> cartOptions,
        string? market,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var cutoff = nowUtc.AddMinutes(-cartOptions.Value.AbandonmentIdleMinutes);
        var effectivePageSize = Math.Clamp(pageSize ?? 50, 1, 500);
        var effectivePage = Math.Max(page ?? 1, 1);

        var query = db.Carts.AsNoTracking()
            .Where(c => c.Status == "active" && c.LastTouchedAt < cutoff && c.AccountId != null);
        if (!string.IsNullOrWhiteSpace(market))
        {
            var m = market.Trim().ToLowerInvariant();
            query = query.Where(c => c.MarketCode == m);
        }
        if (from is { } f)
        {
            query = query.Where(c => c.LastTouchedAt >= f);
        }
        if (to is { } t)
        {
            query = query.Where(c => c.LastTouchedAt <= t);
        }

        var total = await query.CountAsync(ct);

        var carts = await query
            .OrderBy(c => c.LastTouchedAt)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(c => new
            {
                id = c.Id,
                accountId = c.AccountId,
                marketCode = c.MarketCode,
                lastTouchedAt = c.LastTouchedAt,
                couponCode = c.CouponCode,
            })
            .ToListAsync(ct);

        return Results.Ok(new
        {
            items = carts,
            total,
            page = effectivePage,
            pageSize = effectivePageSize,
            idleMinutes = cartOptions.Value.AbandonmentIdleMinutes,
        });
    }
}
