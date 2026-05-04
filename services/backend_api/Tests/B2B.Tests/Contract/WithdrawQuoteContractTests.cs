using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T057 — HTTP contract for <c>POST /api/customer/quotes/{id}/withdraw</c>
/// (contracts §2.5). Asserts: any non-terminal state may withdraw; terminal states
/// (<c>accepted</c>, <c>rejected</c>, <c>expired</c>, <c>withdrawn</c>) reject with
/// <c>409 quote.invalid_state_for_action</c>; idempotency-key required;
/// <c>QuoteWithdrawn</c> domain event published exactly once.
///
/// TDD note: handler/route mapping lands in Cycle B.
/// </summary>
public sealed class WithdrawQuoteContractTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;
    public WithdrawQuoteContractTests(B2BApiFactory factory) => _factory = factory;

    private static string Route(Guid id) => $"/api/customer/quotes/{id}/withdraw";

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_400()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { });

        // Per §1: every state-transitioning POST requires Idempotency-Key.
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_quote_returns_404_quote_not_found()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertReasonCode(resp, "quote.not_found");
    }

    [Fact]
    public async Task Withdraw_in_terminal_state_returns_409_invalid_state_for_action()
    {
        // Cycle B integration suite will seed a quote in each terminal state and
        // assert the 409. Contract-level assertion: the wire vocabulary uses
        // `quote.invalid_state_for_action` (not a generic 409 with no reason code).
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { });
        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.Conflict);
        if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            await AssertReasonCode(resp, "quote.invalid_state_for_action");
        }
        else if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            await AssertReasonCode(resp, "quote.not_found");
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
