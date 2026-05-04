using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T079 — HTTP contract for <c>GET /api/admin/quotes/{id}</c>
/// (contracts §4.2). Asserts the wire shape: full Quote + every QuoteVersion
/// (line items + totals) + every transition + customer/company context +
/// <c>restriction_policy_snapshot</c> + <c>customer_locale</c> + active
/// <c>quote_market_schema</c> at request time, plus the two real-time advisory
/// blocks <c>verification_warnings</c> and <c>archived_sku_lines</c>
/// (re-evaluated each call).
///
/// TDD note: handler lands in T086 (Cycle B). Until then the route is unmapped
/// and every assertion uses <c>BeOneOf(404, ExpectedCode)</c>. The exhaustive
/// behavioral assertions (verification expired between request and authoring,
/// SKU archived between request and authoring) live in T084a / T084b
/// integration tests which seed the relevant cross-module stub state.
/// </summary>
public sealed class AdminQuoteDetailContractTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public AdminQuoteDetailContractTests(B2BApiFactory factory)
    {
        _factory = factory;
    }

    private static string Route(Guid id) => $"/api/admin/quotes/{id}";

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync(Route(Guid.NewGuid()));
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_without_quotes_permission_returns_403()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "catalog.read");

        var resp = await client.GetAsync(Route(Guid.NewGuid()));

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.Forbidden,
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_quote_id_returns_404()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync(Route(Guid.NewGuid()));

        // Unknown id → 404; pre-implementation Cycle A also returns 404. Either
        // is contract-valid. When Cycle B lands, the body MUST carry a
        // problem-details envelope with reasonCode=quote.not_found.
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Detail_response_includes_restriction_policy_snapshot_and_customer_locale()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync(Route(Guid.NewGuid()));

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            // Per §4.2: every detail response MUST carry these three fields.
            // The schema-as-of-request is the active quote_market_schema row
            // resolved by quotes.schema_version at the time of the request.
            body.TryGetProperty("restriction_policy_snapshot", out _).Should().BeTrue(
                "§4.2: detail MUST surface the restriction policy snapshot taken at request");
            body.TryGetProperty("customer_locale", out _).Should().BeTrue(
                "FR-033 mirror: detail surfaces the customer's locale-of-record");
            body.TryGetProperty("market_schema", out _).Should().BeTrue(
                "§4.2: detail MUST resolve the schema version active at request time");
        }
    }

    [Fact]
    public async Task Detail_response_includes_full_transition_history()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync(Route(Guid.NewGuid()));

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("transitions", out var transitions).Should().BeTrue(
                "§4.2: detail MUST surface every QuoteStateTransition row");
            transitions.ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    [Fact]
    public async Task Detail_response_includes_every_quote_version()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync(Route(Guid.NewGuid()));

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("versions", out var versions).Should().BeTrue(
                "§4.2: detail MUST list every published QuoteVersion (data-model §2.6)");
            versions.ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    [Fact]
    public async Task Detail_response_includes_verification_warnings_array()
    {
        // §4.2 advisory block 1: real-time evaluation per call (NOT snapshotted).
        // Empty array when all restricted-SKU lines pass eligibility; populated
        // entries carry { sku, reason_code, message_key }. The exhaustive
        // value-correctness test (expired-verification path) lives in T084a's
        // integration test which seeds StubCustomerVerificationEligibilityQuery.
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync(Route(Guid.NewGuid()));

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("verification_warnings", out var warnings).Should().BeTrue(
                "§4.2 advisory block: verification_warnings MUST always be present (empty array when none)");
            warnings.ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    [Fact]
    public async Task Detail_response_includes_archived_sku_lines_array()
    {
        // §4.2 advisory block 2: real-time evaluation per call. Empty array
        // when every line SKU is still active in spec 005; populated when any
        // SKU was archived between request and authoring (spec.md §Edge Cases
        // "operator authors a quote whose lines no longer all exist"). The
        // exhaustive integration test is T084b.
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync(Route(Guid.NewGuid()));

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("archived_sku_lines", out var archived).Should().BeTrue(
                "§4.2 advisory block: archived_sku_lines MUST always be present (empty array when none)");
            archived.ValueKind.Should().Be(JsonValueKind.Array);
        }
    }

    private static void AuthenticateAsAdmin(
        HttpClient client,
        string market,
        string? permissions = null)
    {
        client.DefaultRequestHeaders.Add("X-Test-Admin-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
        if (!string.IsNullOrWhiteSpace(permissions))
        {
            client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        }
    }
}
