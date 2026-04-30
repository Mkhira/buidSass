using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.Reviews.Primitives;
using FluentAssertions;
using Reviews.Tests.Contract.Infrastructure;

namespace Reviews.Tests.Contract;

/// <summary>
/// Spec 022 T110 + T114 — HTTP contract for the public unauthenticated
/// rating-aggregate read endpoints. Single-id read + batch read; cache header
/// presence; reason-code on unknown market.
/// </summary>
public sealed class ReadProductRatingContractTests : IClassFixture<ReviewsApiFactory>
{
    private readonly ReviewsApiFactory _factory;

    public ReadProductRatingContractTests(ReviewsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Single_read_returns_200_unauthenticated_with_cache_header()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync($"/api/public/reviews/aggregates/{Guid.NewGuid()}?market_code=SA");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.CacheControl?.Public.Should().BeTrue();
        resp.Headers.CacheControl?.MaxAge.Should().Be(TimeSpan.FromSeconds(60));

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reviewCount").GetInt32().Should().Be(0);
        body.GetProperty("avgRating").ValueKind.Should().Be(JsonValueKind.Null,
            "FR-028 — null avg when count = 0");
    }

    [Fact]
    public async Task Unknown_market_code_returns_400_with_aggregate_market_invalid()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync($"/api/public/reviews/aggregates/{Guid.NewGuid()}?market_code=XX");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reasonCode").GetString().Should().Be(ReviewReasonCode.AggregateMarketInvalid);
    }

    [Fact]
    public async Task Batch_read_returns_200_with_items_array()
    {
        var client = _factory.CreateClient();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var resp = await client.GetAsync(
            $"/api/public/reviews/aggregates?product_ids={ids[0]:N},{ids[1]:N}&market_code=SA");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(2);
    }
}
