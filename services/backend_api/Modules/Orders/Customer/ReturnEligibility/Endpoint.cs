using BackendApi.Modules.Orders.Customer.Common;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Customer.ReturnEligibility;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapReturnEligibilityEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id:guid}/return-eligibility", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>FR-009 / SC-009. Per-market return-eligibility window.</summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        OrdersDbContext db,
        ReturnEligibilityEvaluator evaluator,
        CancellationToken ct)
    {
        var accountId = CustomerOrdersResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerOrdersResponseFactory.Problem(context, 401, "orders.requires_auth", "Auth required", "");
        }
        var order = await db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null || order.AccountId != accountId)
        {
            return CustomerOrdersResponseFactory.Problem(context, 404, "order.not_found", "Order not found", "");
        }
        var result = evaluator.Evaluate(order, DateTimeOffset.UtcNow);
        return Results.Ok(new
        {
            eligible = result.Eligible,
            daysRemaining = result.DaysRemaining,
            reasonCode = result.ReasonCode,
        });
    }
}
