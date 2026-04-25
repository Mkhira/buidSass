using System.Net;
using System.Net.Http.Json;
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
/// CodeRabbit round-1 critical fixes:
///   • CR1 — refund.completed/manual_confirmed must reject when payment isn't captured.
///   • CR2 — quotation conversion must be single-winner under concurrent calls.
/// </summary>
[Collection("orders-fixture")]
public sealed class CodeRabbitRound1Tests(OrdersTestFactory factory)
{
    [Fact]
    public async Task CR1_RefundOnAuthorizedOrder_Returns409PaymentNotCaptured()
    {
        await factory.ResetDatabaseAsync();
        var (_, customerId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, customerId,
            paymentState: PaymentSm.Authorized, grandTotalMinor: 100_00);

        var (token, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.internal.advance_refund" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, token);

        var response = await client.PostAsJsonAsync(
            $"/v1/internal/orders/{order.Id}/advance-refund-state",
            new { eventType = "refund.completed", refundId = Guid.NewGuid(),
                  refundedAmountMinor = 50_00 });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("order.refund.payment_not_captured");
    }

    [Fact]
    public async Task CR1_RefundOnPendingCodOrder_AlsoRejected()
    {
        await factory.ResetDatabaseAsync();
        var (_, customerId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, customerId,
            paymentState: PaymentSm.PendingCod, grandTotalMinor: 100_00);

        var (token, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.internal.advance_refund" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, token);

        var response = await client.PostAsJsonAsync(
            $"/v1/internal/orders/{order.Id}/advance-refund-state",
            new { eventType = "refund.manual_confirmed", refundId = Guid.NewGuid(),
                  refundedAmountMinor = 100_00 });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CR1_RefundOnCapturedOrder_Succeeds()
    {
        // Sanity check: legitimate refunds still work after the precheck.
        await factory.ResetDatabaseAsync();
        var (_, customerId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, customerId,
            paymentState: PaymentSm.Captured, grandTotalMinor: 100_00);

        var (token, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.internal.advance_refund" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, token);

        // First mark the return submitted, then complete it.
        (await client.PostAsJsonAsync($"/v1/internal/orders/{order.Id}/advance-refund-state",
            new { eventType = "return.submitted", returnRequestId = Guid.NewGuid() }))
            .EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync(
            $"/v1/internal/orders/{order.Id}/advance-refund-state",
            new { eventType = "refund.completed", refundId = Guid.NewGuid(),
                  refundedAmountMinor = 100_00 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CR2_ConcurrentQuotationAccept_ProducesOneOrder()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var quotationId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Quotations.Add(new Quotation
            {
                Id = quotationId,
                QuoteNumber = "QUO-KSA-202604-CR2",
                AccountId = accountId,
                MarketCode = "KSA",
                Status = Quotation.StatusActive,
                PriceExplanationId = Guid.NewGuid(),
                ValidUntil = DateTimeOffset.UtcNow.AddDays(7),
                CreatedByAccountId = accountId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Lines =
                {
                    new QuotationLine
                    {
                        Id = Guid.NewGuid(), QuotationId = quotationId, ProductId = Guid.NewGuid(),
                        Sku = "Q-CR2", NameAr = "ت", NameEn = "T", Qty = 1,
                        UnitPriceMinor = 100_00, LineTotalMinor = 100_00, AttributesJson = "{}",
                    },
                },
            });
            await db.SaveChangesAsync();
        }

        async Task<CreateFromQuotationResult> Convert()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<CreateFromQuotationHandler>();
            return await handler.CreateAsync(quotationId, accountId, CancellationToken.None);
        }

        // Two parallel callers race the conversion.
        var results = await Task.WhenAll(Convert(), Convert());
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        // Exactly one orderId emerges (winner stays; loser replays it via the
        // ConvertedOrderId-is-not-null idempotent branch).
        results.Select(r => r.OrderId).Distinct().Should().HaveCount(1);

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var orderCount = await verifyDb.Orders.AsNoTracking()
            .CountAsync(o => o.QuotationId == quotationId);
        orderCount.Should().Be(1);
    }

    [Fact]
    public void RefundSm_IsValidTransition_AcceptsUppercase()
    {
        // CR minor: case-insensitive consistency with All set.
        RefundSm.IsValidTransition("REQUESTED", "FULL").Should().BeTrue();
        RefundSm.IsValidTransition("Partial", "Full").Should().BeTrue();
    }
}
