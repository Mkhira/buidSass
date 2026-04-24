using BackendApi.Modules.Identity.Authorization.Filters;
using BackendApi.Modules.Inventory.Admin.Common;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Inventory.Primitives;
using BackendApi.Modules.Inventory.Primitives.Fefo;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Observability;
using BackendApi.Modules.AuditLog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace BackendApi.Modules.Inventory.Internal.Reservations.Create;

public static class Endpoint
{
    // TODO (spec 011): migrate from Admin JWT to service-to-service JWT for internal inventory routes.
    public static IEndpointRouteBuilder MapCreateReservationEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/reservations", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "AdminJwt" })
            .RequirePermission("inventory.internal.reserve");

        return builder;
    }

    private static async Task<IResult> HandleAsync(
        CreateReservationRequest request,
        HttpContext context,
        InventoryDbContext inventoryDb,
        CatalogDbContext catalogDb,
        AtsCalculator atsCalculator,
        BucketMapper bucketMapper,
        FefoPicker fefoPicker,
        ReorderAlertEmitter reorderAlertEmitter,
        AvailabilityEventEmitter availabilityEventEmitter,
        InventoryMetrics inventoryMetrics,
        IAuditEventPublisher auditEventPublisher,
        IOptions<InventoryOptions> inventoryOptions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // Fall back to JWT sub when the body omits AccountId. Ensures audit rows fire for trusted
        // internal callers that forgot the field (Principle 25).
        var jwtSub = AdminInventoryResponseFactory.ResolveActorAccountId(context);
        var effectiveRequest = request.AccountId is { } supplied && supplied != Guid.Empty
            ? request
            : request with { AccountId = jwtSub == Guid.Empty ? null : jwtSub };

        var result = await Handler.HandleAsync(
            effectiveRequest,
            inventoryDb,
            catalogDb,
            atsCalculator,
            bucketMapper,
            fefoPicker,
            reorderAlertEmitter,
            availabilityEventEmitter,
            inventoryMetrics,
            auditEventPublisher,
            inventoryOptions,
            loggerFactory.CreateLogger("InventoryReservationCreate"),
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
        "inventory.insufficient" => "Insufficient inventory",
        "inventory.warehouse_market_mismatch" => "Warehouse-market mismatch",
        "inventory.invalid_items" => "Invalid reservation items",
        "inventory.invalid_qty" => "Invalid quantity",
        _ => "Inventory reservation error",
    };
}
