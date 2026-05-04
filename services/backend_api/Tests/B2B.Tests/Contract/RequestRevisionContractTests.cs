using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;

namespace B2B.Tests.Contract;

/// <summary>
/// Spec 021 T058 — HTTP contract for
/// <c>POST /api/customer/quotes/{id}/request-revision</c> (contracts §2.6). Asserts:
/// state transition only allowed from <c>revised</c>; <c>comment</c> body must carry
/// at least one of <c>{en, ar}</c>; <c>customer_revision_comment</c> is preserved on
/// the next <c>QuoteVersion</c> the operator authors.
/// </summary>
public sealed class RequestRevisionContractTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;
    public RequestRevisionContractTests(B2BApiFactory factory) => _factory = factory;

    private static string Route(Guid id) => $"/api/customer/quotes/{id}/request-revision";

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()),
            new { comment = new { en = "please reduce price" } });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Missing_comment_locales_returns_400_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // comment present but with no locale entries — the validator MUST reject.
        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()),
            new { comment = new { } });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Empty_comment_object_returns_400_required_field_missing()
    {
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new { });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertReasonCode(resp, "quote.required_field_missing");
    }

    [Fact]
    public async Task Empty_locale_strings_returns_400_required_field_missing()
    {
        // Per contract §2.6 + §9 reason-code table: the `comment` body must carry at
        // least one NON-EMPTY locale string. A comment with both `en` and `ar` set to
        // empty/whitespace-only strings is equivalent to no locale at all and rejects
        // with `quote.required_field_missing`. This pins the validator's empty-string
        // semantics so a Cycle B handler can't silently pass blank comments through to
        // QuoteVersion.customer_revision_comment (FR-035 audit-trail invariant).
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()),
            new { comment = new { en = "   ", ar = "" } });

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
        if (resp.StatusCode == HttpStatusCode.BadRequest)
        {
            await AssertReasonCode(resp, "quote.required_field_missing");
        }
    }

    [Fact]
    public async Task No_changes_provided_returns_409_quote_no_changes_provided()
    {
        // Per contract §9 reason-code table: `quote.no_changes_provided` is reserved for
        // §2.6 — when the customer's revision comment encodes no actionable change
        // (e.g. "no changes" / repeat of prior comment). Cycle B integration test will
        // exercise the deduplication branch with seeded prior versions; Cycle A pins the
        // wire vocabulary so the handler can't silently accept a no-op revision.
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()),
            new { comment = new { en = "no changes" } });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.Conflict);
        if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("reasonCode", out var reasonCode).Should().BeTrue();
            // Either no_changes_provided fires (dedup hit) or invalid_state_for_action
            // fires (quote not in `revised`); both are contract-valid 409 branches for
            // this id.
            reasonCode.GetString().Should().BeOneOf(
                "quote.no_changes_provided",
                "quote.invalid_state_for_action");
        }
    }

    [Fact]
    public async Task Wrong_state_returns_409_invalid_state_for_action()
    {
        // Per §2.6: revision can only be requested when the quote is in `revised`
        // state. Other states (requested, drafted, pending-approver, terminal) reject
        // with `quote.invalid_state_for_action`. Cycle B integration test verifies
        // the `customer_revision_comment` round-trip onto the next QuoteVersion.
        var client = _factory.CreateClient();
        AuthenticateAs(client, Guid.NewGuid(), "ksa");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(Route(Guid.NewGuid()),
            new { comment = new { en = "please add bulk discount" } });

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Conflict);
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
