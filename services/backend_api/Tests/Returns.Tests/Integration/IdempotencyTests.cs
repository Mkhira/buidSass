using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Returns.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Returns.Tests.Infrastructure;

namespace Returns.Tests.Integration;

[Collection("returns-fixture")]
public class IdempotencyTests(ReturnsTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SC_005_duplicate_approve_yields_one_state_mutation()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (adminToken, _) = await ReturnsAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "returns.review.write" });
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCapturedOrderAsync(factory, custId);
        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var admin = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(admin, adminToken);

        var submit = await customer.PostAsJsonAsync(
            $"/v1/customer/orders/{order.Id}/returns",
            new
            {
                lines = new[] { new { orderLineId = line.Id, qty = 1, lineReasonCode = "defective" } },
                reasonCode = "defective",
            });
        var returnId = JsonDocument.Parse(await submit.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        // Click approve 5 times — only ONE state-transition row should exist.
        for (int i = 0; i < 5; i++)
        {
            var resp = await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/approve", new { });
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
        var transitions = await db.StateTransitions
            .Where(t => t.ReturnRequestId == returnId && t.Trigger == "admin.approve")
            .CountAsync();
        transitions.Should().Be(1);
    }
}
