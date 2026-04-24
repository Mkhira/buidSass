using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Inventory.Admin.Common;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Inventory.Internal.Reservations.Convert;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapConvertReservationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/reservations/{id:guid}/convert", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("inventory.internal.convert");

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        ConvertReservationRequest request,
        HttpContext context,
        InventoryDbContext inventoryDb,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        ReorderAlertEmitter reorderAlertEmitter,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // Fall back to the caller's JWT sub if AccountId wasn't supplied.
        // Principle 25: every mutation must have an actor. If neither source resolves, refuse to
        // persist an orphaned sale movement with ActorAccountId=null.
        var jwtSub = AdminInventoryResponseFactory.ResolveActorAccountId(context);
        var resolvedAccountId = (request.AccountId is { } supplied && supplied != Guid.Empty)
            ? supplied
            : jwtSub;
        if (resolvedAccountId == Guid.Empty)
        {
            return AdminInventoryResponseFactory.Problem(
                context,
                400,
                "inventory.actor_required",
                "Actor required",
                "Convert requires either request.accountId or an authenticated JWT sub claim.");
        }
        var effectiveRequest = request with { AccountId = resolvedAccountId };

        var result = await Handler.HandleAsync(
            id,
            effectiveRequest,
            inventoryDb,
            atsCalculator,
            bucketMapper,
            reorderAlertEmitter,
            availabilityEventEmitter,
            auditEventPublisher,
            loggerFactory.CreateLogger("InventoryReservationConvert"),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return AdminInventoryResponseFactory.Problem(
                context,
                result.StatusCode,
                result.ReasonCode!,
                ResolveTitle(result.ReasonCode!),
                result.Detail ?? string.Empty,
                result.Extensions);
        }

        return Results.Ok(result.Response);
    }

    private static string ResolveTitle(string reasonCode) => reasonCode switch
    {
        "inventory.reservation.not_found" => "Reservation not found",
        "inventory.reservation.expired" => "Reservation expired",
        "inventory.reservation.already_converted" => "Reservation already converted",
        "inventory.insufficient" => "Insufficient inventory",
        "inventory.invalid_order_id" => "Invalid order id",
        _ => "Inventory conversion error",
    };
}
