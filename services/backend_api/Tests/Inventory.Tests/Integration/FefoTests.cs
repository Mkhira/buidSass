using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Integration;

[Collection("inventory-fixture")]
public sealed class FefoTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task Fefo_PicksNearestExpiryFirst()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-FEFO-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 0, reserved: 0, safetyStock: 0, bucketCache: "out_of_stock");

        var (token, accountId) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.batch.write", "inventory.internal.reserve"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var later = await client.PostAsJsonAsync("/v1/admin/inventory/batches", new
        {
            productId,
            warehouseId,
            lotNo = "LOT-LATE",
            expiryDate = "2028-06-30",
            qty = 5,
            notes = "late batch"
        });
        later.StatusCode.Should().Be(HttpStatusCode.Created);
        var laterBody = await later.Content.ReadFromJsonAsync<BatchCreateResponse>();

        var earlier = await client.PostAsJsonAsync("/v1/admin/inventory/batches", new
        {
            productId,
            warehouseId,
            lotNo = "LOT-EARLY",
            expiryDate = "2027-01-15",
            qty = 5,
            notes = "early batch"
        });
        earlier.StatusCode.Should().Be(HttpStatusCode.Created);
        var earlyBody = await earlier.Content.ReadFromJsonAsync<BatchCreateResponse>();

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
        var reservationBody = await reserve.Content.ReadFromJsonAsync<CreateReservationResponse>();

        reservationBody.Should().NotBeNull();
        reservationBody!.Items.Should().HaveCount(1);
        reservationBody.Items[0].PickedBatchId.Should().Be(earlyBody!.Id);
        reservationBody.Items[0].PickedBatchId.Should().NotBe(laterBody!.Id);
    }

    private sealed record BatchCreateResponse(Guid Id);
    private sealed record CreateReservationResponse(Guid ReservationId, List<CreateReservationItemResponse> Items);
    private sealed record CreateReservationItemResponse(Guid ProductId, int Qty, Guid PickedBatchId, DateTimeOffset ExpiresAt);
}
