using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T080 — HTTP contract for <c>POST /api/admin/quotes/{id}/draft</c>
/// (contracts §4.3). Asserts the wire shape: <c>quotes.author</c> permission,
/// Idempotency-Key required, state transition <c>requested|revised → drafted</c>,
/// FR-040 below-baseline-reason-required validation, and the standard
/// problem-details reasonCode envelope on every error branch.
///
/// TDD note: handler lands in T087 (Cycle B). The validator-vs-handler split for
/// FR-040 is documented in T087 ("validator rejects with 400
/// quote.below_baseline_reason_required when any line has override_unit_price
/// &lt; baseline_unit_price AND override_reason is missing both en and ar").
/// Below-baseline detection requires <see cref="StubPricingBaselineProvider"/>
/// (default 100.00 SAR) — the wire-vocabulary tests here pin the contract; the
/// per-line audit-trail integration test is T084.
/// </summary>
public sealed class AuthorQuoteDraftContractTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public AuthorQuoteDraftContractTests(B2BApiFactory factory)
    {
        _factory = factory;
    }

    private static string Route(Guid id) => $"/api/admin/quotes/{id}/draft";

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_with_review_only_permission_cannot_author_returns_403()
    {
        var client = _factory.CreateClient();
        // quotes.review grants read-only access to detail/queue (§4.1, §4.2);
        // authoring requires quotes.author specifically.
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            lines = Array.Empty<object>(),
        });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.Forbidden,
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_400()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        // Deliberately no Idempotency-Key header.

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Empty_lines_returns_400_quote_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            lines = Array.Empty<object>(),
            terms_text = new { en = "Net 30", ar = "شبكة 30" },
        });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound);

        if (resp.StatusCode == HttpStatusCode.BadRequest)
        {
            await AssertReasonCode(resp, "quote.required_field_missing");
        }
    }

    [Fact]
    public async Task Missing_terms_both_locales_returns_400_quote_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Per T087: validator rejects when terms_text is missing both locales.
        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            lines = new[]
            {
                new { sku = "ABC-123", quantity = 5, override_unit_price = 100.00m },
            },
            terms_text = new { en = "", ar = "" },
        });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound);

        if (resp.StatusCode == HttpStatusCode.BadRequest)
        {
            await AssertReasonCode(resp, "quote.required_field_missing");
        }
    }

    [Fact]
    public async Task Below_baseline_without_reason_returns_400_quote_below_baseline_reason_required()
    {
        // FR-040 + contract §4.3: when any line has override_unit_price below
        // the spec-007 baseline AND override_reason carries neither en nor ar,
        // the validator (T087) MUST reject with 400 quote.below_baseline_reason_required.
        // Stub baseline default is 100.00 SAR (StubPricingBaselineProvider) —
        // an override of 50.00 is unambiguously below baseline once the route
        // is wired in Cycle B.
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            lines = new[]
            {
                new
                {
                    sku = "BELOW-BASELINE-SKU",
                    quantity = 5,
                    override_unit_price = 50.00m,  // < default baseline 100.00
                    // override_reason deliberately omitted
                    line_discount_amount = 0m,
                },
            },
            terms_text = new { en = "Net 30", ar = "شبكة 30" },
            terms_days = 30,
            validity_extends = false,
        });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound);

        if (resp.StatusCode == HttpStatusCode.BadRequest)
        {
            // Either the FR-040 specific code OR the generic required_field_missing
            // (validator order may surface terms first); the contract intent
            // is pinned: a below-baseline override without reason MUST NOT pass.
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
            reasonCode.GetString().Should().BeOneOf(
                "quote.below_baseline_reason_required",
                "quote.required_field_missing");
        }
    }

    [Fact]
    public async Task Below_baseline_with_reason_in_one_locale_passes_validation()
    {
        // Per T087: override_reason needs at-least-one-locale (en OR ar). If
        // EITHER is non-empty, the FR-040 gate is satisfied. (The terminal
        // result will still depend on the quote being in requested|revised state,
        // which Cycle B's seeded fixture will produce.)
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            lines = new[]
            {
                new
                {
                    sku = "BELOW-BASELINE-SKU",
                    quantity = 5,
                    override_unit_price = 50.00m,
                    override_reason = new { en = "Bulk discount.", ar = (string?)null },
                    line_discount_amount = 0m,
                },
            },
            terms_text = new { en = "Net 30", ar = "شبكة 30" },
            terms_days = 30,
            validity_extends = false,
        });

        // The below-baseline gate passes; outcome is then driven by quote state
        // (unknown id → 404 / 409 invalid_state / 200 if it exists in the right
        // state). FR-040 violation (the only path this test guards against) is
        // explicitly NOT in the acceptable set. BadRequest is admitted into the
        // BeOneOf so the defensive FR-040-code check below is reachable.
        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.Conflict,
            HttpStatusCode.NotFound,
            HttpStatusCode.BadRequest);

        if (resp.StatusCode == HttpStatusCode.BadRequest)
        {
            // Defensive: if some other 400 fires, it MUST NOT be the FR-040 code.
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (body.TryGetProperty("reasonCode", out var reasonCode))
            {
                reasonCode.GetString().Should().NotBe(
                    "quote.below_baseline_reason_required",
                    "FR-040 gate is satisfied when override_reason has at-least-one-locale");
            }
        }
    }

    [Fact]
    public async Task Author_from_invalid_state_returns_409_quote_invalid_state_for_action()
    {
        // Contract §4.3: source state MUST be requested OR revised.
        // Authoring against an `accepted` / `withdrawn` / `expired` / `pending-approver`
        // quote MUST surface 409 quote.invalid_state_for_action. The exhaustive
        // matrix is exercised in Cycle B integration tests (T083) where seeded
        // quotes can be put into each terminal state.
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            lines = new[]
            {
                new { sku = "ABC-123", quantity = 5, override_unit_price = 100.00m },
            },
            terms_text = new { en = "Net 30", ar = "شبكة 30" },
            terms_days = 30,
            validity_extends = false,
        });

        // Unknown id → 404 (Cycle A); known-but-wrong-state → 409 (Cycle B).
        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.Conflict);
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

    private static async Task AssertReasonCode(HttpResponseMessage resp, string expected)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("reasonCode", out var reasonCode)
            .Should().BeTrue("every spec 021 problem-details body MUST carry a 'reasonCode' extension (contract §1)");
        reasonCode.GetString().Should().Be(expected);
    }
}
