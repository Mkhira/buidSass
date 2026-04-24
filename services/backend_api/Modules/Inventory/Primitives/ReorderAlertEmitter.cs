using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Inventory.Primitives;

public sealed class ReorderAlertEmitter
{
    public async Task EmitIfCrossedAsync(
        InventoryDbContext db,
        StockLevel stock,
        int atsBefore,
        int atsAfter,
        DateTimeOffset nowUtc,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!IsDownwardCrossing(stock.ReorderThreshold, atsBefore, atsAfter))
        {
            return;
        }

        var utc = nowUtc.ToUniversalTime();
        var windowStartHour = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);

        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inventory.reorder_alert_debounce
                ("WarehouseId", "ProductId", "WindowStartHour", "EmittedAt")
            VALUES
                ({stock.WarehouseId}, {stock.ProductId}, {windowStartHour}, {nowUtc})
            ON CONFLICT ("WarehouseId", "ProductId", "WindowStartHour") DO NOTHING;
            """, cancellationToken);

        // Only emit the event when we actually claimed the debounce slot. Without this check the
        // log/event fires on every crossing within the hour — SC-006 requires exactly-once per window.
        if (inserted == 0)
        {
            return;
        }

        // TODO(spec 011/012): publish this on the real event bus once cross-module bus wiring lands.
        logger.LogInformation(
            "inventory.reorder_threshold_crossed warehouseId={WarehouseId} productId={ProductId} threshold={Threshold} atsBefore={AtsBefore} atsAfter={AtsAfter} windowStartHour={WindowStartHour}",
            stock.WarehouseId,
            stock.ProductId,
            stock.ReorderThreshold,
            atsBefore,
            atsAfter,
            windowStartHour);
    }

    private static bool IsDownwardCrossing(int threshold, int atsBefore, int atsAfter)
    {
        return atsBefore > threshold && atsAfter <= threshold;
    }
}
