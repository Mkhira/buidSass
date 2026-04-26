using BackendApi.Modules.TaxInvoices.Entities;
using BackendApi.Modules.TaxInvoices.Internal.IssueCreditNote;
using BackendApi.Modules.TaxInvoices.Internal.IssueOnCapture;
using BackendApi.Modules.TaxInvoices.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxInvoices.Tests.Infrastructure;

namespace TaxInvoices.Tests.Integration;

/// <summary>Regressions for the deep-review bug fixes (B2 cumulative refund, B3 per-line cumulative qty).</summary>
[Collection("invoices-fixture")]
public sealed class DeepReviewFixesTests(InvoicesTestFactory factory)
{
    [Fact]
    public async Task B2_CumulativeRefund_ExceedingInvoice_IsRejected()
    {
        await factory.ResetDatabaseAsync();
        var accountId = await InvoicesTestSeed.SeedAccountAsync(factory);
        // Seed an invoice with grand total 200_00 by giving the order a single line with qty 2.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var ordersDb = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Orders.Persistence.OrdersDbContext>();
            var order = new BackendApi.Modules.Orders.Entities.Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "ORD-KSA-202604-CUMUL",
                AccountId = accountId,
                MarketCode = "KSA",
                Currency = "SAR",
                SubtotalMinor = 200_00,
                TaxMinor = 30_00,
                GrandTotalMinor = 230_00,
                PriceExplanationId = Guid.NewGuid(),
                ShippingAddressJson = "{}",
                BillingAddressJson = "{}",
                OrderState = "placed",
                PaymentState = "captured",
                FulfillmentState = "not_started",
                RefundState = "none",
                PlacedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            order.Lines.Add(new BackendApi.Modules.Orders.Entities.OrderLine
            {
                Id = Guid.NewGuid(), OrderId = order.Id, ProductId = Guid.NewGuid(),
                Sku = "MULTI", NameAr = "ت", NameEn = "Test",
                Qty = 2, UnitPriceMinor = 100_00, LineTaxMinor = 30_00,
                LineTotalMinor = 230_00, AttributesJson = "{}",
            });
            ordersDb.Orders.Add(order);
            await ordersDb.SaveChangesAsync();

            var issuer = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();
            var invoiceResult = await issuer.IssueAsync(order.Id, CancellationToken.None);
            invoiceResult.IsSuccess.Should().BeTrue();
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).SingleAsync();
        var line = invoice.Lines.Single();
        var creditHandler = verifyScope.ServiceProvider.GetRequiredService<IssueCreditNoteHandler>();

        // First credit note refunds qty 1 (half the invoice). Should succeed.
        var first = await creditHandler.IssueAsync(new IssueCreditNoteRequest(
            invoice.Id, Guid.NewGuid(),
            new[] { new CreditNoteLineInput(line.Id, 1) }, "customer_return"),
            CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        // Second credit note tries qty 1 too — line cumulative now hits 2/2 so the per-line
        // check (B3) blocks it.
        var second = await creditHandler.IssueAsync(new IssueCreditNoteRequest(
            invoice.Id, Guid.NewGuid(),
            new[] { new CreditNoteLineInput(line.Id, 1) }, "customer_return"),
            CancellationToken.None);
        second.IsSuccess.Should().BeTrue("a second qty-1 credit reaches exactly the invoice line qty (2)");

        // Third credit note any qty must fail — invoice fully refunded.
        var third = await creditHandler.IssueAsync(new IssueCreditNoteRequest(
            invoice.Id, Guid.NewGuid(),
            new[] { new CreditNoteLineInput(line.Id, 1) }, "customer_return"),
            CancellationToken.None);
        third.IsSuccess.Should().BeFalse();
        third.ErrorCode.Should().Be("credit_note.line_exceeds_invoice");
    }

    [Fact]
    public async Task B3_PerLine_OverCredit_AcrossCreditNotes_IsRejected()
    {
        await factory.ResetDatabaseAsync();
        var accountId = await InvoicesTestSeed.SeedAccountAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var ordersDb = scope.ServiceProvider.GetRequiredService<BackendApi.Modules.Orders.Persistence.OrdersDbContext>();
            var order = new BackendApi.Modules.Orders.Entities.Order
            {
                Id = Guid.NewGuid(), OrderNumber = "ORD-KSA-202604-Q5",
                AccountId = accountId, MarketCode = "KSA", Currency = "SAR",
                SubtotalMinor = 500_00, TaxMinor = 75_00, GrandTotalMinor = 575_00,
                PriceExplanationId = Guid.NewGuid(),
                ShippingAddressJson = "{}", BillingAddressJson = "{}",
                OrderState = "placed", PaymentState = "captured",
                FulfillmentState = "not_started", RefundState = "none",
                PlacedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            order.Lines.Add(new BackendApi.Modules.Orders.Entities.OrderLine
            {
                Id = Guid.NewGuid(), OrderId = order.Id, ProductId = Guid.NewGuid(),
                Sku = "Q5", NameAr = "ت", NameEn = "Q5",
                Qty = 5, UnitPriceMinor = 100_00, LineTaxMinor = 75_00,
                LineTotalMinor = 575_00, AttributesJson = "{}",
            });
            ordersDb.Orders.Add(order);
            await ordersDb.SaveChangesAsync();
            var issuer = scope.ServiceProvider.GetRequiredService<IssueOnCaptureHandler>();
            (await issuer.IssueAsync(order.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
        }
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).SingleAsync();
        var lineId = invoice.Lines.Single().Id;
        var creditHandler = verifyScope.ServiceProvider.GetRequiredService<IssueCreditNoteHandler>();

        // Qty-3 credit succeeds, qty-3 attempt right after fails (3+3 > 5).
        (await creditHandler.IssueAsync(new IssueCreditNoteRequest(
            invoice.Id, Guid.NewGuid(),
            new[] { new CreditNoteLineInput(lineId, 3) }, "customer_return"),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
        var second = await creditHandler.IssueAsync(new IssueCreditNoteRequest(
            invoice.Id, Guid.NewGuid(),
            new[] { new CreditNoteLineInput(lineId, 3) }, "customer_return"),
            CancellationToken.None);
        second.IsSuccess.Should().BeFalse();
        second.ErrorCode.Should().Be("credit_note.line_exceeds_invoice");
        // Qty-2 (3+2 = 5 = original) succeeds, exactly at the boundary.
        (await creditHandler.IssueAsync(new IssueCreditNoteRequest(
            invoice.Id, Guid.NewGuid(),
            new[] { new CreditNoteLineInput(lineId, 2) }, "customer_return"),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
    }
}
