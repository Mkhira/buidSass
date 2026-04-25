using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Orders.Admin.Common;
using BackendApi.Modules.Orders.Admin.Fulfillment.Common;
using BackendApi.Modules.Orders.Internal.CreateFromQuotation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Orders.Admin.Quotations.ConvertQuotation;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapAdminConvertQuotationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/{id:guid}/convert", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("orders.quotations.write");
        return builder;
    }

    /// <summary>
    /// FR-011 / FR-012. Admin converts an active quotation to an order on the customer's
    /// behalf (e.g., recorded a phone-call acceptance). Routes through the same handler the
    /// customer accept endpoint uses so SC-006 hash identity holds either way.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        Guid id,
        HttpContext context,
        CreateFromQuotationHandler handler,
        IAuditEventPublisher auditPublisher,
        CancellationToken ct)
    {
        var actor = AdminOrdersResponseFactory.ResolveActorAccountId(context);
        if (actor is null || actor == Guid.Empty)
        {
            return AdminOrdersResponseFactory.Problem(context, 401, "orders.actor_required", "Actor required", "");
        }
        var result = await handler.CreateAsync(id, actor, ct);
        if (!result.IsSuccess)
        {
            var status = result.ErrorCode switch
            {
                "order.quote.not_found" => 404,
                "order.quote.expired" => 400,
                "order.quote.invalid_status" => 409,
                "order.quote.empty" => 400,
                "order.quote.integrity_fail" => 500,
                _ => 400,
            };
            return AdminOrdersResponseFactory.Problem(context, status,
                result.ErrorCode!, "Quotation convert failed", result.ErrorMessage ?? "");
        }

        await FulfillmentOps.EmitAdminAuditAsync(auditPublisher, id, actor.Value,
            "orders.quotation.convert",
            null,
            new { orderId = result.OrderId, orderNumber = result.OrderNumber },
            null, ct);

        return Results.Ok(new { orderId = result.OrderId, orderNumber = result.OrderNumber });
    }
}
