using BackendApi.Modules.Orders.Customer.Common;
using BackendApi.Modules.Orders.Internal.CreateFromQuotation;
using BackendApi.Modules.Orders.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Customer.Quotations.AcceptQuotation;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapCustomerAcceptQuotationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/accept", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "CustomerJwt" });
        return builder;
    }

    /// <summary>
    /// FR-011 / FR-012. Customer accepts an active quotation; routes through the
    /// CreateFromQuotation handler which preserves the stored explanation hash byte-identically
    /// (SC-006).
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        OrdersDbContext db,
        CreateFromQuotationHandler handler,
        CancellationToken ct)
    {
        var accountId = CustomerOrdersResponseFactory.ResolveAccountId(context);
        if (accountId is null)
        {
            return CustomerOrdersResponseFactory.Problem(context, 401, "orders.requires_auth", "Auth required", "");
        }
        // Quote ownership check before invoking the handler so cross-account accepts return 404.
        var quote = await db.Quotations.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null || quote.AccountId != accountId)
        {
            return CustomerOrdersResponseFactory.Problem(context, 404, "order.quote.not_found", "Quotation not found", "");
        }

        var result = await handler.CreateAsync(id, accountId, ct);
        if (!result.IsSuccess)
        {
            var status = result.ErrorCode switch
            {
                "order.quote.expired" => 400,
                "order.quote.invalid_status" => 409,
                "order.quote.empty" => 400,
                "order.quote.integrity_fail" => 500,
                _ => 400,
            };
            return CustomerOrdersResponseFactory.Problem(context, status,
                result.ErrorCode!, "Quotation accept failed", result.ErrorMessage ?? "");
        }
        return Results.Ok(new { orderId = result.OrderId, orderNumber = result.OrderNumber });
    }
}
