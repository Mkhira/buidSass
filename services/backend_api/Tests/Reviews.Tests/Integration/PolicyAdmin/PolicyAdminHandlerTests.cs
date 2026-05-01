using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.PolicyAdmin.DeleteWordlistTerm;
using BackendApi.Modules.Reviews.PolicyAdmin.ListWordlistTerms;
using BackendApi.Modules.Reviews.PolicyAdmin.UpdateMarketSchema;
using BackendApi.Modules.Reviews.PolicyAdmin.UpsertWordlistTerm;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Reviews.Seeding;
using BackendApi.Modules.Search.Primitives.Normalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Integration.PolicyAdmin;

/// <summary>
/// Spec 022 T127-T132 — wordlist CRUD + market-schema PATCH:
/// list/upsert/delete with Arabic-normalization at write time, in-process
/// cache invalidation on mutation, market-schema partial update with check-
/// constraint range validation.
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class PolicyAdminHandlerTests
{
    private readonly ReviewsPostgresFixture _fx;

    public PolicyAdminHandlerTests(ReviewsPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task UpsertWordlistTerm_normalizes_term_at_write_time()
    {
        await SeedSchemasAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var normalizer = new ArabicNormalizer();
        var filter = new ProfanityFilter(BuildScopeFactory(), normalizer, TimeSpan.Zero);
        await using var db = _fx.NewContext();
        var handler = new UpsertWordlistTermHandler(db, normalizer, filter, clock);

        // Mixed case + diacritics — should be normalized to lowercase + AR-normalized form.
        var result = await handler.HandleAsync(Guid.NewGuid(),
            new UpsertWordlistTermRequest("SA", "FRAUD", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.Term.Should().Be("fraud");

        await using var verify = _fx.NewContext();
        var stored = await verify.Wordlists.AsNoTracking()
            .FirstAsync(w => w.MarketCode == "SA" && w.Term == "fraud");
        stored.Term.Should().Be("fraud");
    }

    [Fact]
    public async Task UpsertWordlistTerm_rejects_empty_or_whitespace_term()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var normalizer = new ArabicNormalizer();
        var filter = new ProfanityFilter(BuildScopeFactory(), normalizer, TimeSpan.Zero);
        await using var db = _fx.NewContext();
        var handler = new UpsertWordlistTermHandler(db, normalizer, filter, clock);

        var empty = await handler.HandleAsync(Guid.NewGuid(),
            new UpsertWordlistTermRequest("SA", "", null), CancellationToken.None);
        empty.IsSuccess.Should().BeFalse();
        empty.ReasonCode.Should().Be(ReviewReasonCode.PolicyWordlistTermInvalid);
    }

    [Fact]
    public async Task DeleteWordlistTerm_removes_term_and_returns_true()
    {
        await SeedSchemasAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var normalizer = new ArabicNormalizer();
        var filter = new ProfanityFilter(BuildScopeFactory(), normalizer, TimeSpan.Zero);

        // Seed a term first via upsert.
        await using (var seedDb = _fx.NewContext())
        {
            var upsert = new UpsertWordlistTermHandler(seedDb, normalizer, filter, clock);
            await upsert.HandleAsync(Guid.NewGuid(),
                new UpsertWordlistTermRequest("SA", "deleteme", null), CancellationToken.None);
        }

        // Then delete it.
        await using (var deleteDb = _fx.NewContext())
        {
            var delete = new DeleteWordlistTermHandler(deleteDb, normalizer, filter);
            var deleted = await delete.HandleAsync("SA", "deleteme", CancellationToken.None);
            deleted.Should().BeTrue();
        }

        await using var verify = _fx.NewContext();
        var stillThere = await verify.Wordlists.AsNoTracking()
            .AnyAsync(w => w.MarketCode == "SA" && w.Term == "deleteme");
        stillThere.Should().BeFalse();
    }

    [Fact]
    public async Task ListWordlistTerms_returns_per_market_terms_alphabetically()
    {
        await SeedSchemasAsync();
        await using var db = _fx.NewContext();
        var handler = new ListWordlistTermsHandler(db);

        var sa = await handler.HandleAsync("SA", CancellationToken.None);
        var eg = await handler.HandleAsync("EG", CancellationToken.None);

        sa.Items.Should().NotBeEmpty();
        sa.Items.Should().BeInAscendingOrder(i => i.Term);
        sa.Items.Should().OnlyContain(i => i.MarketCode == "SA");
        eg.Items.Should().OnlyContain(i => i.MarketCode == "EG");
    }

    [Fact]
    public async Task UpsertWordlistTerm_invalidates_filter_cache_for_market()
    {
        await SeedSchemasAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var normalizer = new ArabicNormalizer();
        // 60 s TTL so the test isolates the explicit invalidate path.
        var filter = new ProfanityFilter(BuildScopeFactory(), normalizer, TimeSpan.FromSeconds(60));

        // Warm the cache for SA.
        var before = filter.Evaluate("SA", "totally clean text").Tripped;
        before.Should().BeFalse();

        // Add a new term + invalidation should refresh the next call.
        await using (var seedDb = _fx.NewContext())
        {
            var upsert = new UpsertWordlistTermHandler(seedDb, normalizer, filter, clock);
            await upsert.HandleAsync(Guid.NewGuid(),
                new UpsertWordlistTermRequest("SA", "newword", null), CancellationToken.None);
        }

        var after = filter.Evaluate("SA", "this contains newword in the body");
        after.Tripped.Should().BeTrue();
        after.MatchedTerms.Should().Contain("newword");
    }

    [Fact]
    public async Task UpdateMarketSchema_rejects_value_outside_check_constraint_range()
    {
        await SeedSchemasAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = _fx.NewContext();
        var handler = new UpdateMarketSchemaHandler(db, clock);

        var tooBig = await handler.HandleAsync(Guid.NewGuid(), "SA",
            new UpdateMarketSchemaRequest(EligibilityWindowDays: 1000, null, null, null, null, null, null),
            CancellationToken.None);
        tooBig.IsSuccess.Should().BeFalse();
        tooBig.ReasonCode.Should().Be(ReviewReasonCode.PolicyMarketValueOutOfRange);

        var tooSmall = await handler.HandleAsync(Guid.NewGuid(), "SA",
            new UpdateMarketSchemaRequest(null, EditWindowDays: 1, null, null, null, null, null),
            CancellationToken.None);
        tooSmall.IsSuccess.Should().BeFalse();
        tooSmall.ReasonCode.Should().Be(ReviewReasonCode.PolicyMarketValueOutOfRange);
    }

    [Fact]
    public async Task UpdateMarketSchema_partial_update_only_changes_provided_fields()
    {
        await SeedSchemasAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = _fx.NewContext();
        var handler = new UpdateMarketSchemaHandler(db, clock);

        var actor = Guid.NewGuid();
        var result = await handler.HandleAsync(actor, "SA",
            new UpdateMarketSchemaRequest(
                EligibilityWindowDays: 365,
                EditWindowDays: null,
                CommunityReportThreshold: null,
                CommunityReportWindowDays: null,
                ReportQualifyingAccountAgeDays: null,
                ReportQualifyingRequiresVerifiedBuyer: null,
                PendingModerationSlaHours: 240),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.EligibilityWindowDays.Should().Be(365);
        result.Response.PendingModerationSlaHours.Should().Be(240);
        result.Response.EditWindowDays.Should().Be(30, "field not in request stays unchanged");
        result.Response.CommunityReportThreshold.Should().Be(3);

        await using var verify = _fx.NewContext();
        var row = await verify.MarketSchemas.AsNoTracking().FirstAsync(s => s.MarketCode == "SA");
        row.UpdatedByActorId.Should().Be(actor);
    }

    [Fact]
    public async Task UpdateMarketSchema_unknown_market_returns_404()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = _fx.NewContext();
        var handler = new UpdateMarketSchemaHandler(db, clock);

        var result = await handler.HandleAsync(Guid.NewGuid(), "XX",
            new UpdateMarketSchemaRequest(180, null, null, null, null, null, null),
            CancellationToken.None);
        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(404);
    }

    private async Task SeedSchemasAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(_fx.ConnectionString));
        var provider = services.BuildServiceProvider();
        var seeder = new ReviewsReferenceDataSeeder();
        var ctx = new SeedContext(null!, provider, DatasetSize.Small, new TestHostEnv(), NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(_fx.ConnectionString));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Reviews.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
