using BackendApi.Modules.Inventory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Inventory.Customer.GetAvailability;

public static class Handler
{
    public sealed record Result(
        bool IsSuccess,
        int StatusCode,
        string? ReasonCode,
        string? Detail,
        GetAvailabilityResponse? Response);

    public static async Task<Result> HandleAsync(
        GetAvailabilityRequest request,
        InventoryDbContext db,
        CancellationToken cancellationToken)
    {
        if (request.ProductIds.Count == 0)
        {
            return new Result(false, 400, "inventory.invalid_items", "At least one product id is required.", null);
        }

        if (string.IsNullOrWhiteSpace(request.MarketCode))
        {
            return new Result(false, 400, "inventory.warehouse_market_mismatch", "Market code is required.", null);
        }

        var marketCode = request.MarketCode.Trim().ToLowerInvariant();
        var warehouse = await db.Warehouses
            .AsNoTracking()
            .Where(x => x.IsActive && x.MarketCode == marketCode)
            .OrderBy(x => x.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (warehouse is null)
        {
            return new Result(false, 400, "inventory.warehouse_market_mismatch", "No active warehouse is configured for this market.", null);
        }

        var productIds = request.ProductIds.Distinct().ToArray();
        var stockRows = await db.StockLevels
            .AsNoTracking()
            .Where(x => x.WarehouseId == warehouse.Id && productIds.Contains(x.ProductId))
            .ToListAsync(cancellationToken);

        var byProductId = stockRows.ToDictionary(x => x.ProductId, x => x.BucketCache);

        var items = productIds
            .Select(productId => new GetAvailabilityItem(
                productId,
                byProductId.TryGetValue(productId, out var bucket) ? bucket : "out_of_stock"))
            .ToList();

        return new Result(true, 200, null, null, new GetAvailabilityResponse(items));
    }
}
