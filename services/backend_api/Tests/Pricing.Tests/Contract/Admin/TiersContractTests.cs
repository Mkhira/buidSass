using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Contract.Admin;

[Collection("pricing-fixture")]
public sealed class TiersContractTests(PricingTestFactory factory)
{
    [Fact]
    public async Task CreateTier_AndUpsertProductTierPrice()
    {
        await factory.ResetDatabaseAsync();
        var (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
            factory, new[] { "pricing.tier.read", "pricing.tier.write" });
        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var create = await client.PostAsJsonAsync("/v1/admin/pricing/b2b-tiers", new
        {
            slug = "tier-1",
            name = "Tier 1",
            defaultDiscountBps = 500,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "TIER-001", 10_000, new[] { "ksa" });
        }

        var listResp = await client.GetAsync("/v1/admin/pricing/b2b-tiers");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tiers = await listResp.Content.ReadFromJsonAsync<List<TierDto>>();
        var tierId = tiers!.Single(t => t.Slug == "tier-1").Id;

        var upsert = await client.PostAsJsonAsync(
            $"/v1/admin/pricing/products/{productId:N}/tier-prices",
            new { tierId, marketCode = "ksa", netMinor = 9_000L });
        upsert.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    public sealed record TierDto(Guid Id, string Slug, string Name, int DefaultDiscountBps, bool IsActive);
}
