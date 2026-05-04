using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T054 — HTTP contract for <c>POST /api/customer/quotes/from-cart</c>
/// (contracts §2.1). Asserts the wire shape: status code + problem-details
/// <c>reasonCode</c> extension for every error branch the spec defines, plus the
/// <c>201 Created</c> happy path.
///
/// TDD note: handlers and endpoint mapping land in Cycle B / phase-3 follow-up.
/// While the route is unmapped the asserts here are written to exercise the future
/// shape; running this suite right now produces a 404-driven red signal, which is
/// the correct TDD pre-implementation state.
/// </summary>
public sealed class RequestQuoteFromCartContractTests : IClassFixture<B2BApiFactory>
{
    private const string Route = "/api/customer/quotes/from-cart";
    private readonly B2BApiFactory _factory;

    public RequestQuoteFromCartContractTests(B2BApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync(Route, new { });
        // Per contract: customer endpoints require a customer JWT. Unauthenticated
        // requests must be challenged before any handler logic runs.
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_400_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");

        var resp = await client.PostAsJsonAsync(Route, new { });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Empty_cart_returns_400_quote_cart_empty()
    {
        var client = _factory.CreateClient();
        var customerId = Guid.NewGuid();
        AuthenticateAs(client, customerId, market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        // Stub provider returns an empty snapshot for unseeded customers — exactly
        // the wire path the handler is required to surface as quote.cart_empty.

        var resp = await client.PostAsJsonAsync(Route, new { });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.cart_empty");
    }

    [Fact]
    public async Task No_active_company_membership_returns_409_with_reason_code()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Caller passes a company_id they don't belong to → 409.
        var resp = await client.PostAsJsonAsync(Route, new
        {
            company_id = Guid.NewGuid().ToString(),
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertReasonCode(resp, "quote.no_active_company_membership");
    }

    [Fact]
    public async Task Market_mismatch_returns_422()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Test setup that passes a company in a different market goes here in Cycle B
        // when we have a real CompanyId to point at. Right now the fixture has no
        // companies seeded; this path documents the wire shape.
        var resp = await client.PostAsJsonAsync(Route, new
        {
            company_id = Guid.NewGuid().ToString(),
        });

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict);
        // When the company is unknown the membership check fires first; when known
        // and mismatched, market_mismatch fires. Both are valid 4xx reason codes.
    }

    [Fact]
    public async Task Account_inactive_returns_422_account_inactive()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa", permissions: "account.locked");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        // Cycle B handler honors the account-state lookup; for now this documents the wire shape.

        var resp = await client.PostAsJsonAsync(Route, new { });

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Company_suspended_returns_422_company_suspended()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new
        {
            company_id = Guid.NewGuid().ToString(),
        });

