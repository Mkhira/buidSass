using BackendApi.Modules.Orders.Primitives.StateMachines;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// H10 / SC-010 — Every admin state mutation MUST leave an audit row.
/// Drives a representative admin mutation (start-picking) end-to-end through the HTTP
/// surface and verifies the corresponding row in spec 003's audit_log_entries table.
/// </summary>
[Collection("orders-fixture")]
public sealed class AdminAuditTests(OrdersTestFactory factory)
{
    [Fact]
    public async Task StartPicking_WritesAuditRow()
    {
        await factory.ResetDatabaseAsync();
        var (_, customerId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        var order = await OrdersTestSeed.SeedOrderAsync(factory, customerId,
            paymentState: PaymentSm.Captured, fulfillmentState: FulfillmentSm.NotStarted);

        var (adminToken, adminId) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.fulfillment.write" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);

        var response = await client.PostAsync($"/v1/admin/orders/{order.Id}/fulfillment/start-picking", null);
        response.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditCount = await appDb.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM audit_log_entries WHERE \"EntityType\" = 'orders.order' AND \"EntityId\" = {order.Id} AND \"Action\" = 'orders.fulfillment.start_picking' AND \"ActorId\" = {adminId}")
            .ToListAsync();
        auditCount.Single().Should().BeGreaterOrEqualTo(1);
    }
}
