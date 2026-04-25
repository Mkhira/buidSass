using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.Orders.Primitives.StateMachines;
using FluentAssertions;
using Orders.Tests.Infrastructure;

namespace Orders.Tests.Integration;

/// <summary>
/// H9 / SC-009 — Per-market return window boundaries. KSA = 14 days, EG = 7 days at launch.
/// Exercised through the customer endpoint /v1/customer/orders/{id}/return-eligibility.
/// </summary>
[Collection("orders-fixture")]
public sealed class ReturnWindowBoundariesTests(OrdersTestFactory factory)
{
    [Theory]
    [InlineData("ksa", 1, true, 13)]    // KSA delivered 1 day ago → 13 days remaining
    [InlineData("ksa", 14, true, 0)]    // exactly at boundary
    [InlineData("ksa", 15, false, 0)]   // expired
    [InlineData("eg", 1, true, 6)]
    [InlineData("eg", 7, true, 0)]
    [InlineData("eg", 8, false, 0)]
    public async Task ReturnEligibility_RespectsMarketWindow(
        string market, int daysAgo, bool expectedEligible, int expectedDaysRemaining)
    {
        await factory.ResetDatabaseAsync();
        var (token, accountId) = await OrdersCustomerAuthHelper.IssueCustomerTokenAsync(factory, market);

        var order = await OrdersTestSeed.SeedOrderAsync(factory, accountId,
            market: market.ToUpperInvariant(),
            paymentState: PaymentSm.Captured,
            fulfillmentState: FulfillmentSm.Delivered,
            deliveredAt: DateTimeOffset.UtcNow.AddDays(-daysAgo));

        var client = factory.CreateClient();
        OrdersCustomerAuthHelper.SetBearer(client, token);

        var response = await client.GetAsync($"/v1/customer/orders/{order.Id}/return-eligibility");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EligibilityResponse>();
        body.Should().NotBeNull();
        body!.Eligible.Should().Be(expectedEligible);
        if (expectedEligible)
        {
            body.DaysRemaining.Should().Be(expectedDaysRemaining);
        }
    }

    private sealed record EligibilityResponse(bool Eligible, int? DaysRemaining, string? ReasonCode);
}
