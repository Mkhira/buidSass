using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Tests.Infrastructure;

namespace Pricing.Tests.Integration;

[Collection("pricing-fixture")]
public sealed class QuotationReuseTests(PricingTestFactory factory)
{
    [Fact]
    public async Task QuoteIssued_ReusedOnAcceptance()
    {
        await factory.ResetDatabaseAsync();
        Guid productId;
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await PricingTestSeedHelper.SeedKsaVatAsync(scope.ServiceProvider);
            productId = await PricingTestSeedHelper.CreatePublishedProductAsync(
                scope.ServiceProvider, "QUO-001", priceHintMinor: 10_000, marketCodes: new[] { "ksa" });

            (token, _) = await PricingAdminAuthHelper.IssueAdminTokenAsync(
                factory,
                new[] { "pricing.internal.calculate", "pricing.explanation.read" });
        }

        var client = factory.CreateClient();
        PricingAdminAuthHelper.SetBearer(client, token);

        var quotationId = Guid.NewGuid();
        var first = await client.PostAsJsonAsync("/v1/internal/pricing/calculate", new
        {
            marketCode = "ksa",
            locale = "en",
            lines = new[] { new { productId, qty = 1 } },
            mode = "issue",
            quotationId,
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<CalculateResponseDto>();
        firstBody!.ExplanationHash.Should().NotBeNullOrEmpty();

        // Second call for the same quotationId returns stored explanation verbatim
        var second = await client.PostAsJsonAsync("/v1/internal/pricing/calculate", new
        {
            marketCode = "ksa",
            locale = "en",
            lines = new[] { new { productId, qty = 1 } },
            mode = "preview",
            quotationId,
        });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<CalculateResponseDto>();
        secondBody!.ExplanationHash.Should().Be(firstBody.ExplanationHash);
    }

    public sealed record CalculateResponseDto(
        IReadOnlyList<object> Lines,
        object Totals,
        string Currency,
        string ExplanationHash,
        Guid? ExplanationId);
}
