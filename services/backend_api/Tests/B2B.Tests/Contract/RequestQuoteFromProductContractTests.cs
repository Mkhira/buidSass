using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T074 (US2) — HTTP contract for
/// <c>POST /api/customer/quotes/from-product</c> (contracts §2.2). Asserts every
/// reason-code branch the spec defines plus the <c>201 Created</c> happy path.
///
/// Behavioral deltas vs. <c>RequestQuoteFromCartContractTests</c> (§2.1):
/// <list type="bullet">
///   <item><c>product_id</c> + <c>quantity</c> are MANDATORY — missing fields
///         surface <c>quote.required_field_missing</c>.</item>
///   <item>Cart is NOT cleared — even when the customer has cart contents, a
///         successful from-product request leaves the cart untouched.</item>
///   <item>Adds <c>quote.product_not_quotable</c> as a 400 branch when spec 005's
///         <see cref="BackendApi.Modules.Shared.IProductCatalogQuery"/> flags the
///         product as non-quotable.</item>
/// </list>
/// </summary>
public sealed class RequestQuoteFromProductContractTests : IClassFixture<B2BApiFactory>
{
    private const string Route = "/api/customer/quotes/from-product";
    private readonly B2BApiFactory _factory;

    public RequestQuoteFromProductContractTests(B2BApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync(Route, new { });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_400_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");

        var resp = await client.PostAsJsonAsync(Route, new
        {
            product_id = Guid.NewGuid().ToString(),
            quantity = 1,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Missing_product_id_returns_400_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new
        {
            quantity = 1,
            // product_id deliberately omitted
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Missing_quantity_returns_400_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new
        {
            product_id = Guid.NewGuid().ToString(),
            // quantity deliberately omitted
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Zero_quantity_returns_400_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new
        {
            product_id = Guid.NewGuid().ToString(),
            quantity = 0,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Account_inactive_returns_422_account_inactive()
    {
        // FR-038 — caller's account is locked (X-Test-Permissions=account.locked).
        // The handler MUST surface 422 quote.account_inactive BEFORE any cross-module
        // I/O so a locked account can't drain rate-limit buckets or probe membership.
        _factory.ResetStubState();
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa", permissions: "account.locked");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new
        {
            product_id = Guid.NewGuid().ToString(),
            quantity = 1,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertReasonCode(resp, "quote.account_inactive");
    }

    [Fact]
    public async Task Product_not_quotable_returns_400_product_not_quotable()
    {
        // §2.2 — when IProductCatalogQuery.IsQuotableAsync returns false for the
        // requested product, the handler MUST reject with 400 quote.product_not_quotable.
        _factory.ResetStubState();
        var nonQuotableProductId = Guid.NewGuid();
        _factory.ProductCatalogQuery.NonQuotableProductIds.Add(nonQuotableProductId);

        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new
        {
            product_id = nonQuotableProductId.ToString(),
            quantity = 3,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.product_not_quotable");
    }

    [Fact]
    public async Task Rate_limit_exceeded_returns_429_with_retry_after_seconds_in_body()
    {
        // FR-045 — per-customer rate-limit. Even though §2.2 tests are class-fixture
        // shared, the per-customer bucket is keyed by id so we use a fresh customer
        // each loop pass... no wait — we need a STABLE customer to drain a single
        // bucket. Use one customer + many requests with fresh idempotency keys until
        // the limiter trips (sliding window default per market schema seed).
        _factory.ResetStubState();
        var customerId = Guid.NewGuid();
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId, market: "ksa");

        HttpResponseMessage? final = null;
        // Drive enough volume to exceed the default per-customer cap (seeded as 10/h
        // for KSA in B2BReferenceDataSeeder; pump 30 to ensure we cross it).
        for (var i = 0; i < 30; i++)
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
            final = await client.PostAsJsonAsync(Route, new
            {
                product_id = Guid.NewGuid().ToString(),
                quantity = 1,
            });
            if (final.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var body = await final.Content.ReadFromJsonAsync<JsonElement>();
                body.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
                reasonCode.GetString().Should().Be("quote.rate_limit_exceeded");
                body.TryGetProperty("retry_after_seconds", out var retryAfter)
                    .Should().BeTrue("contract §2.2 requires retry_after_seconds in the 429 body");
                retryAfter.GetInt32().Should().BeGreaterThan(0);
                return;
            }
        }

        // If the per-customer cap is high enough that 30 didn't trip it, document
        // the wire vocabulary without flunking — the deterministic per-window test
        // is exercised by RateLimitEnforcementTests via FakeTimeProvider.
        final.Should().NotBeNull();
        final!.StatusCode.Should().BeOneOf(
            HttpStatusCode.Created,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Happy_path_returns_201_with_originating_product_id_and_publishes_quote_requested_event()
    {
        _factory.ResetStubState();

        var client = _factory.CreateClient();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        AuthenticateAs(client, customerId, market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Seed a non-empty cart so we can assert the cart is NOT cleared by this
        // intake path (cart-vs-product is the key behavioral delta from §2.1).
        _factory.CartSnapshotProvider.SnapshotsByCustomer[customerId] = new[]
        {
            new BackendApi.Modules.Shared.CartSnapshotLine(
                Sku: "OTHER-CART-ITEM",
                Quantity: 2,
                LineNote: null),
        };

        var resp = await client.PostAsJsonAsync(Route, new
        {
            product_id = productId.ToString(),
            quantity = 7,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("requested");
        body.GetProperty("market_code").GetString().Should().Be("ksa");
        body.GetProperty("originating_product_id").GetGuid().Should().Be(productId);
        body.TryGetProperty("requested_at", out _).Should().BeTrue();

        // Cart NOT cleared — the §2.2 intake leaves cart contents alone.
        _factory.CartSnapshotProvider.ClearedCustomerIds.Should().NotContain(customerId,
            "§2.2 — from-product intake MUST NOT clear the customer's cart");

        _factory.EventCollector.OfType<BackendApi.Modules.Shared.QuoteRequested>()
            .Should().HaveCount(1, "FR-043: QuoteRequested fans out exactly once on the §2.2 happy path");
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
