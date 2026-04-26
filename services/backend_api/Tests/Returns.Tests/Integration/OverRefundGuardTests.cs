using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Returns.Tests.Infrastructure;

namespace Returns.Tests.Integration;

[Collection("returns-fixture")]
public class OverRefundGuardTests(ReturnsTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SC_006_force_refund_then_second_RMA_blocked()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (adminToken, _) = await ReturnsAuthHelper.IssueAdminTokenAsync(factory, new[]
        {
            "returns.review.write", "returns.refund.write",
        });
        // qty=2 so we can submit two distinct RMAs back-to-back, the second one should
        // exceed the captured total.
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCapturedOrderAsync(factory, custId, qty: 2);
        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var admin = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(admin, adminToken);

        // First RMA — qty 2. Force-refund + issue-refund refunds the WHOLE captured total.
        var first = await customer.PostAsJsonAsync(
            $"/v1/customer/orders/{order.Id}/returns",
            new
            {
                lines = new[] { new { orderLineId = line.Id, qty = 2, lineReasonCode = "defective" } },
                reasonCode = "defective",
            });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstId = JsonDocument.Parse(await first.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();
        var force = await admin.PostAsJsonAsync($"/v1/admin/returns/{firstId}/force-refund",
            new { reasonCode = "goodwill" });
        force.StatusCode.Should().Be(HttpStatusCode.OK);
        var refund = await admin.PostAsJsonAsync($"/v1/admin/returns/{firstId}/issue-refund", new { });
        var refundBody = await refund.Content.ReadAsStringAsync();
        refund.StatusCode.Should().Be(HttpStatusCode.OK, $"first refund body: {refundBody}");

        // Second RMA — same line, qty 1. Should be blocked at submit (no available qty)
        // because requested qty 2 already consumed all 2 units. Submit returns 400.
        var second = await customer.PostAsJsonAsync(
            $"/v1/customer/orders/{order.Id}/returns",
            new
            {
                lines = new[] { new { orderLineId = line.Id, qty = 1, lineReasonCode = "defective" } },
                reasonCode = "defective",
            });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var secondBody = await second.Content.ReadAsStringAsync();
        secondBody.Should().Contain("qty_exceeds_delivered");
    }
}
