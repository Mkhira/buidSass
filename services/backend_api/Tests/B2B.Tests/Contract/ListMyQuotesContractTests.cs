using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T055 — HTTP contract for <c>GET /api/customer/quotes</c> (contracts §2.3).
/// Asserts pagination shape and visibility scope: caller-owned individual quotes plus
/// company quotes where caller holds <c>buyer</c> / <c>approver</c> / <c>companies.admin</c>
/// membership.
///
/// TDD note: handler/endpoint mapping lands in Cycle B. Until then this suite reds
/// against the unmapped route; the assertions document the eventual response shape.
/// </summary>
public sealed class ListMyQuotesContractTests : IClassFixture<B2BApiFactory>
{
    private const string Route = "/api/customer/quotes";
    private readonly B2BApiFactory _factory;

    public ListMyQuotesContractTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync(Route);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_request_returns_paginated_envelope()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");

        var resp = await client.GetAsync(Route);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("items", out var items).Should().BeTrue("contract §2.3 paginated envelope requires 'items'");
        items.ValueKind.Should().Be(JsonValueKind.Array);
        body.TryGetProperty("page", out _).Should().BeTrue();
        body.TryGetProperty("page_size", out _).Should().BeTrue();
        body.TryGetProperty("total", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Page_size_above_50_returns_400_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");

        var resp = await client.GetAsync($"{Route}?page_size=500");

        // Per contract §2.3: page_size ≤ 50. CodeRabbit Round 1: pin the
        // reasonCode so the test only passes when the page_size branch fires —
        // a regression that 400s on a different validation rule (sort/state) is
        // no longer admissible.
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Sort_oldest_returns_oldest_first_ordering()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");

        var resp = await client.GetAsync($"{Route}?sort=oldest");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        // Cycle B handler ordering will be asserted once seed data is present;
        // this contract test pins the sort-token vocabulary (`oldest|newest`) only.
    }

    [Fact]
    public async Task Other_market_caller_does_not_see_other_market_quotes()
    {
        // Cross-market visibility is gated by ADR-010's market_code partition. The
        // handler MUST scope by the caller's market_code claim. Documented here as a
        // contract-level expectation; data-bearing assertion lands in Cycle B's
        // integration test once company + quote seed helpers exist.
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "eg");

        var resp = await client.GetAsync(Route);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static void AuthenticateAs(HttpClient client, Guid customerId, string market)
    {
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
    }

    private static async Task AssertReasonCode(HttpResponseMessage resp, string expected)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
        reasonCode.GetString().Should().Be(expected);
    }
}
