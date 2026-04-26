using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Returns.Persistence;
using BackendApi.Modules.Returns.Workers;
using BackendApi.Modules.TaxInvoices.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Returns.Tests.Infrastructure;

namespace Returns.Tests.Integration;

/// <summary>
/// SC-009 / J8. After a refund completes and the outbox dispatcher fires, a credit note must
/// exist in spec 012 whose <c>GrandTotalMinor</c> equals the refund's <c>AmountMinor</c> to 0
/// minor units. Both modules use the IDENTICAL pro-rate formula (deep-review pass 1 fix on
/// <c>RefundAmountCalculator</c>) so reconciliation is exact.
/// </summary>
[Collection("returns-fixture")]
public class CreditNoteReconciliationTests(ReturnsTestFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Refund_amount_equals_credit_note_grand_total()
    {
        var (custToken, custId) = await ReturnsAuthHelper.IssueCustomerTokenAsync(factory);
        var (adminToken, _) = await ReturnsAuthHelper.IssueAdminTokenAsync(factory, new[]
        {
            "returns.review.write", "returns.warehouse.write", "returns.refund.write",
            "invoices.credit_note.issue",
        });
        var (order, line) = await ReturnsTestSeed.SeedDeliveredCapturedOrderAsync(factory, custId,
            unitPriceMinor: 250_00, taxRateBp: 1500, qty: 4);

        // SEED an invoice for the order so spec 012 has something to credit.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var issuer = scope.ServiceProvider.GetRequiredService<
                BackendApi.Modules.TaxInvoices.Internal.IssueOnCapture.IssueOnCaptureHandler>();
            var result = await issuer.IssueAsync(order.Id, CancellationToken.None);
            result.IsSuccess.Should().BeTrue($"invoice issuance failed: {result.ErrorCode} {result.Detail}");
        }

        var customer = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(customer, custToken);
        var admin = factory.CreateClient();
        ReturnsAuthHelper.SetBearer(admin, adminToken);

        // Submit + approve + receive + inspect (sellable=2 of 4) + issue-refund.
        var submit = await customer.PostAsJsonAsync($"/v1/customer/orders/{order.Id}/returns",
            new
            {
                lines = new[] { new { orderLineId = line.Id, qty = 2, lineReasonCode = "defective" } },
                reasonCode = "defective",
            });
        submit.StatusCode.Should().Be(HttpStatusCode.Created);
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
        await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/mark-received",
            new { lines = new[] { new { returnLineId = lineId, receivedQty = 2 } } });
        await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/inspect",
            new { lines = new[] { new { returnLineId = lineId, sellableQty = 2, defectiveQty = 0 } } });

        var refund = await admin.PostAsJsonAsync($"/v1/admin/returns/{returnId}/issue-refund", new { });
        refund.StatusCode.Should().Be(HttpStatusCode.OK,
            $"issue-refund failed: {await refund.Content.ReadAsStringAsync()}");
        var refundDoc = JsonDocument.Parse(await refund.Content.ReadAsStringAsync()).RootElement;
        refundDoc.GetProperty("state").GetString().Should().Be("completed");
        var refundAmount = refundDoc.GetProperty("amountMinor").GetInt64();
        var refundId = refundDoc.GetProperty("id").GetGuid();

        // Drive the outbox dispatcher once. It will:
        //   1. Issue a credit note via spec 012 (idempotent on refundId).
        //   2. Advance spec 011 refund_state.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<ReturnsOutboxDispatchService>();
            // Drain — there can be several queued events (return.submitted, return.approved,
            // return.received, return.inspected, refund.completed). Loop a few ticks until
            // pending == 0 to keep the test robust.
            for (int i = 0; i < 5; i++)
            {
                var n = await dispatcher.DispatchOnceAsync(CancellationToken.None);
                if (n == 0) break;
            }
        }

        // Verify credit note exists and reconciles.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var invDb = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
            var creditNote = await invDb.CreditNotes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.RefundId == refundId);
            creditNote.Should().NotBeNull("dispatcher should have issued a credit note");
            creditNote!.GrandTotalMinor.Should().Be(refundAmount,
                "SC-009: refund_amount must equal |credit_note.grand_total| to 0 minor units");
        }

        // Verify spec 011 was advanced too.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var ordersDb = scope.ServiceProvider.GetRequiredService<
                BackendApi.Modules.Orders.Persistence.OrdersDbContext>();
            var orderRow = await ordersDb.Orders.AsNoTracking()
                .Include(o => o.Lines).FirstAsync(o => o.Id == order.Id);
            orderRow.RefundState.Should().BeOneOf("partial", "full");
            orderRow.Lines.Single(l => l.Id == line.Id).ReturnedQty.Should().Be(2);
        }
    }
}
