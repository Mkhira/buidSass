using BackendApi.Modules.Catalog.Entities;
using BackendApi.Modules.Catalog.Persistence;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.Extensions.DependencyInjection;

namespace Orders.Tests.Infrastructure;

/// <summary>
/// Lightweight seeding for integration tests — directly inserts catalog products + orders
/// rather than driving the full cart→checkout→submit→handler chain. The few tests that
/// MUST exercise that chain end-to-end use the real DI handlers via service-locator.
/// </summary>
public static class OrdersTestSeed
{
    public static async Task<Guid> SeedProductAsync(OrdersTestFactory factory, string sku = "TEST-SKU-001",
        bool restricted = false, string nameEn = "Test Product")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var brandId = Guid.NewGuid();
        catalog.Brands.Add(new Brand
        {
            Id = brandId,
            Slug = $"brand-{Guid.NewGuid():N}",
            NameAr = "تجريبي",
            NameEn = "Test Brand",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var productId = Guid.NewGuid();
        catalog.Products.Add(new Product
        {
            Id = productId,
            Sku = sku,
            BrandId = brandId,
            SlugAr = $"slug-ar-{Guid.NewGuid():N}",
            SlugEn = $"slug-en-{Guid.NewGuid():N}",
            NameAr = nameEn + " (AR)",
            NameEn = nameEn,
            AttributesJson = "{}",
            MarketCodes = new[] { "ksa", "eg" },
            Status = "active",
            Restricted = restricted,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await catalog.SaveChangesAsync();
        return productId;
    }

    public static async Task<Order> SeedOrderAsync(
        OrdersTestFactory factory,
        Guid accountId,
        string market = "KSA",
        string paymentState = "captured",
        string fulfillmentState = "not_started",
        DateTimeOffset? deliveredAt = null,
        long grandTotalMinor = 100_00,
        string? paymentProviderId = null,
        string? paymentProviderTxnId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"ORD-{market}-{nowUtc:yyyyMM}-{Random.Shared.Next(100000, 999999):D6}",
            AccountId = accountId,
            MarketCode = market,
            Currency = "SAR",
            SubtotalMinor = grandTotalMinor,
            GrandTotalMinor = grandTotalMinor,
            PriceExplanationId = Guid.NewGuid(),
            ShippingAddressJson = "{}",
            BillingAddressJson = "{}",
            OrderState = OrderSm.Placed,
            PaymentState = paymentState,
            FulfillmentState = fulfillmentState,
            RefundState = RefundSm.None,
            PlacedAt = nowUtc,
            DeliveredAt = deliveredAt,
            PaymentProviderId = paymentProviderId,
            PaymentProviderTxnId = paymentProviderTxnId,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        order.Lines.Add(new OrderLine
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = Guid.NewGuid(),
            Sku = "TEST",
            NameAr = "اختبار",
            NameEn = "Test",
            Qty = 1,
            UnitPriceMinor = grandTotalMinor,
            LineTotalMinor = grandTotalMinor,
            AttributesJson = "{}",
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }
}
