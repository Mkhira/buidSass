using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using BackendApi.Modules.Orders.Entities;
using BackendApi.Modules.Orders.Internal.CreateFromQuotation;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// Regression tests for the deep-review bug fixes (B1, B2, B3, B6, B7, B8). B5 has its own
/// dedicated test file (CancelInventoryReleaseTests).
/// </summary>
[Collection("orders-fixture")]
public sealed class DeepReviewFixesTests(OrdersTestFactory factory)
{
    // B1 — quotation conversion uses per-market currency, not hardcoded SAR.
    [Fact]
    public async Task B1_QuotationInEgMarket_GetsEgpCurrency()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory, "eg");
        var quoteId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Quotations.Add(new Quotation
            {
                Id = quoteId,
                QuoteNumber = "QUO-EG-202604-000001",
                AccountId = accountId,
                MarketCode = "EG",
                Status = Quotation.StatusActive,
                PriceExplanationId = Guid.NewGuid(),
                ValidUntil = DateTimeOffset.UtcNow.AddDays(7),
                CreatedByAccountId = accountId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Lines = { new QuotationLine
                {
                    Id = Guid.NewGuid(), QuotationId = quoteId, ProductId = Guid.NewGuid(),
                    Sku = "EG-1", NameAr = "ت", NameEn = "T", Qty = 1,
                    UnitPriceMinor = 100_00, LineTotalMinor = 100_00, AttributesJson = "{}",
                } },
            });
            await db.SaveChangesAsync();
        }
        await using var convertScope = factory.Services.CreateAsyncScope();
        var handler = convertScope.ServiceProvider.GetRequiredService<CreateFromQuotationHandler>();
        var result = await handler.CreateAsync(quoteId, accountId, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        var verifyDb = convertScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var order = await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == result.OrderId);
        order.Currency.Should().Be("EGP");
    }

    // B2 — multi-shipment order: MarkDelivered transitions all shipments, not just the latest.
    [Fact]
    public async Task B2_MarkDelivered_AppliesToAllShipments()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentState: PaymentSm.Captured, fulfillmentState: FulfillmentSm.HandedToCarrier);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Shipments.Add(new Shipment
            {
                Id = Guid.NewGuid(), OrderId = order.Id, ProviderId = "stub", MethodCode = "express",
                State = Shipment.StateInTransit, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            });
            db.Shipments.Add(new Shipment
            {
                Id = Guid.NewGuid(), OrderId = order.Id, ProviderId = "stub", MethodCode = "express",
                State = Shipment.StateOutForDelivery, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            });
            await db.SaveChangesAsync();
        }

        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory, new[] { "orders.fulfillment.write" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);
        (await client.PostAsync($"/v1/admin/orders/{order.Id}/fulfillment/mark-delivered", null))
            .EnsureSuccessStatusCode();

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var ordersDb = verifyScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var shipments = await ordersDb.Shipments.AsNoTracking().Where(s => s.OrderId == order.Id).ToListAsync();
        shipments.Should().HaveCount(2);
        shipments.Should().AllSatisfy(s =>
        {
            s.State.Should().Be(Shipment.StateDelivered);
            s.DeliveredAt.Should().NotBeNull();
        });
    }

    // B3 — concurrent refund.completed events: cumulative over-refund must be blocked.
    [Fact]
    public async Task B3_ConcurrentRefunds_BlockOverRefund()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentState: PaymentSm.Captured, grandTotalMinor: 100_00);

        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.internal.advance_refund" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);

        // First, submit a return to flip refund_state to requested.
        (await client.PostAsJsonAsync($"/v1/internal/orders/{order.Id}/advance-refund-state",
            new { eventType = "return.submitted", returnRequestId = Guid.NewGuid() }))
            .EnsureSuccessStatusCode();

        // Two concurrent refund.completed for 60_00 + 60_00 (would exceed 100_00). The lock
        // ensures one wins (Partial) and the other 409s on over-refund.
        var refundA = client.PostAsJsonAsync($"/v1/internal/orders/{order.Id}/advance-refund-state",
            new { eventType = "refund.completed", returnRequestId = Guid.NewGuid(),
                  refundId = Guid.NewGuid(), refundedAmountMinor = 60_00 });
        var refundB = client.PostAsJsonAsync($"/v1/internal/orders/{order.Id}/advance-refund-state",
            new { eventType = "refund.completed", returnRequestId = Guid.NewGuid(),
                  refundId = Guid.NewGuid(), refundedAmountMinor = 60_00 });
        var responses = await Task.WhenAll(refundA, refundB);

        // Exactly one succeeded; the other returned 409 over_refund_blocked.
        var statuses = responses.Select(r => r.StatusCode).ToList();
        statuses.Should().Contain(HttpStatusCode.OK);
        statuses.Should().Contain(HttpStatusCode.Conflict);
    }

    // B6 — quote number is sequential per (market, yyyymm), not random hex.
    [Fact]
    public async Task B6_AdminCreateQuotation_GetsSequentialQuoteNumber()
    {
        await factory.ResetDatabaseAsync();
        var (_, customerId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.quotations.write" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);

        async Task<string> CreateOne()
        {
            var resp = await client.PostAsJsonAsync("/v1/admin/quotations/", new
            {
                accountId = customerId,
                marketCode = "KSA",
                priceExplanationId = Guid.NewGuid(),
                validUntil = DateTimeOffset.UtcNow.AddDays(7),
                lines = new[] { new {
                    productId = Guid.NewGuid(), sku = "Q-1", nameAr = "ت", nameEn = "T",
                    qty = 1, unitPriceMinor = 100_00, lineDiscountMinor = 0,
                    lineTaxMinor = 15_00, lineTotalMinor = 115_00, restricted = false } },
            });
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<QuoteResponse>();
            return body!.QuoteNumber;
        }

        var first = await CreateOne();
        var second = await CreateOne();
        first.Should().MatchRegex("^QUO-KSA-\\d{6}-\\d{6}$");
        second.Should().MatchRegex("^QUO-KSA-\\d{6}-\\d{6}$");
        first.Should().NotBe(second);
        // Sequence increments monotonically.
        var firstSeq = int.Parse(first[^6..]);
        var secondSeq = int.Parse(second[^6..]);
        secondSeq.Should().Be(firstSeq + 1);
    }

    private sealed record QuoteResponse(Guid QuotationId, string QuoteNumber, string Status);

    // B7 — finance export rejects from > to.
    [Fact]
    public async Task B7_FinanceExport_RejectsInvertedDateRange()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.finance.export" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, token);
        var from = DateTimeOffset.UtcNow.ToString("o");
        var to = DateTimeOffset.UtcNow.AddDays(-1).ToString("o");
        var response = await client.GetAsync($"/v1/admin/orders/export?format=csv&from={from}&to={to}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // B8 — filtered unique index prevents duplicate (provider, txn) on populated rows.
    [Fact]
    public async Task B8_DuplicateProviderTxnId_RejectedAtInsert()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentProviderId: "stub", paymentProviderTxnId: "txn-shared");

        var act = async () => await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            paymentProviderId: "stub", paymentProviderTxnId: "txn-shared");
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
