using BackendApi.Modules.AuditLog;
using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Inventory.Admin.Common;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Inventory.Internal.Reservations.Release;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapReleaseReservationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/reservations/{id:guid}", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("inventory.internal.release");

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        Guid? accountId,
        string? reason,
        HttpContext context,
        InventoryDbContext inventoryDb,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        AvailabilityEventEmitter availabilityEventEmitter,
        IAuditEventPublisher auditEventPublisher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var actorId = accountId ?? AdminInventoryResponseFactory.ResolveActorAccountId(context);

        var result = await Handler.HandleAsync(
            id,
            actorId,
            reason,
            inventoryDb,
            atsCalculator,
            bucketMapper,
            availabilityEventEmitter,
            auditEventPublisher,
            loggerFactory.CreateLogger("InventoryReservationRelease"),
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

        return Results.NoContent();
    }

    private static string ResolveTitle(string reasonCode) => reasonCode switch
    {
        "inventory.reservation.not_found" => "Reservation not found",
        "inventory.reservation.already_converted" => "Reservation already converted",
        "inventory.insufficient" => "Insufficient inventory",
        _ => "Inventory release error",
    };
}
