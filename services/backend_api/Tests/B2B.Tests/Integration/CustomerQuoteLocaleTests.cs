using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T061 — verifies FR-041 / FR-042: every customer-visible problem-details
/// response carries a stable <c>reasonCode</c> extension whose token is keyed in
/// BOTH <c>b2b.en.icu</c> and <c>b2b.ar.icu</c>. The unit-level
/// <see cref="BackendApi.Modules.B2B.Primitives.QuoteReasonCode"/>
/// /-key-existence test (<c>QuoteReasonCodeIcuKeysTests</c>) already enforces
/// per-key bilingual coverage; this test closes the wire-side loop by asserting
/// the canonical tokens fire over actual HTTP rejections.
///
/// We exercise three rejection branches against the live customer surface
/// without seeding any quote rows — every assertion lands at the validation /
/// claim-shape layer, which is where the locale-bearing problem-details path is
/// hottest in practice. Cycle C-3 / Cycle B (T070 / SubmitAcceptance) layers in
/// the localized title + detail strings; this test pins the pre-localization
/// invariant that the wire token is stable so the localizer has something to
/// resolve against.
///
/// CodeRabbit / DoD note: Cycle B's response factory unifies <c>title</c> and
/// <c>detail</c> into the same English string until the ICU resolver lands.
/// This test therefore asserts the <c>reasonCode</c> token (the load-bearing
/// vocabulary), not the message text — that flips to a localized-text assertion
/// in Cycle C-3 once the resolver ships.
/// </summary>
public sealed class CustomerQuoteLocaleTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public CustomerQuoteLocaleTests(B2BApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    public async Task Idempotency_key_missing_emits_problem_details_with_stable_reason_code(
        string acceptLanguage)
    {
        // Idempotency-Key header missing — the endpoint short-circuits with
        // problem-details before any handler runs. The locale assertion here
        // documents that the wire shape is stable across Accept-Language values:
        // the token doesn't change because the customer asked for Arabic.
        _factory.ResetStubState();
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), market: "ksa", acceptLanguage: acceptLanguage);

        var resp = await client.PostAsJsonAsync(
            "/api/customer/quotes/from-cart",
            new { });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue(
            "every B2B problem-details response carries a stable reasonCode extension (FR-041)");
        reasonCode.GetString().Should().Be("quote.required_field_missing");

        // The token is locale-invariant. Cycle C-3's ICU resolver will localize
        // `title` and `detail` based on the Accept-Language header — until then,
        // the wire vocabulary is the test surface.
        problem.TryGetProperty("title", out _).Should().BeTrue();
        problem.TryGetProperty("detail", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Unknown_market_claim_emits_market_mismatch_with_stable_reason_code(
        string acceptLanguage)
    {
        // Claim resolution surfaces an unknown market explicitly rather than
        // falling open to the ksa default (B2BResponseFactory.Normalize).
        _factory.ResetStubState();
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), market: "unknown_market", acceptLanguage: acceptLanguage);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            "/api/customer/quotes/from-cart",
            new { });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
        reasonCode.GetString().Should().Be("quote.market_mismatch",
            "an unknown market claim is the canonical FR-011 wire signal regardless of Accept-Language");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Get_unknown_quote_emits_quote_not_found_with_stable_reason_code(
        string acceptLanguage)
    {
        // Visibility-leak prevention: an unknown quote id and a not-authorized
        // quote id BOTH surface 404 quote.not_found regardless of locale.
        _factory.ResetStubState();
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), market: "ksa", acceptLanguage: acceptLanguage);

        var resp = await client.GetAsync($"/api/customer/quotes/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
        problem.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
        reasonCode.GetString().Should().Be("quote.not_found");
    }

    private static void AuthenticateAs(
        HttpClient client,
        Guid customerId,
        string market,
        string acceptLanguage)
    {
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);
    }
}
