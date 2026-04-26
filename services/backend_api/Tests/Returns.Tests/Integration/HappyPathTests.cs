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
public class HappyPathTests(ReturnsTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Submit_approve_receive_inspect_refund_full_path()
    {
        // Customer + admin tokens; delivered captured order.
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (adminToken, _) = await ReturnsAuthHelper.IssueAdminTokenAsync(factory, new[]
        {
            "returns.read", "returns.review.write", "returns.warehouse.write", "returns.refund.write",
        });
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCapturedOrderAsync(factory, custId);

        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var admin = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(admin, adminToken);

        // 1. Submit return.
        var submit = await customer.PostAsJsonAsync(
            $"/v1/customer/orders/{order.Id}/returns",
            new
            {
                lines = new[] { new { orderLineId = line.Id, qty = 1, lineReasonCode = "defective" } },
                reasonCode = "defective",
                customerNotes = "broken on arrival",
                photoIds = Array.Empty<Guid>(),
            });
        submit.StatusCode.Should().Be(HttpStatusCode.Created);
        var submitDoc = JsonDocument.Parse(await submit.Content.ReadAsStringAsync()).RootElement;
        var returnId = submitDoc.GetProperty("id").GetGuid();
        submitDoc.GetProperty("returnNumber").GetString().Should().StartWith("RET-KSA-");
        submitDoc.GetProperty("state").GetString().Should().Be("pending_review");

        // 2. Admin approve.
        var approve = await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/approve",
            new { adminNotes = "ok" });
        approve.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Admin mark-received.
        var rec = await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/mark-received",
            new
            {
                lines = new[] { new { returnLineId = await GetReturnLineIdAsync(returnId), receivedQty = 1 } },
            });
        rec.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Inspect (1 sellable, 0 defective).
        var insp = await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/inspect",
            new
            {
                lines = new[] { new { returnLineId = await GetReturnLineIdAsync(returnId), sellableQty = 1, defectiveQty = 0 } },
            });
        insp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Issue refund.
        var refund = await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/issue-refund",
            new { restockingFeeMinor = (long?)null });
        var refundBody = await refund.Content.ReadAsStringAsync();
        refund.StatusCode.Should().Be(HttpStatusCode.OK,
            $"refund body: {refundBody}");
        var refundDoc = JsonDocument.Parse(refundBody).RootElement;
        refundDoc.GetProperty("state").GetString().Should().Be("completed");
        refundDoc.GetProperty("amountMinor").GetInt64().Should().Be(115_00);

        // Verify return is now refunded.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
        var rr = await db.ReturnRequests.FirstAsync(r => r.Id == returnId);
        rr.State.Should().Be("refunded");
    }

    private async Task<Guid> GetReturnLineIdAsync(Guid returnId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
        return await db.ReturnLines.Where(l => l.ReturnRequestId == returnId).Select(l => l.Id).FirstAsync();
    }
}
