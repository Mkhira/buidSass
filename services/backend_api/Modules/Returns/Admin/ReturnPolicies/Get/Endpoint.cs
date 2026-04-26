using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Returns.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Admin.ReturnPolicies.Get;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminGetReturnPoliciesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("returns.read");
        return builder;
    }

    private static async Task<IResult> HandleAsync(ReturnsDbContext db, CancellationToken ct)
    {
        var rows = await db.ReturnPolicies.AsNoTracking()
            .OrderBy(p => p.MarketCode)
            .ToListAsync(ct);
        return Results.Ok(new
        {
            items = rows.Select(p => new
            {
                marketCode = p.MarketCode,
                returnWindowDays = p.ReturnWindowDays,
                autoApproveUnderDays = p.AutoApproveUnderDays,
                restockingFeeBp = p.RestockingFeeBp,
                shippingRefundOnFullOnly = p.ShippingRefundOnFullOnly,
                updatedAt = p.UpdatedAt,
            }),
        });
    }
}
