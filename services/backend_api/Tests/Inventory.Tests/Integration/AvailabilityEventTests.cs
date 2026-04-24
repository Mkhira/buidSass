using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Inventory.Primitives;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Integration;

[Collection("inventory-fixture")]
public sealed class AvailabilityEventTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task BucketChange_EmitsAvailabilityEvent_Under10s()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var emitter = seedScope.ServiceProvider.GetRequiredService<AvailabilityEventEmitter>();
        emitter.Clear();

        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-AVAIL-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        _ = await InventoryTestSeedHelper.AddBatchAsync(seedScope.ServiceProvider, productId, warehouseId, "LOT-AVAIL", new DateOnly(2028, 6, 30), 1);
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 1, reserved: 0, safetyStock: 0, bucketCache: "in_stock");

        var (token, accountId) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.internal.reserve"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var reserve = await client.PostAsJsonAsync("/v1/internal/inventory/reservations", new
        {
            cartId = Guid.NewGuid(),
            accountId,
            marketCode = "ksa",
            items = new[]
            {
                new { productId, qty = 1 }
            }
        });
        reserve.StatusCode.Should().Be(HttpStatusCode.OK);

        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            var events = emitter.Snapshot();
            var availabilityEvent = events.LastOrDefault(e => e.ProductId == productId && e.WarehouseId == warehouseId);
            if (availabilityEvent is not null)
            {
                availabilityEvent.OldBucket.Should().Be("in_stock");
                availabilityEvent.NewBucket.Should().Be("out_of_stock");
                return;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException("Availability event was not observed within 10 seconds.");
    }
}
