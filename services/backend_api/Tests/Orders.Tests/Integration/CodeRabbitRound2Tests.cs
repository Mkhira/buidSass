using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Orders.Internal.CreateFromQuotation;
using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// CodeRabbit round-2 regressions:
///   • R2-Major-1 — JsonDocument disposal + malformed-JSON tolerance.
///   • R2-Major-2 — IReservationConverter shared seam (no static cross-module dep).
///   • R2-Major-3 — PaymentSm.PendingBnpl + ResolveInitialPaymentState.
///   • R2-Major-4 — MarketCurrency fails closed for unknown markets.
/// </summary>
[Collection("orders-fixture")]
public sealed class CodeRabbitRound2Tests(OrdersTestFactory factory)
{
    [Fact]
    public async Task R2M1_GetOrder_ToleratesMalformedAddressJson()
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId);
        // Bypass the entity (non-jsonb wouldn't accept invalid JSON via EF) using raw SQL —
        // simulate a corrupted row.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE orders.orders SET \"ShippingAddressJson\" = '{{}}'::jsonb WHERE \"Id\" = {order.Id}");
        }
        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, token);
        var response = await client.GetAsync($"/v1/customer/orders/{order.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void R2M2_IReservationConverter_RegisteredViaDi()
    {
        // CR Major: orders should resolve the converter via DI, not via a static call.
        using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetService<IReservationConverter>().Should().NotBeNull();
    }

    [Fact]
    public async Task R2M3_BnplCheckout_LandsInPendingBnpl()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory);
        var productId = await OrdersTestSeed.SeedProductAsync(factory, "BNPL-1");

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IOrderFromCheckoutHandler>();
        var orderId = Guid.NewGuid();
        var result = await handler.CreateAsync(new OrderFromCheckoutRequest(
            PreallocatedOrderId: orderId,
            SessionId: Guid.NewGuid(),
            CartId: Guid.NewGuid(),
            AccountId: accountId,
            MarketCode: "KSA",
            Lines: new[] { new OrderFromCheckoutLine(productId, 1, 100_00, 100_00, 0, 100_00, null) },
            CouponCode: null,
            PaymentMethod: "bnpl",
            PaymentProviderId: "stub",
            PaymentProviderTxnId: "txn-bnpl-1",
            ShippingFeeMinor: 0,
            ShippingProviderId: "stub",
            ShippingMethodCode: "express",
            ShippingAddressJson: "{}",
            BillingAddressJson: "{}",
            SubtotalMinor: 100_00,
            DiscountMinor: 0,
            TaxMinor: 0,
            GrandTotalMinor: 100_00,
            Currency: "SAR",
            IssuedExplanationId: Guid.NewGuid()), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var stored = await ordersDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        stored.PaymentState.Should().Be(PaymentSm.PendingBnpl);
    }

    [Fact]
    public void R2M3_PaymentSm_AcceptsPendingBnplTransitions()
    {
        PaymentSm.IsValidTransition(PaymentSm.PendingBnpl, PaymentSm.Captured).Should().BeTrue();
        PaymentSm.IsValidTransition(PaymentSm.PendingBnpl, PaymentSm.Failed).Should().BeTrue();
        PaymentSm.IsValidTransition(PaymentSm.PendingBnpl, PaymentSm.Voided).Should().BeTrue();
        PaymentSm.IsPending(PaymentSm.PendingBnpl).Should().BeTrue();
        PaymentSm.All.Should().Contain(PaymentSm.PendingBnpl);
    }

    [Fact]
    public void R2M4_MarketCurrency_FailsClosedOnUnknownNonEmpty()
    {
        var act = () => MarketCurrency.Resolve("FAKE");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void R2M4_MarketCurrency_StillFallsBackOnEmpty()
    {
        // Truly missing / whitespace input keeps the SAR default — represents the
        // "no market specified yet" pre-checkout state, not a typo.
        MarketCurrency.Resolve("").Should().Be("SAR");
        MarketCurrency.Resolve("   ").Should().Be("SAR");
    }
}
