using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Inventory.Persistence;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Integration;

[Collection("inventory-fixture")]
public sealed class TtlReleaseTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task TtlExpiry_WorkerReleasesWithin1Min()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-TTL-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        _ = await InventoryTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-TTL", new DateOnly(2028, 6, 30), 5);
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 5, reserved: 0, safetyStock: 0, bucketCache: "in_stock");

        var (token, accountId) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.internal.reserve"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var create = await client.PostAsJsonAsync("/v1/internal/inventory/reservations", new
        {
            cartId = Guid.NewGuid(),
            accountId,
            marketCode = "ksa",
            items = new[]
            {
                new { productId, qty = 3 }
            }
        });

        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await create.Content.ReadFromJsonAsync<CreateReservationResponse>();
        body.Should().NotBeNull();

        await using (var forceExpireScope = factory.Services.CreateAsyncScope())
        {
            var db = forceExpireScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var reservation = await db.InventoryReservations.SingleAsync(x => x.Id == body!.ReservationId);
            reservation.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-10);
            await db.SaveChangesAsync();
        }

        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(55);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            await using var assertScope = factory.Services.CreateAsyncScope();
            var db = assertScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var reservation = await db.InventoryReservations.AsNoTracking().SingleAsync(x => x.Id == body!.ReservationId);
            var stock = await db.StockLevels.AsNoTracking().SingleAsync(x => x.ProductId == productId && x.WarehouseId == warehouseId);

            if (reservation.Status == "released" && stock.Reserved == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new Xunit.Sdk.XunitException("Reservation was not auto-released within 1 minute.");
    }

    private sealed record CreateReservationResponse(Guid ReservationId);
}
