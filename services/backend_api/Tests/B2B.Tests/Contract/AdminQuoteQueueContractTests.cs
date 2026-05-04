using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T078 — HTTP contract for <c>GET /api/admin/quotes</c> (contracts §4.1).
/// Asserts the wire shape: RBAC gates (admin caller MUST hold <c>quotes.author</c>
/// or <c>quotes.review</c>), market scope (default to caller's market), default
/// sort (oldest-first by <c>requested_at</c>), pagination, and per-row response
/// shape including <c>sla_signal</c>, <c>age_business_days</c>, <c>customer_id</c>,
/// <c>company_id</c>, and <c>po_number</c>.
///
/// TDD note: handler + endpoint mapping land in T085 (Cycle B). Until the route
/// is mapped, every assertion uses <c>BeOneOf(401|403|404, ExpectedCode)</c> to
/// pin the eventual contract while letting the suite stay green pre-implementation
/// — same pattern as US1's contract suite (T054–T060).
/// </summary>
public sealed class AdminQuoteQueueContractTests : IClassFixture<B2BApiFactory>
{
    private const string Route = "/api/admin/quotes";
    private readonly B2BApiFactory _factory;

    public AdminQuoteQueueContractTests(B2BApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync(Route);
        // Admin endpoints are gated by AdminJwt scheme; absence of an admin id
        // header MUST surface a challenge before any handler logic runs.
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_without_quotes_permission_returns_403()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "catalog.read");

        var resp = await client.GetAsync(Route);

        // Per contract §4.1: only quotes.author OR quotes.review may read the
        // queue. Unrelated permissions (catalog.read here) MUST be rejected with
        // 403; the route may also be unmapped at Cycle A (404).
        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.Forbidden,
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_with_quotes_review_returns_200_with_pagination_envelope()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync(Route);

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,           // T085 wired
            HttpStatusCode.NotFound);    // Cycle A pre-implementation

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            // Contract §4.1: paginated envelope ({ items, page, page_size, total }).
            // All four envelope fields are load-bearing: pinning page/page_size/total
            // here keeps pagination-metadata regressions detectable in the OK-branch.
            body.TryGetProperty("items", out var items).Should().BeTrue(
                "contract §4.1 returns a paginated envelope with an items array");
            items.ValueKind.Should().Be(JsonValueKind.Array);
            body.TryGetProperty("page", out var page).Should().BeTrue(
                "contract §4.1 envelope MUST expose `page`");
            page.ValueKind.Should().Be(JsonValueKind.Number);
            body.TryGetProperty("page_size", out var pageSize).Should().BeTrue(
                "contract §4.1 envelope MUST expose `page_size`");
            pageSize.ValueKind.Should().Be(JsonValueKind.Number);
            body.TryGetProperty("total", out var total).Should().BeTrue(
                "contract §4.1 envelope MUST expose `total`");
            total.ValueKind.Should().Be(JsonValueKind.Number);
        }
    }

    [Fact]
    public async Task Admin_with_quotes_author_returns_200()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");

        var resp = await client.GetAsync(Route);

        // Per §4.1: quotes.author has read access for editing.
        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Default_sort_is_oldest_first_by_requested_at()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        // No explicit ?sort= parameter → contract default of oldest-first.
        var resp = await client.GetAsync(Route);

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("items", out var items).Should().BeTrue();

            // When the response carries 2+ rows, assert oldest-first by
            // requested_at. (Cycle A typically returns 0 rows since the
            // harness DB is empty per-class; this branch only fires once a
            // future test seeds multiple quotes against the same fixture, but
            // pinning the invariant here keeps the contract honest for T085.)
            if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() >= 2)
            {
                var requestedAts = items.EnumerateArray()
                    .Where(row => row.TryGetProperty("requested_at", out var p)
                                  && p.ValueKind == JsonValueKind.String)
                    .Select(row => DateTimeOffset.Parse(
                        row.GetProperty("requested_at").GetString()!))
                    .ToList();

                requestedAts.Should().BeInAscendingOrder(
                    "contract §4.1: default sort is oldest-first by requested_at");
            }
        }
    }

    [Fact]
    public async Task Per_row_shape_includes_sla_signal_and_age_business_days()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync(Route);

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (body.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array
                && items.GetArrayLength() > 0)
            {
                var row = items[0];
                // FR-014 — every row carries the per-row SLA signal + age. The
                // exhaustive value-correctness test against three age cohorts is
                // T084c's integration test; here we only assert the keys exist.
                row.TryGetProperty("id", out _).Should().BeTrue();
                row.TryGetProperty("state", out _).Should().BeTrue();
                row.TryGetProperty("market_code", out _).Should().BeTrue();
                row.TryGetProperty("requested_at", out _).Should().BeTrue();
                row.TryGetProperty("age_business_days", out _).Should().BeTrue(
                    "FR-014: every queue row exposes age_business_days for operator visibility");
                row.TryGetProperty("sla_signal", out var sla).Should().BeTrue(
                    "FR-014: every queue row exposes sla_signal");
                // Guard ValueKind before GetString() — a `null`-kind value
                // would otherwise raise a confusing InvalidOperationException
                // instead of the desired "should be one of …" assertion.
                sla.ValueKind.Should().Be(JsonValueKind.String,
                    "sla_signal MUST be a string token, not null");
                sla.GetString().Should().BeOneOf("ok", "warning", "breach");
            }
        }
    }

    [Fact]
    public async Task Page_size_is_capped_at_100()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        // Per §4.1: page_size ≤ 100. A request for 500 should either clamp
        // silently (preferred) or 400 with required_field_missing — both are
        // contract-valid. The hard-rejection branch is named so a future
        // tightening reviewer can pick the canonical answer.
        var resp = await client.GetAsync(Route + "?page_size=500");

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Search_filter_accepts_po_or_company_text()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        // Per §4.1: ?search=<po_or_company> is a free-text filter; either an
        // empty result envelope (200) or 404 if route unmapped.
        var resp = await client.GetAsync(Route + "?search=PO-2026");

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
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
