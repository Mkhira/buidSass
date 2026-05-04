using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Persistence;
using B2B.Tests.Contract.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 T084b — admin Quote-Detail (<c>GET /api/admin/quotes/{id}</c>,
/// contract §4.2) MUST surface a real-time <c>archived_sku_lines</c> advisory
/// block listing every line SKU that is no longer present-and-active in spec
/// 005's product catalog (spec.md §Edge Cases — "operator authors a quote
/// whose lines no longer all exist"). The operator sees this BEFORE
/// publishing so they can either swap the line or abandon the draft.
///
/// Population logic:
/// - Empty array when every line SKU returns true from
///   <see cref="BackendApi.Modules.Shared.IProductCatalogQuery.IsActiveAsync"/>.
/// - Populated when ANY line SKU is in the stub's
///   <see cref="StubProductCatalogQuery.InactiveSkus"/> set.
///
/// <b>Cycle scoping</b>: T086 lands in Cycle B. While the route is unmapped
/// the test pins the contract using <c>BeOneOf(404, ExpectedCode)</c>.
/// </summary>
public sealed class AdminDetailArchivedSkusTests : IClassFixture<B2BApiFactory>
{
    private readonly B2BApiFactory _factory;

    public AdminDetailArchivedSkusTests(B2BApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Archived_sku_on_line_surfaces_in_archived_sku_lines()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        const string archivedSku = "DISCONTINUED-001";
        const string activeSku = "STILL-ACTIVE-002";
        var quoteId = await SeedQuoteWithLinesAsync(customerId, new[] { archivedSku, activeSku });

        // Mark archivedSku as inactive in the catalog stub. The active SKU is
        // not added to InactiveSkus so the stub returns IsActiveAsync=true for it.
        _factory.ProductCatalogQuery.InactiveSkus.Add(archivedSku);

        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync($"/api/admin/quotes/{quoteId}");

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound,    // T086 not yet mapped (Cycle A)
            HttpStatusCode.OK);         // T086 wired (Cycle B+)

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("archived_sku_lines", out var archived).Should().BeTrue();
            archived.ValueKind.Should().Be(JsonValueKind.Array);

            var skus = archived.EnumerateArray()
                .Select(e => e.GetString())
                .ToList();
            skus.Should().Contain(archivedSku,
                "archived SKU MUST appear in the advisory");
            skus.Should().NotContain(activeSku,
                "still-active SKUs MUST NOT appear in the advisory");
        }
    }

    [Fact]
    public async Task All_active_skus_yield_empty_archived_sku_lines_array()
    {
        _factory.ResetStubState();

        var customerId = Guid.NewGuid();
        var quoteId = await SeedQuoteWithLinesAsync(
            customerId,
            new[] { "ACTIVE-A", "ACTIVE-B", "ACTIVE-C" });

        // No InactiveSkus seeded → stub default returns IsActive=true for all.
        var client = _factory.CreateClient();
        AuthenticateAsAdmin(client, market: "ksa", permissions: "quotes.review");

        var resp = await client.GetAsync($"/api/admin/quotes/{quoteId}");

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.OK);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            body.TryGetProperty("archived_sku_lines", out var archived).Should().BeTrue(
                "the field MUST always be present (empty array when no archived) per §4.2");
            archived.ValueKind.Should().Be(JsonValueKind.Array);
            archived.GetArrayLength().Should().Be(0,
                "all-active line SKUs MUST yield an empty archived_sku_lines array");
        }
    }

    private async Task<Guid> SeedQuoteWithLinesAsync(Guid customerId, IReadOnlyCollection<string> skus)
    {
        var quoteId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<B2BDbContext>();

        // Build a minimal cart snapshot with one entry per SKU. T086's archived
        // advisory reads off the originating cart snapshot when no QuoteVersion
        // exists yet (pre-publish state). Use JsonSerializer (rather than string
        // interpolation) so a SKU containing a quote/backslash cannot corrupt
        // the seeded payload — defensive against future SKU shapes.
        var cartPayload = skus.Select(sku => new
        {
            sku,
            quantity = 1,
            line_note = (string?)null,
        }).ToArray();
        var cartSnapshotJson = System.Text.Json.JsonSerializer.Serialize(cartPayload);

        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            CustomerId = customerId,
            CompanyId = null,
            BranchId = null,
            MarketCode = "ksa",
            State = "requested",
            RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
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
            OriginatingCartSnapshotJson = cartSnapshotJson,
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
