using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Inventory.Admin.Common;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Inventory.Internal.Movements.Return;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapReturnMovementEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/movements/return", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("inventory.internal.return");

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        ReturnMovementRequest request,
        HttpContext context,
        InventoryDbContext db,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        CancellationToken cancellationToken)
    {
        var actorId = request.AccountId.GetValueOrDefault();
        if (actorId == Guid.Empty)
        {
            actorId = AdminInventoryResponseFactory.ResolveActorAccountId(context);
        }

        var result = await Handler.HandleAsync(
            request,
            db,
            atsCalculator,
            bucketMapper,
            availabilityEventEmitter,
            auditEventPublisher,
            actorId,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return AdminInventoryResponseFactory.Problem(
                context,
                result.StatusCode,
                result.ReasonCode!,
                ResolveTitle(result.ReasonCode!),
                result.Detail ?? string.Empty);
        }

        return Results.Ok(result.Response);
    }

    private static string ResolveTitle(string reasonCode) => reasonCode switch
    {
        "inventory.invalid_order_id" => "Invalid order id",
        "inventory.invalid_items" => "Invalid return items",
        _ => "Inventory return error",
    };
}
