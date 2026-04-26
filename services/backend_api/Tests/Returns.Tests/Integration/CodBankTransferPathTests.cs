using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Returns.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Returns.Tests.Infrastructure;

namespace Returns.Tests.Integration;

/// <summary>J4 — COD orders have no captured gateway txn, so IssueRefund must route to the
/// manual-bank-transfer path. Admin then enters IBAN + beneficiary; the refund advances to
/// completed and the return becomes refunded.</summary>
[Collection("returns-fixture")]
public class CodBankTransferPathTests(ReturnsTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task COD_refund_routes_to_pending_manual_then_confirms()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (adminToken, _) = await ReturnsAuthHelper.IssueAdminTokenAsync(factory, new[]
        {
            "returns.review.write", "returns.warehouse.write", "returns.refund.write",
        });
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCodOrderAsync(factory, custId);

        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var admin = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(admin, adminToken);

        // Submit + approve + receive + inspect (sellable=1) so the return is at `inspected`.
        var submit = await customer.PostAsJsonAsync(
            $"/v1/customer/orders/{order.Id}/returns",
            new
            {
                lines = new[] { new { orderLineId = line.Id, qty = 1, lineReasonCode = "defective" } },
                reasonCode = "defective",
            });
        submit.StatusCode.Should().Be(HttpStatusCode.Created);
        var returnId = JsonDocument.Parse(await submit.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/approve", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        Guid returnLineId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
            returnLineId = await db.ReturnLines.Where(l => l.ReturnRequestId == returnId).Select(l => l.Id).FirstAsync();
        }

        (await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/mark-received",
            new { lines = new[] { new { returnLineId, receivedQty = 1 } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/inspect",
            new { lines = new[] { new { returnLineId, sellableQty = 1, defectiveQty = 0 } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Issue-refund: COD → manual path.
        var refund = await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/issue-refund", new { });
        var refundBody = await refund.Content.ReadAsStringAsync();
        refund.StatusCode.Should().Be(HttpStatusCode.OK, $"refund body: {refundBody}");
        var refundDoc = JsonDocument.Parse(refundBody).RootElement;
        refundDoc.GetProperty("state").GetString().Should().Be("pending_manual_transfer");
        refundDoc.GetProperty("manual").GetBoolean().Should().BeTrue();
        var refundId = refundDoc.GetProperty("id").GetGuid();

        // Refund stays in pending_manual_transfer; return state stays at `inspected` until
        // the bank transfer is confirmed.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
            var rrBefore = await db.ReturnRequests.FirstAsync(r => r.Id == returnId);
            rrBefore.State.Should().Be("inspected");
        }

        // Admin confirms the bank transfer → refund=completed, return=refunded.
        var confirm = await admin.PostAsJsonAsync(
            $"/v1/admin/refunds/{refundId}/confirm-bank-transfer",
            new
            {
                iban = "SA0380000000608010167519",
                beneficiaryName = "Returns Customer",
                bankName = "Saudi National Bank",
                reference = "RET-MANUAL-001",
            });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmDoc = JsonDocument.Parse(await confirm.Content.ReadAsStringAsync()).RootElement;
        confirmDoc.GetProperty("state").GetString().Should().Be("completed");
        confirmDoc.GetProperty("returnState").GetString().Should().Be("refunded");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
            var rr = await db.ReturnRequests.FirstAsync(r => r.Id == returnId);
            rr.State.Should().Be("refunded");
            var rf = await db.Refunds.FirstAsync(r => r.Id == refundId);
            rf.State.Should().Be("completed");
            rf.ManualIban.Should().Be("SA0380000000608010167519");
            rf.ManualBeneficiaryName.Should().Be("Returns Customer");
            rf.ManualConfirmedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Confirm_bank_transfer_requires_iban_and_beneficiary()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (adminToken, _) = await ReturnsAuthHelper.IssueAdminTokenAsync(factory, new[]
        {
            "returns.review.write", "returns.warehouse.write", "returns.refund.write",
        });
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCodOrderAsync(factory, custId);
        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var admin = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(admin, adminToken);

        var submit = await customer.PostAsJsonAsync($"/v1/customer/orders/{order.Id}/returns",
            new { lines = new[] { new { orderLineId = line.Id, qty = 1, lineReasonCode = "x" } }, reasonCode = "x" });
        var returnId = JsonDocument.Parse(await submit.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/approve", new { });
        Guid lineId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
            lineId = await db.ReturnLines.Where(l => l.ReturnRequestId == returnId).Select(l => l.Id).FirstAsync();
        }
        await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/mark-received",
            new { lines = new[] { new { returnLineId = lineId, receivedQty = 1 } } });
        await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/inspect",
            new { lines = new[] { new { returnLineId = lineId, sellableQty = 1, defectiveQty = 0 } } });
        var refund = await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/issue-refund", new { });
        var refundId = JsonDocument.Parse(await refund.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        // Missing IBAN → 400 refund.manual_iban.required.
        var bad = await admin.PostAsJsonAsync($"/v1/admin/refunds/{refundId}/confirm-bank-transfer",
            new { iban = "", beneficiaryName = "x" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("refund.manual_iban.required");
    }
}
