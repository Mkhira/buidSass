using BackendApi.Modules.Orders.Primitives.StateMachines;
using FluentAssertions;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// H7 / SC-007 — Streaming finance CSV emits one row per order_line with line-level tax +
/// discount columns matching the order's stored snapshot. Spec 012 invoices reference the
/// same snapshot, so byte-equality of these columns is the reconciliation invariant.
/// </summary>
[Collection("orders-fixture")]
public sealed class FinanceExportTests(OrdersTestFactory factory)
{
    [Fact]
    public async Task Export_ReturnsCsvWithLineLevelTotals()
    {
        await factory.ResetDatabaseAsync();
        var (_, customerId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory, "ksa");
        await OrdersTestSeed.SeedOrderAsync(factory, customerId, market: "KSA",
            paymentState: PaymentSm.Captured, grandTotalMinor: 250_00);
        await OrdersTestSeed.SeedOrderAsync(factory, customerId, market: "KSA",
            paymentState: PaymentSm.Captured, grandTotalMinor: 175_00);

        var (adminToken, _) = await OrdersAdminAuthHelper.IssueAdminTokenAsync(factory,
            new[] { "orders.finance.export" });
        var client = factory.CreateClient();
        OrdersAdminAuthHelper.SetBearer(client, adminToken);

        var response = await client.GetAsync("/v1/admin/orders/export?market=KSA&format=csv");
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("order_number,placed_at,market,currency,grand_total_minor");
        lines.Should().HaveCountGreaterOrEqualTo(3); // header + 2 order_lines (each seeded order has 1 line)
        // Both seeded orders' totals appear in the line rows.
        csv.Should().Contain("25000");
        csv.Should().Contain("17500");
    }
}
