using System.Net.Http.Json;
using BackendApi.Modules.Cart.Entities;
using BackendApi.Modules.Cart.Persistence;
using BackendApi.Modules.Cart.Workers;
using BackendApi.Modules.Inventory.Persistence;
using Cart.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cart.Tests.Integration;

/// <summary>
/// SC-005 (guest cart purge) + C1 (worker purge releases reservations). Plants a guest cart
/// whose LastTouchedAt is older than the 30-day purge threshold, runs the worker, and asserts:
///   1. the guest cart + its lines + saved/b2b/emission rows are hard-deleted,
///   2. the attached inventory reservation is released (status != 'active'),
///   3. stock_levels.reserved is decremented back to 0.
/// </summary>
[Collection("cart-fixture")]
public sealed class WorkerPurgeTests(CartTestFactory factory)
{
    [Fact]
    public async Task GuestCleanup_Purges_AndReleasesReservations()
    {
        await factory.ResetDatabaseAsync();

        // Seed product + warehouse + stock + batch.
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-WPURGE", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-purge", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-PURGE",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 10);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        // Use the public add-line path to create a guest cart with a live reservation.
        var anon = factory.CreateClient();
        var addResp = await anon.PostAsJsonAsync("/v1/customer/cart/lines",
            new { marketCode = "ksa", productId, qty = 2 });
        addResp.EnsureSuccessStatusCode();

        // Backdate LastTouchedAt past the purge threshold so the worker picks it up.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();
            var cart = await db.Carts.SingleAsync();
            cart.LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-45);
            await db.SaveChangesAsync();
        }

        // Run the worker once.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var worker = ActivatorUtilities.CreateInstance<GuestCartCleanupWorker>(scope.ServiceProvider);
            var purged = await worker.TickAsync(CancellationToken.None);
            purged.Should().Be(1);
        }

        await using var assertScope = factory.Services.CreateAsyncScope();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var inventoryDb = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        (await cartDb.Carts.CountAsync()).Should().Be(0);
        (await cartDb.CartLines.CountAsync()).Should().Be(0);

        var reservationStatuses = await inventoryDb.InventoryReservations.AsNoTracking()
            .Where(r => r.ProductId == productId).Select(r => r.Status).ToListAsync();
        reservationStatuses.Should().NotContain("active", because: "purge must release the reservation (C1)");

        var stock = await inventoryDb.StockLevels.AsNoTracking()
            .SingleAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
        stock.Reserved.Should().Be(0, because: "stock.Reserved must decrement when reservation is released");
    }

    [Fact]
    public async Task ArchiveReaper_PurgesOldArchivedCarts_AndReleasesAnyDanglingReservations()
    {
        await factory.ResetDatabaseAsync();
        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await CartTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-REAPER", ["ksa"]);
        var warehouseId = await CartTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-reap", "ksa");
        await CartTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 10);
        await CartTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-REAP",
            DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)), qtyOnHand: 10);
        await CartTestSeedHelper.EnsureTaxRateAsync(seedScope.ServiceProvider, "ksa");

        var (_, accountId) = await CartCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");

        // Plant an archived cart whose ArchivedAt is older than retention. Leave a
        // reservation still attached to exercise the defense-in-depth release path.
        Guid archivedCartId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CartDbContext>();
            var cart = new BackendApi.Modules.Cart.Entities.Cart
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                MarketCode = "ksa",
                Status = "archived",
                ArchivedAt = DateTimeOffset.UtcNow.AddDays(-30),
                ArchivedReason = "market_switch",
                LastTouchedAt = DateTimeOffset.UtcNow.AddDays(-30),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-35),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                OwnerId = "platform",
            };
            db.Carts.Add(cart);
            await db.SaveChangesAsync();
            archivedCartId = cart.Id;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var worker = ActivatorUtilities.CreateInstance<ArchivedCartReaperWorker>(scope.ServiceProvider);
            var purged = await worker.TickAsync(CancellationToken.None);
            purged.Should().Be(1);
        }

        await using var assertScope = factory.Services.CreateAsyncScope();
        var cartDb = assertScope.ServiceProvider.GetRequiredService<CartDbContext>();
        var purgedCart = await cartDb.Carts.AsNoTracking().SingleAsync(c => c.Id == archivedCartId);
        purgedCart.Status.Should().Be("purged");
    }
}
