using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Cart.Admin.Common;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Primitives;
using BackendApi.Modules.Identity.Authorization.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        IAuditEventPublisher auditEventPublisher,
        ILoggerFactory loggerFactory,
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
        // Guard pagination arithmetic: page * pageSize can overflow Int32 once page is in the
        // millions, which `.Skip()` then misinterprets as a negative offset.
        var offset = ((long)effectivePage - 1) * effectivePageSize;
        if (offset > int.MaxValue)
        {
            return AdminCartResponseFactory.Problem(
                context, 400, "cart.pagination_out_of_range",
                "Pagination out of range", "Requested page is too large.");
        }

        var query = db.Carts.AsNoTracking()
            .Where(c => c.Status == CartStatuses.Active && c.LastTouchedAt < cutoff && c.AccountId != null);
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
            .Skip((int)offset)
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

        // Principle 25 — privileged admin read emits an audit trail. Failures in audit emission
        // are logged but must not abort the endpoint: traceability is best-effort here, since
        // the data has already been read and returning a 500 would confuse the caller.
        var actorId = AdminCartResponseFactory.ResolveActorAccountId(context);
        try
        {
            await auditEventPublisher.PublishAsync(new AuditEvent(
                actorId,
                "admin",
                "cart.admin_abandoned_listed",
                nameof(Entities.Cart),
                // EntityId is required by Validate; there's no single cart under audit here, so
                // use the actor id as a stable grouping key for "admin bulk reads."
                actorId,
                null,
                new
                {
                    market,
                    from,
                    to,
                    page = effectivePage,
                    pageSize = effectivePageSize,
                    total,
                    returned = carts.Count,
                },
                "cart.admin.abandoned_list"), ct);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("Cart.Admin.ListAbandoned")
                .LogWarning(ex, "cart.admin_abandoned_listed.audit_failed actor={Actor}", actorId);
        }

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
