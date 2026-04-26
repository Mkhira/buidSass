using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Returns.Tests.Infrastructure;

public static class ReturnsTestSeed
{
    /// <summary>
    /// Seeds a <c>delivered</c> + <c>captured</c> order with one line, plus a corresponding
    /// inventory sale movement (so the inspection slice can reverse-restock). Returns the
    /// order id and line id.
    /// </summary>
    public static Task<(Order Order, OrderLine Line)> SeedDeliveredCapturedOrderAsync(
        ReturnsTestFactory factory,
        Guid accountId,
        string market = "KSA",
        long unitPriceMinor = 100_00,
        int taxRateBp = 1500,
        int qty = 1)
        => SeedDeliveredOrderAsync(factory, accountId, market, unitPriceMinor, taxRateBp, qty,
            paymentProviderId: "stub",
            paymentProviderTxnId: $"txn-{Guid.NewGuid():N}");

    /// <summary>COD variant: <c>PaymentProviderId</c> + <c>PaymentProviderTxnId</c> are null
    /// so the IssueRefund slice routes the refund to the manual-bank-transfer path.</summary>
    public static Task<(Order Order, OrderLine Line)> SeedDeliveredCodOrderAsync(
        ReturnsTestFactory factory,
        Guid accountId,
        string market = "KSA",
        long unitPriceMinor = 100_00,
        int taxRateBp = 1500,
        int qty = 1)
        => SeedDeliveredOrderAsync(factory, accountId, market, unitPriceMinor, taxRateBp, qty,
            paymentProviderId: null,
            paymentProviderTxnId: null);

    private static async Task<(Order Order, OrderLine Line)> SeedDeliveredOrderAsync(
        ReturnsTestFactory factory,
        Guid accountId,
        string market,
        long unitPriceMinor,
        int taxRateBp,
        int qty,
        string? paymentProviderId,
        string? paymentProviderTxnId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        var subtotal = unitPriceMinor * qty;
        var tax = subtotal * taxRateBp / 10_000;
        var grand = subtotal + tax;

        var productId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        // Inventory pre-setup so PostReturnAsync can find a sale movement to reverse.
        inventoryDb.Warehouses.Add(new Warehouse
        {
            Id = warehouseId, MarketCode = market, Code = $"W-{warehouseId:N}", DisplayName = "Test WH",
            IsActive = true,
        });
        inventoryDb.StockLevels.Add(new StockLevel
        {
            ProductId = productId, WarehouseId = warehouseId,
            OnHand = 100, Reserved = 0, SafetyStock = 0, ReorderThreshold = 0,
            BucketCache = "in_stock", UpdatedAt = nowUtc,
        });
        inventoryDb.InventoryBatches.Add(new InventoryBatch
        {
            Id = batchId, ProductId = productId, WarehouseId = warehouseId, MarketCode = market,
            LotNo = $"LOT-{batchId:N}", ExpiryDate = DateOnly.FromDateTime(nowUtc.UtcDateTime.AddYears(2)),
            QtyOnHand = 100, Status = "active", ReceivedAt = nowUtc, Notes = "test",
        });
        await inventoryDb.SaveChangesAsync();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"ORD-{market}-{nowUtc:yyyyMM}-{Random.Shared.Next(100000, 999999):D6}",
            AccountId = accountId,
            MarketCode = market,
            Currency = market == "EG" ? "EGP" : "SAR",
            SubtotalMinor = subtotal,
            DiscountMinor = 0,
            TaxMinor = tax,
            ShippingMinor = 0,
            GrandTotalMinor = grand,
            PriceExplanationId = Guid.NewGuid(),
            ShippingAddressJson = "{}",
            BillingAddressJson = "{}",
            OrderState = OrderSm.Placed,
            PaymentState = PaymentSm.Captured,
            FulfillmentState = FulfillmentSm.Delivered,
            RefundState = RefundSm.None,
            PlacedAt = nowUtc.AddDays(-5),
            DeliveredAt = nowUtc.AddDays(-3),
            PaymentProviderId = paymentProviderId,
            PaymentProviderTxnId = paymentProviderTxnId,
            CreatedAt = nowUtc.AddDays(-5),
            UpdatedAt = nowUtc,
        };
        order.Lines.Add(new OrderLine
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = productId,
            Sku = $"SKU-{Guid.NewGuid():N}",
            NameAr = "اختبار",
            NameEn = "Test",
            Qty = qty,
            UnitPriceMinor = unitPriceMinor,
            LineDiscountMinor = 0,
            LineTaxMinor = tax,
            LineTotalMinor = grand,
            Restricted = false,
            AttributesJson = "{}",
        });
        ordersDb.Orders.Add(order);
        await ordersDb.SaveChangesAsync();

        // Sale movement so spec 008 PostReturnAsync can reverse.
        inventoryDb.InventoryMovements.Add(new InventoryMovement
        {
            ProductId = productId, WarehouseId = warehouseId, MarketCode = market,
            BatchId = batchId, Kind = "sale", Delta = -qty, Reason = "test.sale",
            SourceKind = "order", SourceId = order.Id, OccurredAt = nowUtc.AddDays(-3),
        });
        // CR Major: mirror the sale on BOTH StockLevel AND InventoryBatch so return-path
        // tests start from a consistent ledger (no lot-level desync hiding bugs).
        var stock = await inventoryDb.StockLevels.FirstAsync(s =>
            s.ProductId == productId && s.WarehouseId == warehouseId);
        stock.OnHand -= qty;
        var batch = await inventoryDb.InventoryBatches.FirstAsync(b => b.Id == batchId);
        batch.QtyOnHand -= qty;
        await inventoryDb.SaveChangesAsync();

        return (order, order.Lines[0]);
    }
}
