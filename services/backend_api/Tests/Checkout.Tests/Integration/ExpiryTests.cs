using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Checkout.Persistence;
using BackendApi.Modules.Checkout.Primitives;
using BackendApi.Modules.Checkout.Workers;
using BackendApi.Modules.Inventory.Persistence;
using Checkout.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Tests.Integration;

/// <summary>SC-006 — expiry worker releases reservations from idle sessions.</summary>
[Collection("checkout-fixture")]
public sealed class ExpiryTests(CheckoutTestFactory factory)
{
    [Fact]
    public async Task Session_Idle35Min_Expires_ReleasesReservations()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await CheckoutCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CheckoutTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-EXP", ["ksa"]);
        var warehouseId = await CheckoutTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-exp", "ksa");
        await CheckoutTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, 10);
        await CheckoutTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-EXP", DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), 10);
        await CheckoutTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");
        var cartId = await CheckoutTestSeedHelper.SeedReadyCartAsync(seedScope.ServiceProvider, accountId, "ksa", productId, qty: 3);

        // Plant a session with an already-elapsed expiry.
        Guid sessionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
            var expiredSession = new BackendApi.Modules.Checkout.Entities.CheckoutSession
            {
                Id = Guid.NewGuid(),
                CartId = cartId,
                AccountId = accountId,
                MarketCode = "ksa",
                State = CheckoutStates.PaymentSelected,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastTouchedAt = DateTimeOffset.UtcNow.AddHours(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                PaymentMethod = "card",
                ShippingFeeMinor = 2500,
                ShippingProviderId = "stub",
                ShippingMethodCode = "standard",
            };
            db.Sessions.Add(expiredSession);
            await db.SaveChangesAsync();
            sessionId = expiredSession.Id;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var worker = ActivatorUtilities.CreateInstance<CheckoutExpiryWorker>(scope.ServiceProvider);
            var count = await worker.TickAsync(CancellationToken.None);
            count.Should().BeGreaterThan(0);
        }

        await using var assertScope = factory.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        var session = await assertDb.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        session.State.Should().Be(CheckoutStates.Expired);

        // The seeded cart line's reservation should no longer be active.
        var inventoryDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var reservationId = await cartDb.CartLines.AsNoTracking()
            .Where(l => l.CartId == cartId).Select(l => l.ReservationId).SingleAsync();
        if (reservationId is { } rid)
        {
            var reservation = await inventoryDb.InventoryReservations.AsNoTracking().SingleAsync(r => r.Id == rid);
            reservation.Status.Should().NotBe("active",
                because: "expiry worker must release the reservation");
        }
    }
}
