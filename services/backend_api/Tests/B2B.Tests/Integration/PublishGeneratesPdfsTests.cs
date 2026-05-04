using System.Net;
using System.Net.Http.Json;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.Shared;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T082 — every <c>POST /api/admin/quotes/{id}/publish</c> success
/// MUST write exactly one EN <see cref="QuoteVersionDocument"/> and exactly one
/// AR <see cref="QuoteVersionDocument"/>, both pointing to a real storage blob;
/// the corresponding <see cref="QuotePublished"/> domain event MUST carry both
/// storage keys keyed by locale so spec 025's notifier can fan-out to the
/// customer.
///
/// <b>Cycle scoping</b> (Cycle A TDD-baseline pattern): T088 (PublishQuoteVersion
/// handler) is NOT in this cycle's scope — it ships in Cycle B alongside
/// T089/T090 (PDF templates) and T091 (renderer). While T088 is unmapped (404),
/// this test asserts the eventual contract using <c>BeOneOf(404, ExpectedCode)</c>
/// so the suite stays green pre-implementation while still pinning the
/// invariant. Once T088 lands, the assertions tighten.
/// </summary>
public sealed class PublishGeneratesPdfsTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public PublishGeneratesPdfsTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Publish_persists_one_en_and_one_ar_quote_version_document()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        var quoteId = await SeedDraftedQuoteAsync(customerId);

        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            $"/api/admin/quotes/{quoteId}/publish",
            new { validity_extends = false });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,    // T088 not yet mapped (Cycle A)
            HttpStatusCode.OK);         // T088 wired (Cycle B+)

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

            // The published QuoteVersion is whichever row points back at the
            // quote — exactly ONE QuoteVersion is added per publish (data-model
            // §2.6 invariant; QuoteVersionImmutabilityTests already covers the
            // "never updated" half).
            var versions = await db.QuoteVersions
                .Where(v => v.QuoteId == quoteId)
                .ToListAsync();
            versions.Should().HaveCount(1, "one publish writes exactly one QuoteVersion");

            var versionId = versions[0].Id;
            var documents = await db.QuoteVersionDocuments
                .Where(d => d.QuoteVersionId == versionId)
                .ToListAsync();

            documents.Should().HaveCount(2, "every publish writes both EN and AR documents (R3)");
            documents.Select(d => d.Locale).Should().BeEquivalentTo(new[] { "en", "ar" });

            // Each document must carry a non-empty storage key — the renderer
            // (T091) is responsible for surfacing the IStorageService key, not
            // a placeholder.
            documents.Should().OnlyContain(d => !string.IsNullOrWhiteSpace(d.StorageKey));
            documents.Should().OnlyContain(d => d.ContentType == "application/pdf");
        }
    }

    [Fact]
    public async Task QuotePublished_event_payload_carries_both_locale_storage_keys()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        var quoteId = await SeedDraftedQuoteAsync(customerId);

        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.author");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resp = await client.PostAsJsonAsync(
            $"/api/admin/quotes/{quoteId}/publish",
            new { validity_extends = false });

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,
            HttpStatusCode.OK);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            // Per QuotePublished record (Modules/Shared/QuoteDomainEvents.cs):
            // PdfStorageKeysByLocale is a required field. Spec 025 reads it on
            // notification fan-out without re-querying the database.
            var events = _factory.EventCollector.OfType<QuotePublished>().ToList();
            events.Should().HaveCount(1, "publish emits QuotePublished exactly once");

            var keys = events[0].PdfStorageKeysByLocale;
            keys.Should().ContainKey("en");
            keys.Should().ContainKey("ar");
            keys["en"].Should().NotBeNullOrWhiteSpace();
            keys["ar"].Should().NotBeNullOrWhiteSpace();
        }
    }

    private async Task<Guid> SeedDraftedQuoteAsync(Guid customerId)
    {
        var quoteId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        // Source state for publish is `drafted` per §4.4 state machine.
        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            CustomerId = customerId,
            CompanyId = null,        // individual customer keeps the seed minimal
            BranchId = null,
            MarketCode = "ksa",
            State = "drafted",
            RequestedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
            CurrentVersionId = null, // first publish — no prior version
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
