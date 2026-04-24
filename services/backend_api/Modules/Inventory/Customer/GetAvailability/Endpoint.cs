using BackendApi.Modules.Inventory.Customer.Common;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.AspNetCore.Routing;

namespace BackendApi.Modules.Inventory.Customer.GetAvailability;

public static class Endpoint
{
    public static IEndpointRouteBuilder MapGetAvailabilityEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/availability", HandleAsync);
        return builder;
    }

    private static async Task<IResult> HandleAsync(
        string productIds,
        string market,
        HttpContext context,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        var parsedProductIds = new List<Guid>();
        foreach (var token in productIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(token, out var productId))
            {
                return CustomerInventoryResponseFactory.Problem(
                    context,
                    400,
                    "inventory.invalid_items",
                    "Invalid product ids",
                    "Each productId must be a valid GUID.");
            }

            parsedProductIds.Add(productId);
        }

        var result = await Handler.HandleAsync(new GetAvailabilityRequest(parsedProductIds, market), db, cancellationToken);
        if (!result.IsSuccess)
        {
            return CustomerInventoryResponseFactory.Problem(
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
        "inventory.invalid_items" => "Invalid product ids",
        "inventory.warehouse_market_mismatch" => "Warehouse-market mismatch",
        _ => "Inventory availability error",
    };
}
