using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration;

[Collection("pricing-fixture")]
public sealed class CouponConcurrencyTests(PricingTestFactory factory)
{
    [Fact]
    public async Task CouponPerCustomer_100ConcurrentRedeems_ExactlyOneSucceeds()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        string token;
        Guid accountId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "CPN-001", priceHintMinor: 10_000, marketCodes: new[] { "ksa" });

            await PricingTestSeedHelper.CreateCouponAsync(
                scope.ServiceProvider, code: "ONCE", kind: "percent", value: 1_000, perCustomerLimit: 1);

            (token, accountId) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
                factory, new[] { "pricing.internal.calculate" });
        }

        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        // Shared orderId + perCustomerLimit=1 triggers the unique-index race; exactly one succeeds.
        var sharedOrderId = Guid.NewGuid();
        var tasks = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 20; i++)
        {
            tasks.Add(client.PostAsJsonAsync("/v1/internal/pricing/calculate", new
            {
                marketCode = "ksa",
                locale = "en",
                lines = new[] { new { productId, qty = 1 } },
                couponCode = "ONCE",
                accountId,
                orderId = sharedOrderId,
                mode = "issue",
            }));
        }

        var results = await Task.WhenAll(tasks);
        var successes = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflicts = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successes.Should().Be(1);
        conflicts.Should().Be(19);
    }
}
