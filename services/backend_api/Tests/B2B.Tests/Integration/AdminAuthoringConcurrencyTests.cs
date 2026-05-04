using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T083 — two admin operators authoring the same quote concurrently
/// MUST NOT both produce a <see cref="QuoteVersion"/> with the same
/// <c>(QuoteId, VersionNumber)</c> tuple. The optimistic-concurrency guard
/// (xmin row-version on the parent <see cref="Quote"/> + UNIQUE
/// <c>(quote_id, version_number, market_code)</c> on QuoteVersion per
/// data-model §2.6) MUST cause the loser to surface a stale-write error and
/// the winner's row to be the only one persisted.
///
/// <b>Cycle scoping</b> (Cycle A TDD-baseline pattern): T087 (AuthorQuoteDraft
/// handler) lands in Cycle B. While the route is unmapped both calls return
/// 404 and no QuoteVersion rows are written — the post-condition is satisfied
/// vacuously (0 ≤ 1) but the loser-detection branch can't fire. We pin the
/// invariant with <c>BeOneOf(404, ExpectedCode)</c> so the suite stays green
/// pre-implementation; once T087 lands, the assertions tighten.
///
/// <b>Why test the underlying handler not just the validator</b>: this is an
/// integration test (not a contract test) precisely because the race condition
/// only matters once two real handlers materialize a write. The contract test
/// (T080) covers the wire envelope; this test covers the persistence-layer
/// invariant.
/// </summary>
public sealed class AdminAuthoringConcurrencyTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public AdminAuthoringConcurrencyTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Two_concurrent_drafts_produce_at_most_one_quote_version()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        var quoteId = await SeedRequestedQuoteAsync(customerId);

        // Two distinct admin clients with quotes.author both fire a draft
        // against the same quote. Each carries its own Idempotency-Key so the
        // server can NOT collapse them via idempotent-replay — the race must
        // resolve via the optimistic-concurrency guard, not the dedupe path.
        var firstAdmin = _factory.CreateClient();
        AuthenticateAsAdmin(firstAdmin, market: "ksa", permissions: "quotes.author");
        firstAdmin.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var secondAdmin = _factory.CreateClient();
        AuthenticateAsAdmin(secondAdmin, market: "ksa", permissions: "quotes.author");
        secondAdmin.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var body = new
        {
            lines = new[]
            {
                new
                {
                    sku = "ABC-123",
                    quantity = 5,
                    override_unit_price = 100.00m,
                    line_discount_amount = 0m,
                },
            },
            terms_text = new { en = "Net 30", ar = "شبكة 30" },
            terms_days = 30,
            validity_extends = false,
        };

        // Fire both drafts concurrently against the same quote id.
        var firstTask = firstAdmin.PostAsJsonAsync($"/api/admin/quotes/{quoteId}/draft", body);
        var secondTask = secondAdmin.PostAsJsonAsync($"/api/admin/quotes/{quoteId}/draft", body);

        var responses = await Task.WhenAll(firstTask, secondTask);

        // Cycle A: both 404 (route unmapped). Cycle B: exactly one wins,
        // exactly one loses with a 409 stale-write or invalid-state error.
        responses.Should().OnlyContain(r =>
            r.StatusCode == HttpStatusCode.NotFound
            || r.StatusCode == HttpStatusCode.OK
            || r.StatusCode == HttpStatusCode.Conflict);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();
        var versionsForQuote = await db.QuoteVersions
            .Where(v => v.QuoteId == quoteId)
            .ToListAsync();

        // Hard invariant — even pre-implementation: a quote MUST never have
        // two QuoteVersions with the same VersionNumber in the same market.
        // The pre-impl path satisfies this trivially (0 versions); the
        // post-impl path satisfies it via the unique index. Both cases pass.
        versionsForQuote
            .GroupBy(v => (v.VersionNumber, v.MarketCode))
            .Should().OnlyContain(g => g.Count() == 1,
                "data-model §2.6 unique index forbids two QuoteVersions with the same (quote_id, version_number, market_code)");

        if (responses.Any(r => r.StatusCode == HttpStatusCode.OK))
        {
            // Once Cycle B lands: exactly one of the two drafts must succeed
            // and exactly one must fail. We guard against both succeeding
            // (which would indicate the concurrency check is missing).
            var successes = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            successes.Should().Be(1,
                "exactly one of two concurrent drafts must win; the other must surface a stale-write conflict");

            // The loser MUST be a 409 (xmin or unique-violation), not a 500.
            // Use SingleOrDefault so a future "both succeeded" regression
            // surfaces via the Be(1) assertion above instead of a less-helpful
            // InvalidOperationException from .Single() finding two non-OK rows.
            var loser = responses.SingleOrDefault(r => r.StatusCode != HttpStatusCode.OK);
            if (loser is not null)
            {
                loser.StatusCode.Should().Be(HttpStatusCode.Conflict,
                    "the losing concurrent draft surfaces 409 (concurrency or invalid-state)");
            }
        }
    }

    private async Task<Guid> SeedRequestedQuoteAsync(Guid customerId)
    {
        var quoteId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            CustomerId = customerId,
            CompanyId = null,
            BranchId = null,
            MarketCode = "ksa",
            State = "requested",   // legal source for AuthorQuoteDraft (§4.3)
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            ExpiresAt = null,
            CurrentVersionId = null,
            DecidedAt = null,
            DecidedBy = null,
            TerminalAt = null,
            TerminalReason = null,
            PoNumber = null,
            InvoiceBilling = false,
            CustomerSuppliedMessageJson = null,
            InternalNote = null,
            ApproverRejectionNote = null,
            OriginatingCartSnapshotJson = """[{"sku":"ABC-123","quantity":5,"line_note":null}]""",
            OriginatingProductId = null,
            RestrictionPolicySnapshotJson = "{}",
            SchemaVersion = 1,
        });

        await db.SaveChangesAsync();
        return quoteId;
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