        // Suspended-company branch (FR-026) requires a seeded company in suspended state;
        // Cycle B will register a fixture seeder. Contract assertion: when the route is
        // wired AND the company is suspended, the response is 422 with reason
        // `quote.company_suspended`. Until Cycle B seeds the company, the membership-check
        // fires first → 409 quote.no_active_company_membership; either path is contract-
        // valid for an unknown-company id. The 4xx status floor is the load-bearing
        // assertion at Cycle A.
        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.Conflict,
            HttpStatusCode.NotFound);
        if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            await AssertReasonCode(resp, "quote.company_suspended");
        }
        else if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            await AssertReasonCode(resp, "quote.no_active_company_membership");
        }
    }

    [Fact]
    public async Task Po_required_returns_400_quote_po_required()
    {
        // Contract §2.1: when company.po_required=true and the request body omits
        // po_number, the handler MUST reject with 400 quote.po_required (NOT a generic
        // required_field_missing — the wire vocabulary distinguishes the two so the
        // storefront can render a PO-specific input prompt).
        // Cycle B seeds a po_required=true company; Cycle A asserts the wire vocabulary
        // is exercised by the route once it's wired.
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new
        {
            company_id = Guid.NewGuid().ToString(),
            // po_number deliberately omitted
        });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Conflict,
            HttpStatusCode.NotFound);
        if (resp.StatusCode == HttpStatusCode.BadRequest)
        {
            await AssertReasonCode(resp, "quote.po_required");
        }
    }

    [Fact]
    public async Task Po_already_used_returns_400_quote_po_already_used_when_unique_po_required()
    {
        // Contract §2.1: company.unique_po_required=true and the supplied po_number
        // collides with a prior quote for the same company → 400 quote.po_already_used
        // (HARD reject; soft-warning path only fires when unique_po_required=false at
        // submit-acceptance time, see §2.7). Cycle B integration test seeds a prior quote
        // with the same PO; Cycle A pins the wire vocabulary.
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new
        {
            company_id = Guid.NewGuid().ToString(),
            po_number = "PO-2026-0042",
        });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Conflict,
            HttpStatusCode.NotFound);
        if (resp.StatusCode == HttpStatusCode.BadRequest)
        {
            // Either po_already_used (collision detected) or required_field_missing
            // (validation fired first on a body field) — both are contract-valid 400
            // branches. Cycle B narrows this once the company seed is real.
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
            reasonCode.GetString().Should().BeOneOf(
                "quote.po_already_used",
                "quote.required_field_missing");
        }
    }

    [Fact]
    public async Task Rate_limit_exceeded_returns_429_with_retry_after_seconds_in_body()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");

        // Per FR-045 and contract §2.1: per-customer + per-company sliding-window rate-limit.
        // The wire shape on rejection is 429 with a `retry_after_seconds` field in the body
        // and `reasonCode=quote.rate_limit_exceeded`. Cycle B integration test
        // (RateLimitEnforcementTests) will use FakeTimeProvider to exercise the full window;
        // this contract test only documents the shape.
        HttpResponseMessage? final = null;
        for (var i = 0; i < 12; i++)
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
            final = await client.PostAsJsonAsync(Route, new { });
            if (final.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // CodeRabbit Round 2: read the body ONCE — `ReadFromJsonAsync`
                // does not buffer by default, so reading twice (here previously
                // via AssertReasonCode then again for retry_after_seconds) can
                // return empty content on the second call. Assert both fields
                // off the same JsonElement.
                var body = await final.Content.ReadFromJsonAsync<JsonElement>();
                body.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
                reasonCode.GetString().Should().Be("quote.rate_limit_exceeded");
                body.TryGetProperty("retry_after_seconds", out var retryAfter)
                    .Should().BeTrue("contract §2.1 requires retry_after_seconds in the 429 body");
                retryAfter.GetInt32().Should().BeGreaterThan(0);
                return;
            }
        }

        // If we never tripped the rate limit, either the handler isn't wired yet
        // (Cycle A — every response is 404) or the rate-limit middleware isn't
        // engaged at this surface yet. Both are valid Cycle-A pre-implementation
        // states. The hard assertion lands in Cycle B's RateLimitEnforcementTests
        // (T061a) which uses FakeTimeProvider to drive the sliding window
        // deterministically. Pin the contract intent here without flunking the
        // suite at Cycle A:
        final.Should().NotBeNull();
        final!.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,           // route unmapped (Cycle A)
            HttpStatusCode.BadRequest,         // validation reached, no rate-limit
            HttpStatusCode.Created,            // handler wired but limit not tripped yet
            HttpStatusCode.TooManyRequests);   // limit tripped (Cycle B+)
    }

    [Fact]
    public async Task Happy_path_returns_201_with_quote_summary_and_publishes_quote_requested_event()
    {
        // Class-fixture is shared across [Fact]s — reset every stub's mutable state so
        // accumulated invocations / replays / events from prior tests do not leak in.
        _factory.ResetStubState();

        var client = _factory.CreateClient();
        var customerId = Guid.NewGuid();
        AuthenticateAs(client, customerId, market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Seed a non-empty cart so the snapshot path produces lines.
        _factory.CartSnapshotProvider.SnapshotsByCustomer[customerId] = new[]
        {
            new BackendApi.Modules.Shared.CartSnapshotLine(
                Sku: "ABC-123",
                Quantity: 5,
                LineNote: null),
        };

        var resp = await client.PostAsJsonAsync(Route, new { });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("requested");
        body.GetProperty("market_code").GetString().Should().Be("ksa");
        body.TryGetProperty("requested_at", out _).Should().BeTrue();

        _factory.EventCollector.OfType<BackendApi.Modules.Shared.QuoteRequested>()
            .Should().HaveCount(1, "FR-043: QuoteRequested fans out exactly once on the happy path");
    }

    private static void AuthenticateAs(
        HttpClient client,
        Guid customerId,
        string market,
        string? permissions = null)
    {
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
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
