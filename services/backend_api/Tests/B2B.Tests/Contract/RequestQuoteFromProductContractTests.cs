using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using BackendApi.Modules.Shared;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T074 — HTTP contract for <c>POST /api/customer/quotes/from-product</c>
/// (contracts §2.2). Mirrors <see cref="RequestQuoteFromCartContractTests"/> with the
/// from-product-specific surfaces: required <c>product_id</c> + <c>quantity</c>, the
/// <c>quote.product_not_quotable</c> branch, the cart-NOT-cleared invariant, and the
/// 201 happy path emitting <see cref="QuoteRequested"/>.
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

        var resp = await client.PostAsJsonAsync(Route, new { quantity = 1 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Missing_quantity_returns_400_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new { product_id = Guid.NewGuid().ToString() });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Zero_quantity_is_rejected_400()
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
    public async Task Product_not_quotable_returns_400_product_not_quotable()
    {
        _factory.ResetStubState();
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var productId = Guid.NewGuid();
        _factory.ProductCatalogQuery.NonQuotableProductIds.Add(productId);

        var resp = await client.PostAsJsonAsync(Route, new
        {
            product_id = productId.ToString(),
            quantity = 3,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.product_not_quotable");
    }

    [Fact]
    public async Task No_active_company_membership_returns_409()
    {
        _factory.ResetStubState();
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new
        {
            product_id = Guid.NewGuid().ToString(),
            quantity = 2,
            company_id = Guid.NewGuid().ToString(),
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertReasonCode(resp, "quote.no_active_company_membership");
    }

    [Fact]
    public async Task Branch_without_company_returns_400_required_field_missing()
    {
        _factory.ResetStubState();
        var client = _factory.CreateClient();
        AuthenticateAs(client, customerId: Guid.NewGuid(), market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route, new
        {
            product_id = Guid.NewGuid().ToString(),
            quantity = 1,
            branch_id = Guid.NewGuid().ToString(),
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Happy_path_returns_201_and_publishes_quote_requested_event_without_clearing_cart()
    {
        _factory.ResetStubState();
        var client = _factory.CreateClient();
        var customerId = Guid.NewGuid();
        AuthenticateAs(client, customerId, market: "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Seed the customer's cart so we can prove from-product does NOT clear it.
        _factory.CartSnapshotProvider.SnapshotsByCustomer[customerId] = new[]
        {
            new CartSnapshotLine(Sku: "OTHER-999", Quantity: 2, LineNote: null),
        };

        var productId = Guid.NewGuid();

        var resp = await client.PostAsJsonAsync(Route, new
        {
            product_id = productId.ToString(),
            quantity = 5,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be("requested");
        body.GetProperty("market_code").GetString().Should().Be("ksa");
        body.GetProperty("originating_product_id").GetGuid().Should().Be(productId);
        body.TryGetProperty("requested_at", out _).Should().BeTrue();

        _factory.EventCollector.OfType<QuoteRequested>()
            .Should().HaveCount(1, "FR-043: QuoteRequested fans out exactly once");

        // Cart-NOT-cleared invariant (contract §2.2). The from-cart slice clears
        // the cart via SnapshotAndClearAsync; from-product MUST NOT touch the cart.
        _factory.CartSnapshotProvider.ClearedCustomerIds
            .Should().NotContain(customerId, "contract §2.2: from-product MUST NOT clear the cart");
    }

    private static void AuthenticateAs(HttpClient client, Guid customerId, string market)
    {
        client.DefaultRequestHeaders.Add("X-Test-Customer-Id", customerId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Market", market);
    }

    private static async Task AssertReasonCode(HttpResponseMessage resp, string expected)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("reasonCode", out var reasonCode)
            .Should().BeTrue("every spec 021 problem-details body MUST carry a 'reasonCode' extension (contract §1)");
        reasonCode.GetString().Should().Be(expected);
    }
}
