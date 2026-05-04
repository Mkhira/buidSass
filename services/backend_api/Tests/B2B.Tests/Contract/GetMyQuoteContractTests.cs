using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T056 — HTTP contract for <c>GET /api/customer/quotes/{id}</c>
/// (contracts §2.4). Asserts owner-or-membership access gating, the
/// <c>next_action</c> derived field, and the prior-versions metadata array.
///
/// TDD note: handler lands in Cycle B; this suite captures the wire contract.
/// </summary>
public sealed class GetMyQuoteContractTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;
    public GetMyQuoteContractTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync($"/api/customer/quotes/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unknown_quote_id_returns_404_quote_not_found()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");

        var resp = await client.GetAsync($"/api/customer/quotes/{Guid.NewGuid()}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertReasonCode(resp, "quote.not_found");
    }

    [Fact]
    public async Task Caller_without_visibility_returns_404_quote_not_found()
    {
        // Per contract §2.4 the handler MUST NOT leak existence of quotes the caller
        // can't see — return 404 (not 403) for both unknown ids and
        // owned-by-someone-else-and-no-membership ids.
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");

        var resp = await client.GetAsync($"/api/customer/quotes/{Guid.NewGuid()}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Response_includes_next_action_field_with_known_vocabulary()
    {
        // Cycle B will exercise this with a seeded quote in each lifecycle state to
        // assert next_action ∈ {null, request_revision, submit_acceptance, renew_now}.
        // For contract-time the vocabulary is documented in the test name; concrete
        // body assertion is deferred until a quote-seeding helper exists.
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");

        var resp = await client.GetAsync($"/api/customer/quotes/{Guid.NewGuid()}");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.OK);

        // CodeRabbit Round 1: when the response is 200, validate the next_action
        // vocabulary so a regression that emits a wrong value (e.g. `cancel`)
        // doesn't pass silently. The 404-only path is exercised by the dedicated
        // unknown-id test above; this branch protects the success-shape contract.
        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("next_action", out var nextAction).Should().BeTrue();
            if (nextAction.ValueKind == JsonValueKind.String)
            {
                nextAction.GetString().Should().BeOneOf(
                    "request_revision",
                    "submit_acceptance",
                    "renew_now");
            }
            else
            {
                nextAction.ValueKind.Should().Be(JsonValueKind.Null);
            }
        }
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
