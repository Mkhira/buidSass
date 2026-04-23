using System.Net;
using System.Net.Http.Json;
using Catalog.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Tests.Contract.Restrictions;

[Collection("catalog-fixture")]
public sealed class CheckRestrictionContractTests(CatalogTestFactory factory)
{
    [Fact]
    public async Task CheckRestriction_RestrictedUnverified_ReturnsAllowedFalse()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            productId = await CatalogTestSeedHelper.CreateProductAsync(
                scope.ServiceProvider,
                brandId,
                restricted: true,
                restrictionReasonCode: "professional_verification",
                restrictionMarkets: new[] { "ksa" });
        }

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/v1/internal/catalog/restrictions/check",
            new { productId, marketCode = "ksa", verificationState = "unverified" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RestrictionResponseDto>();
        body!.Allowed.Should().BeFalse();
        body.ReasonCode.Should().Be("catalog.restricted.professional_verification");
    }

    [Fact]
    public async Task CheckRestriction_RestrictedVerified_ReturnsAllowedTrue()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            productId = await CatalogTestSeedHelper.CreateProductAsync(
                scope.ServiceProvider,
                brandId,
                restricted: true,
                restrictionReasonCode: "professional_verification",
                restrictionMarkets: new[] { "ksa" });
        }

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/v1/internal/catalog/restrictions/check",
            new { productId, marketCode = "ksa", verificationState = "verified" });

        var body = await response.Content.ReadFromJsonAsync<RestrictionResponseDto>();
        body!.Allowed.Should().BeTrue();
        body.ReasonCode.Should().Be("ok");
    }

    [Fact]
    public async Task CheckRestriction_MarketScoped_UnrestrictedInOtherMarket()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var brandId = await CatalogTestSeedHelper.CreateBrandAsync(scope.ServiceProvider, "acme");
            productId = await CatalogTestSeedHelper.CreateProductAsync(
                scope.ServiceProvider,
                brandId,
                restricted: true,
                restrictionReasonCode: "professional_verification",
                restrictionMarkets: new[] { "ksa" });
        }

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/v1/internal/catalog/restrictions/check",
            new { productId, marketCode = "eg", verificationState = "unverified" });

        var body = await response.Content.ReadFromJsonAsync<RestrictionResponseDto>();
        body!.Allowed.Should().BeTrue("restriction only applies to listed markets");
    }

    private sealed record RestrictionResponseDto(bool Allowed, string ReasonCode);
}
