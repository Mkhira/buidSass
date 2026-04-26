using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Returns.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Returns.Tests.Infrastructure;

namespace Returns.Tests.Integration;

/// <summary>
/// SC-007 / J7. Replaying the inspect call (e.g. duplicate admin click) MUST NOT post a second
/// inventory return movement. The endpoint short-circuits via <c>AdminMutation.WasAlreadyApplied</c>
/// keyed on (return_id, "admin.inspect", payload-hash) so the second call is a 200 dedupe.
/// </summary>
[Collection("returns-fixture")]
public class InspectionIdempotencyTests(ReturnsTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Replayed_inspection_posts_zero_duplicate_inventory_movements()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (adminToken, _) = await ReturnsAuthHelper.IssueAdminTokenAsync(factory, new[]
        {
            "returns.review.write", "returns.warehouse.write",
        });
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCapturedOrderAsync(factory, custId, qty: 3);
        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var admin = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(admin, adminToken);

        var submit = await customer.PostAsJsonAsync($"/v1/customer/orders/{order.Id}/returns",
            new
            {
                lines = new[] { new { orderLineId = line.Id, qty = 3, lineReasonCode = "defective" } },
                reasonCode = "defective",
            });
        var returnId = JsonDocument.Parse(await submit.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();
        (await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/approve", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        Guid lineId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
            lineId = await db.ReturnLines.Where(l => l.ReturnRequestId == returnId).Select(l => l.Id).FirstAsync();
        }
        (await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/mark-received",
            new { lines = new[] { new { returnLineId = lineId, receivedQty = 3 } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Snapshot inventory return-movement count BEFORE inspect.
        long beforeReturnMovementCount;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var inv = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            beforeReturnMovementCount = await inv.InventoryMovements
                .Where(m => m.SourceKind == "return" && m.SourceId == order.Id && m.Kind == "return")
                .CountAsync();
        }

        // First inspect — sellable=2, defective=1 (so exactly ONE return movement gets posted
        // for the sellable units against this batch).
        var inspectBody = new
        {
            lines = new[] { new { returnLineId = lineId, sellableQty = 2, defectiveQty = 1 } },
        };
        var first = await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/inspect", inspectBody);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        firstDoc.GetProperty("state").GetString().Should().Be("inspected");

        // Replay the same inspect 4 more times. Each replay should be a dedup short-circuit
        // — a 200 OK with `deduped: true` and ZERO additional inventory movements.
        for (int i = 0; i < 4; i++)
        {
            var replay = await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/inspect", inspectBody);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            var replayDoc = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()).RootElement;
            replayDoc.TryGetProperty("deduped", out var dedupEl).Should().BeTrue($"replay #{i + 1}");
            dedupEl.GetBoolean().Should().BeTrue();
        }

        // After 1 + 4 calls, exactly ONE return movement should have been posted.
        long afterReturnMovementCount;
        int inspectionRowCount;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var inv = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var db = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
            afterReturnMovementCount = await inv.InventoryMovements
                .Where(m => m.SourceKind == "return" && m.SourceId == order.Id && m.Kind == "return")
                .CountAsync();
            inspectionRowCount = await db.Inspections.Where(i => i.ReturnRequestId == returnId).CountAsync();
        }
        (afterReturnMovementCount - beforeReturnMovementCount).Should().Be(1,
            "five total inspect calls (1 real + 4 replays) must produce exactly one inventory return movement");
        inspectionRowCount.Should().Be(1, "only one Inspection row should be persisted across replays");
    }
}
