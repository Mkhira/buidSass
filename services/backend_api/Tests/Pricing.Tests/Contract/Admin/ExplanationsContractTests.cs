using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Contract.Admin;

[Collection("pricing-fixture")]
public sealed class ExplanationsContractTests(PricingTestFactory factory)
{
    [Fact]
    public async Task GetExplanation_ByQuoteId_ReturnsImmutable()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "EXPL-001", 10_000, new[] { "ksa" });
            (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
                factory, new[] { "pricing.internal.calculate", "pricing.explanation.read" });
        }

        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var quotationId = Guid.NewGuid();
        var issued = await client.PostAsJsonAsync("/v1/internal/pricing/calculate", new
        {
            marketCode = "ksa",
            locale = "en",
            lines = new[] { new { productId, qty = 1 } },
            mode = "issue",
            quotationId,
        });
        issued.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetch = await client.GetAsync($"/v1/admin/pricing/explanations/quote/{quotationId:N}");
        fetch.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetExplanation_NotFound_Returns404()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { "pricing.explanation.read" });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.GetAsync($"/v1/admin/pricing/explanations/quote/{Guid.NewGuid():N}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
