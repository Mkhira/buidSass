using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using Microsoft.Extensions.DependencyInjection;

namespace TaxInvoices.Tests.Infrastructure;

public static class InvoicesTestSeed
{
    public static async Task<Order> SeedCapturedOrderAsync(
        InvoicesTestFactory factory,
        Guid accountId,
        string market = "KSA",
        long grandTotalMinor = 115_00)
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
            Currency = market == "EG" ? "EGP" : "SAR",
            SubtotalMinor = grandTotalMinor - 15_00,
            DiscountMinor = 0,
            TaxMinor = 15_00,
            ShippingMinor = 0,
            GrandTotalMinor = grandTotalMinor,
            PriceExplanationId = Guid.NewGuid(),
            ShippingAddressJson = "{}",
            BillingAddressJson = "{}",
            OrderState = OrderSm.Placed,
            PaymentState = PaymentSm.Captured,
            FulfillmentState = FulfillmentSm.NotStarted,
            RefundState = RefundSm.None,
            PlacedAt = nowUtc,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        order.Lines.Add(new OrderLine
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = Guid.NewGuid(),
            Sku = "TEST-INV",
            NameAr = "اختبار",
            NameEn = "Test",
            Qty = 1,
            UnitPriceMinor = grandTotalMinor - 15_00,
            LineDiscountMinor = 0,
            LineTaxMinor = 15_00,
            LineTotalMinor = grandTotalMinor,
            AttributesJson = "{}",
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    public static async Task<Guid> SeedAccountAsync(InvoicesTestFactory factory, string market = "ksa")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Identity.Persistence.IdentityDbContext>();
        var accountId = Guid.NewGuid();
        db.Accounts.Add(new BackendApi.Modules.Identity.Entities.Account
        {
            Id = accountId,
            Surface = "customer",
            MarketCode = market,
            EmailNormalized = $"inv-{accountId:N}@example.test",
            EmailDisplay = $"inv-{accountId:N}@example.test",
            PasswordHash = "x",
            PasswordHashVersion = 1,
            PermissionVersion = 1,
            Status = "active",
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            Locale = "en",
            DisplayName = "Invoice Tester",
            ProfessionalVerificationStatus = "unverified",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return accountId;
    }
}
