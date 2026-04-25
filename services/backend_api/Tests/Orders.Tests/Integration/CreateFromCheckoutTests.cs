using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// H4 / SC-001 — exercises <c>IOrderFromCheckoutHandler.CreateAsync</c> directly via DI
/// against the real Postgres testcontainer. Skips the full HTTP round-trip through spec
/// 010's Submit slice (that path is exercised by Checkout.Tests) — here we focus on the
/// order-creation invariants: pre-allocated id is honoured, order_number matches the
/// ORD-{market}-{yyyymm}-{seq6} format, snapshots land, outbox row emitted, idempotent on
/// retry.
/// </summary>
[Collection("orders-fixture")]
public sealed class CreateFromCheckoutTests(OrdersTestFactory factory)
{
    [Fact]
    public async Task CreateAsync_PlacesOrder_WithSnapshotsAndOutboxEvent()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var productId = await OrdersTestSeed.SeedProductAsync(factory, "SKU-CFCT-1", nameEn: "Cart-To-Order Product");

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IOrderFromCheckoutHandler>();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var preallocatedOrderId = Guid.NewGuid();
        var explanationId = Guid.NewGuid();
        var request = new OrderFromCheckoutRequest(
            PreallocatedOrderId: preallocatedOrderId,
            SessionId: Guid.NewGuid(),
            CartId: Guid.NewGuid(),
            AccountId: accountId,
            MarketCode: "KSA",
            Lines: new[]
            {
                new OrderFromCheckoutLine(
                    ProductId: productId,
                    Qty: 2,
                    UnitPriceMinor: 50_00,
                    NetMinor: 100_00,
                    TaxMinor: 15_00,
                    GrossMinor: 115_00,
                    ReservationId: null),
            },
            CouponCode: null,
            PaymentMethod: "card",
            PaymentProviderId: "stub",
            PaymentProviderTxnId: "txn-1",
            ShippingFeeMinor: 5_00,
            ShippingProviderId: "stub",
            ShippingMethodCode: "express",
            ShippingAddressJson: """{"line1":"Riyadh"}""",
            BillingAddressJson: """{"line1":"Riyadh"}""",
            SubtotalMinor: 100_00,
            DiscountMinor: 0,
            TaxMinor: 15_00,
            GrandTotalMinor: 120_00,
            Currency: "SAR",
            IssuedExplanationId: explanationId);

        var result = await handler.CreateAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OrderId.Should().Be(preallocatedOrderId);
        result.OrderNumber.Should().MatchRegex("^ORD-KSA-\\d{6}-\\d{6}$");

        var order = await ordersDb.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .SingleAsync(o => o.Id == preallocatedOrderId);
        order.PriceExplanationId.Should().Be(explanationId);
        order.OrderState.Should().Be(OrderSm.Placed);
        order.PaymentState.Should().Be(PaymentSm.Captured); // mirrors StubOrderFromCheckoutHandler default
        order.GrandTotalMinor.Should().Be(120_00);
        order.Lines.Should().HaveCount(1);
        order.Lines.Single().Sku.Should().Be("SKU-CFCT-1");
        order.Lines.Single().NameEn.Should().Be("Cart-To-Order Product");

        var outbox = await ordersDb.Outbox.AsNoTracking().Where(e => e.AggregateId == preallocatedOrderId).ToListAsync();
        outbox.Should().Contain(e => e.EventType == "order.placed");
        outbox.Should().Contain(e => e.EventType == "payment.captured"); // Captured initial state emits invoice trigger
    }

    [Fact]
    public async Task CreateAsync_IsIdempotentOnRetry()
    {
        await factory.ResetDatabaseAsync();
        var (_, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var productId = await OrdersTestSeed.SeedProductAsync(factory, "SKU-IDEM");

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IOrderFromCheckoutHandler>();
        var preallocatedOrderId = Guid.NewGuid();
        var request = BuildRequest(preallocatedOrderId, accountId, productId);

        var first = await handler.CreateAsync(request, CancellationToken.None);
        var second = await handler.CreateAsync(request, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.OrderId.Should().Be(first.OrderId);
        second.OrderNumber.Should().Be(first.OrderNumber);

        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var rows = await ordersDb.Orders.AsNoTracking().Where(o => o.Id == preallocatedOrderId).CountAsync();
        rows.Should().Be(1);
    }

    private static OrderFromCheckoutRequest BuildRequest(Guid orderId, Guid accountId, Guid productId) =>
        new(
            PreallocatedOrderId: orderId,
            SessionId: Guid.NewGuid(),
            CartId: Guid.NewGuid(),
            AccountId: accountId,
            MarketCode: "KSA",
            Lines: new[]
            {
                new OrderFromCheckoutLine(productId, 1, 100_00, 100_00, 15_00, 115_00, null),
            },
            CouponCode: null,
            PaymentMethod: "card",
            PaymentProviderId: "stub",
            PaymentProviderTxnId: "txn-idem",
            ShippingFeeMinor: 0,
            ShippingProviderId: "stub",
            ShippingMethodCode: "express",
            ShippingAddressJson: "{}",
            BillingAddressJson: "{}",
            SubtotalMinor: 100_00,
            DiscountMinor: 0,
            TaxMinor: 15_00,
            GrandTotalMinor: 115_00,
            Currency: "SAR",
            IssuedExplanationId: Guid.NewGuid());
}
