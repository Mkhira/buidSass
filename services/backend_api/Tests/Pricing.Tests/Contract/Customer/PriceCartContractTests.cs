using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Contract.Customer;

[Collection("pricing-fixture")]
public sealed class PriceCartContractTests(PricingTestFactory factory)
{
    [Fact]
    public async Task PriceCart_ListPlusVat_BreakdownVisible()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "PRC-001", priceHintMinor: 10_000, marketCodes: new[] { "ksa" });
        }

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/v1/customer/pricing/price-cart", new
        {
            marketCode = "ksa",
            locale = "en",
            lines = new[] { new { productId, qty = 1 } },
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PriceCartResponseDto>();
        body.Should().NotBeNull();
        body!.Currency.Should().Be("SAR");
        body.Totals.SubtotalMinor.Should().Be(10_000);
        body.Totals.TaxMinor.Should().Be(1_500);
        body.Totals.GrandTotalMinor.Should().Be(11_500);
        body.Lines.Should().ContainSingle();
        body.Lines[0].Layers.Should().Contain(l => l.Layer == "list");
        body.Lines[0].Layers.Should().Contain(l => l.Layer == "tax" && l.AppliedMinor == 1_500);
        body.ExplanationHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PriceCart_Coupon_AppliesPercentWithCap()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "PRC-002", priceHintMinor: 100_000, marketCodes: new[] { "ksa" });
            await PricingTestSeedHelper.CreateCouponAsync(
                scope.ServiceProvider, code: "WELCOME10", kind: "percent", value: 1_000, capMinor: 5_000);
        }

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/v1/customer/pricing/price-cart", new
        {
            marketCode = "ksa",
            locale = "en",
            lines = new[] { new { productId, qty = 1 } },
            couponCode = "welcome10",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PriceCartResponseDto>();
        body!.Totals.SubtotalMinor.Should().Be(95_000); // cap hit
        body.Totals.GrandTotalMinor.Should().Be(95_000 + 14_250); // 15% VAT on 95,000
    }

    [Fact]
    public async Task PriceCart_ExpiredCoupon_ReturnsReasonCode()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "PRC-003", priceHintMinor: 10_000, marketCodes: new[] { "ksa" });
            await PricingTestSeedHelper.CreateCouponAsync(
                scope.ServiceProvider, code: "EXPIRED", validTo: DateTimeOffset.UtcNow.AddDays(-1));
        }

        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/v1/customer/pricing/price-cart", new
        {
            marketCode = "ksa",
            locale = "en",
            lines = new[] { new { productId, qty = 1 } },
            couponCode = "EXPIRED",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDto>();
        problem!.ReasonCode.Should().Be("pricing.coupon.expired");
    }

    [Fact]
    public async Task InternalCalculate_Tier2Account_AppliesTierPrice()
    {
        // Tier resolution requires an authenticated account. The customer /price-cart endpoint is
        // unauthenticated (Principle 1) and does not parse a JWT; B2B-tier pricing flows through
        // /v1/internal/pricing/calculate with an explicit accountId — which is how cart/checkout
        // (spec 009/010) will wire tier pricing at launch.
        await factory.ResetDatabaseAsync();
        Guid productId;
        string token;
        Guid accountId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "PRC-004", priceHintMinor: 10_000, marketCodes: new[] { "ksa" });
            var tierId = await PricingTestSeedHelper.CreateTierAsync(scope.ServiceProvider, "tier-2");

            (token, accountId) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
                factory, new[] { "pricing.internal.calculate" });
            await PricingTestSeedHelper.AssignTierAsync(scope.ServiceProvider, accountId, tierId);
            await PricingTestSeedHelper.UpsertTierPriceAsync(scope.ServiceProvider, productId, tierId, "ksa", netMinor: 9_000);
        }

        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var resp = await client.PostAsJsonAsync("/v1/internal/pricing/calculate", new
        {
            marketCode = "ksa",
            locale = "en",
            lines = new[] { new { productId, qty = 1 } },
            accountId,
            mode = "preview",
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<PriceCartResponseDto>();
        body!.Totals.SubtotalMinor.Should().Be(9_000);
    }
}
