using BackendApi.Modules.Orders.Persistence;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// H8 / FR-026 / SC-008 — COD delivery via admin endpoint flips payment.state from
/// pending_cod → captured and emits payment.captured for spec 012's invoice issuance.
/// </summary>
[Collection("orders-fixture")]
public sealed class CodDeliveryCaptureTests(OrdersTestFactory factory)
{
    [Fact]
    public async Task MarkDelivered_OnCodOrder_CapturesPayment()
    {
        await factory.ResetDatabaseAsync();
        var (_, customerId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        // Seed COD order in handed_to_carrier state — eligible for delivery transition.
        var order = await OrdersTestSeed.SeedOrderAsync(factory, customerId,
            paymentState: PaymentSm.PendingCod, fulfillmentState: FulfillmentSm.HandedToCarrier);

        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.fulfillment.write" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);

        var response = await client.PostAsync($"/v1/admin/orders/{order.Id}/fulfillment/mark-delivered", null);
        response.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var refreshed = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        refreshed.PaymentState.Should().Be(PaymentSm.Captured);
        refreshed.FulfillmentState.Should().Be(FulfillmentSm.Delivered);
        refreshed.DeliveredAt.Should().NotBeNull();

        var capturedRow = await db.Outbox.AsNoTracking()
            .FirstOrDefaultAsync(e => e.AggregateId == order.Id && e.EventType == "payment.captured");
        capturedRow.Should().NotBeNull();
        // jsonb storage normalises whitespace; match the field+value pair tolerantly.
        capturedRow!.PayloadJson.Should().MatchRegex("\"method\"\\s*:\\s*\"cod\"");
    }
}
