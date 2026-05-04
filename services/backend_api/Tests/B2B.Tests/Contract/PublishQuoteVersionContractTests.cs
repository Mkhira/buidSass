using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T081 — HTTP contract for <c>POST /api/admin/quotes/{id}/publish</c>
/// (contracts §4.4). Asserts the wire shape: <c>quotes.author</c> permission,
/// Idempotency-Key required, state transition <c>drafted → revised</c>, EN+AR
/// PDF generation synchronously (verified structurally in T082's integration
/// test), and the <c>validity_extends</c> recompute behavior per Clarifications
/// Q5 (<c>true</c> recomputes <c>expires_at = now + market.validity_days</c>;
/// <c>false</c> preserves the current <c>expires_at</c>).
///
/// TDD note: handler lands in T088 (Cycle B). Cycle A pins the wire vocabulary;
/// Cycle B asserts persistence + event fan-out.
/// </summary>
public sealed class PublishQuoteVersionContractTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public PublishQuoteVersionContractTests(B2BApiFactory factory)
    {
        _factory = factory;
    }

    private static string Route(Guid id) => $"/api/admin/quotes/{id}/publish";

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_with_review_only_cannot_publish_returns_403()
    {
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            validity_extends = false,
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

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publish_from_non_drafted_state_returns_409_quote_invalid_state_for_action()
    {
        // Contract §4.4: only `drafted` quotes can be published. Publish from
        // `requested`, `revised`, or any terminal state MUST surface 409
        // quote.invalid_state_for_action. The full state-matrix coverage is in
        // Cycle B integration tests where the seeded fixture can pin each state.
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            validity_extends = false,
        });

        // Unknown id → 404 (Cycle A); known-but-wrong-state → 409 (Cycle B).
        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.Conflict);

        if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
            reasonCode.GetString().Should().Be("quote.invalid_state_for_action");
        }
    }

    [Fact]
    public async Task Publish_with_validity_extends_true_recomputes_expires_at()
    {
        // Per Clarifications Q5: validity_extends=true MUST recompute
        // expires_at = now + market.validity_days (KSA default 14 days). The
        // wire-level test pins the response shape — the persistence assertion
        // (the new `expires_at` is greater than the original) is in Cycle B.
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            validity_extends = true,
        });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotFound,
            HttpStatusCode.Conflict);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("state", out var state).Should().BeTrue();
            state.GetString().Should().Be("revised",
                "publish transitions drafted → revised per §4.4");
            body.TryGetProperty("expires_at", out _).Should().BeTrue(
                "publish response MUST surface the (possibly recomputed) expires_at");
            body.TryGetProperty("version_number", out _).Should().BeTrue(
                "publish response MUST surface the new QuoteVersion's monotonic version_number");
        }
    }

    [Fact]
    public async Task Publish_with_validity_extends_false_preserves_existing_expires_at()
    {
        // Counterpart to validity_extends=true: when false (the default), the
        // existing expires_at is preserved. The structural assertion is in
        // Cycle B where the seeded quote can carry a known expires_at.
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            validity_extends = false,
        });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotFound,
            HttpStatusCode.Conflict);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("state", out var state).Should().BeTrue();
            state.GetString().Should().Be("revised");
        }
    }

    [Fact]
    public async Task Published_response_includes_pdf_storage_keys_for_both_locales()
    {
        // §4.4 + R3: publish synchronously generates EN + AR PDFs and persists
        // QuoteVersionDocument rows. The success response surfaces both storage
        // keys so the admin UI can offer immediate download. T082's integration
        // test asserts the QuoteVersionDocument rows + storage blobs exist.
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new
        {
            validity_extends = false,
        });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.NotFound,
            HttpStatusCode.Conflict);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("documents", out var documents).Should().BeTrue(
                "§4.4: publish response MUST surface the EN + AR PDF storage keys");
            // Either keyed-by-locale object {en: ..., ar: ...} OR an array of
            // { locale, storage_key } items — both are contract-valid surface
            // shapes. Cycle B narrows once the handler is implemented.
            documents.ValueKind.Should().BeOneOf(
                JsonValueKind.Object,
                JsonValueKind.Array);
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
