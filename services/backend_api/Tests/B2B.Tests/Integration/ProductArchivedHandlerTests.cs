using BackendApi.Modules.B2B.Entities;
using BackendApi.Modules.B2B.Hooks;
using BackendApi.Modules.B2B.Persistence;
using BackendApi.Modules.B2B.Primitives;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace B2B.Tests.Integration;

/// <summary>
/// Spec 021 task T144 — drives <see cref="ProductArchivedHandler"/> against a real
/// Postgres (Testcontainers) and verifies:
///
/// <list type="bullet">
///   <item>A <c>revised</c> quote whose <c>QuoteVersion.LineItemsJson</c>
///         references the archived SKU receives a <c>product_archived:</c>
///         flag in <c>internal_note</c>.</item>
///   <item>A <c>requested</c> quote whose <c>OriginatingCartSnapshotJson</c>
///         references the archived SKU is also flagged.</item>
///   <item>Quotes that don't reference the SKU are untouched.</item>
///   <item>Terminal quotes (accepted / rejected / etc.) are untouched even when
///         they reference the SKU.</item>
///   <item>Re-delivery of the same event does not append duplicate hints.</item>
/// </list>
/// </summary>
public sealed class ProductArchivedHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("b2b_product_archived")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
    private B2BDbContext _db = default!;
    private ProductArchivedHandler _handler = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var options = new DbContextOptionsBuilder<B2BDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        _db = new B2BDbContext(options);
        await _db.Database.MigrateAsync();
        _handler = new ProductArchivedHandler(_db, _clock, NullLogger<ProductArchivedHandler>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Flags_revised_quote_referencing_archived_sku()
    {
        var matchedId = await SeedRevisedQuoteWithVersionAsync(skuOnLine: "SKU-ARC-1");
        var unrelatedId = await SeedRevisedQuoteWithVersionAsync(skuOnLine: "SKU-OTHER");

        await _handler.OnProductArchivedAsync(
            new ProductArchived(Guid.NewGuid(), "SKU-ARC-1", Guid.NewGuid(), _clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var matched = await verify.Quotes.SingleAsync(q => q.Id == matchedId);
        matched.InternalNote.Should().NotBeNull().And.Contain("product_archived:SKU-ARC-1");

        var unrelated = await verify.Quotes.SingleAsync(q => q.Id == unrelatedId);
        unrelated.InternalNote.Should().BeNull("a quote without the archived SKU MUST NOT be flagged");
    }

    [Fact]
    public async Task Flags_requested_quote_via_originating_cart_snapshot()
    {
        var id = await SeedRequestedQuoteAsync(cartSku: "SKU-CART-7");

        await _handler.OnProductArchivedAsync(
            new ProductArchived(Guid.NewGuid(), "SKU-CART-7", Guid.NewGuid(), _clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var quote = await verify.Quotes.SingleAsync(q => q.Id == id);
        quote.InternalNote.Should().NotBeNull().And.Contain("product_archived:SKU-CART-7");
    }

    [Fact]
    public async Task Skips_terminal_quotes()
    {
        var acceptedId = await SeedRevisedQuoteWithVersionAsync(skuOnLine: "SKU-T-1", overrideState: "accepted");

        await _handler.OnProductArchivedAsync(
            new ProductArchived(Guid.NewGuid(), "SKU-T-1", Guid.NewGuid(), _clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var quote = await verify.Quotes.SingleAsync(q => q.Id == acceptedId);
        quote.InternalNote.Should().BeNull("terminal quotes are out of scope");
    }

    [Fact]
    public async Task Idempotent_on_redelivery()
    {
        var id = await SeedRevisedQuoteWithVersionAsync(skuOnLine: "SKU-IDEMP");

        await _handler.OnProductArchivedAsync(
            new ProductArchived(Guid.NewGuid(), "SKU-IDEMP", Guid.NewGuid(), _clock.GetUtcNow()),
            CancellationToken.None);
        await _handler.OnProductArchivedAsync(
            new ProductArchived(Guid.NewGuid(), "SKU-IDEMP", Guid.NewGuid(), _clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var quote = await verify.Quotes.SingleAsync(q => q.Id == id);
        var occurrences = (quote.InternalNote ?? string.Empty).Split("product_archived:SKU-IDEMP").Length - 1;
        occurrences.Should().Be(1, "the second delivery MUST NOT append a duplicate hint");
    }

    private B2BDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<B2BDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new B2BDbContext(options);
    }

    private async Task<Guid> SeedRevisedQuoteWithVersionAsync(string skuOnLine, string overrideState = "revised")
    {
        var quoteId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // Quote.CurrentVersionId → QuoteVersion.Id and QuoteVersion.QuoteId →
        // Quote.Id form a circular FK; insert in two passes (quote first
        // without CurrentVersionId, then the version, then patch the FK).
        var quote = new Quote
        {
            Id = quoteId,
            CustomerId = customerId,
            CompanyId = null,
            BranchId = null,
            MarketCode = "ksa",
            State = overrideState,
            RequestedAt = _clock.GetUtcNow().AddDays(-1),
            ExpiresAt = _clock.GetUtcNow().AddDays(7),
            CurrentVersionId = null,
            CustomerSuppliedMessageJson = null,
            RestrictionPolicySnapshotJson = "{}",
            SchemaVersion = 1,
            TerminalAt = overrideState is "accepted" or "rejected" ? _clock.GetUtcNow() : null,
            TerminalReason = overrideState is "accepted" or "rejected" ? overrideState : null,
        };
        _db.Quotes.Add(quote);
        _db.QuoteStateTransitions.Add(new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = quoteId,
            MarketCode = "ksa",
            PriorState = "__none__",
            NewState = overrideState,
            ActorKind = QuoteActorKind.Customer.ToToken(),
            ActorId = customerId,
            ReasonJson = null,
            MetadataJson = "{}",
            OccurredAt = _clock.GetUtcNow().AddDays(-1),
        });
        await _db.SaveChangesAsync();

        _db.QuoteVersions.Add(new QuoteVersion
        {
            Id = versionId,
            QuoteId = quoteId,
            MarketCode = "ksa",
            VersionNumber = 1,
            AuthoredBy = Guid.NewGuid(),
            PublishedAt = _clock.GetUtcNow().AddHours(-1),
            LineItemsJson = $"[{{\"sku\":\"{skuOnLine}\",\"qty\":1}}]",
            TermsTextJson = "{\"en\":\"Net 30\",\"ar\":\"صافي 30\"}",
            TermsDays = 30,
            ValidityExtends = false,
            TotalsSummaryJson = "{}",
        });
        await _db.SaveChangesAsync();

        quote.CurrentVersionId = versionId;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return quoteId;
    }

    private async Task<Guid> SeedRequestedQuoteAsync(string cartSku)
    {
        var id = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        _db.Quotes.Add(new Quote
        {
            Id = id,
            CustomerId = customerId,
            CompanyId = null,
            BranchId = null,
            MarketCode = "ksa",
            State = "requested",
            RequestedAt = _clock.GetUtcNow().AddDays(-1),
            ExpiresAt = _clock.GetUtcNow().AddDays(7),
            CustomerSuppliedMessageJson = null,
            RestrictionPolicySnapshotJson = "{}",
            SchemaVersion = 1,
            OriginatingCartSnapshotJson = $"[{{\"sku\":\"{cartSku}\",\"qty\":2}}]",
        });
        _db.QuoteStateTransitions.Add(new QuoteStateTransition
        {
            Id = Guid.NewGuid(),
            QuoteId = id,
            MarketCode = "ksa",
            PriorState = "__none__",
            NewState = "requested",
            ActorKind = QuoteActorKind.Customer.ToToken(),
            ActorId = customerId,
            ReasonJson = null,
            MetadataJson = "{}",
            OccurredAt = _clock.GetUtcNow().AddDays(-1),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return id;
    }
}
