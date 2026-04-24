using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Inventory.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests.Contract.Admin;

[Collection("inventory-fixture")]
public sealed class AdjustContractTests(InventoryTestFactory factory)
{
    [Fact]
    public async Task Adjust_NegativeThatWouldGoSubZero_Rejected()
    {
        await factory.ResetDatabaseAsync();

        await using var seedScope = factory.Services.CreateAsyncScope();
        var productId = await InventoryTestSeedHelper.CreatePublishedProductAsync(seedScope.ServiceProvider, "SKU-ADJ-001", ["ksa"]);
        var warehouseId = await InventoryTestSeedHelper.EnsureWarehouseAsync(seedScope.ServiceProvider, "ksa-main", "ksa");
        await InventoryTestSeedHelper.UpsertStockAsync(seedScope.ServiceProvider, productId, warehouseId, onHand: 1, reserved: 0, safetyStock: 0, bucketCache: "in_stock");

        var (token, _) = await InventoryAdminAuthHelper.IssueAdminTokenAsync(factory, ["inventory.movement.write", "inventory.movement.read"]);
        var client = factory.CreateClient();
        InventoryAdminAuthHelper.SetBearer(client, token);

        var response = await client.PostAsJsonAsync("/v1/admin/inventory/movements/adjust", new
        {
            productId,
            warehouseId,
            delta = -2,
            reason = "physical-count-miss"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        problem.Should().NotBeNull();
        problem!.ReasonCode.Should().Be("inventory.negative_on_hand_blocked");
    }

    private sealed record ProblemResponse(string ReasonCode);
}
