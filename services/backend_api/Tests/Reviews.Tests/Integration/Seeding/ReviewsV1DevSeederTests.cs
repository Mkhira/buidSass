using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Reviews.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Integration.Seeding;

/// <summary>
/// Spec 022 T120-T123 — dev / staging seeder coverage:
/// idempotent across two runs, every state has ≥ 1 row (SC-008),
/// production environment short-circuits, dev environment seeds.
/// </summary>
[Collection(nameof(ReviewsPostgresCollection))]
public sealed class ReviewsV1DevSeederTests
{
    private readonly ReviewsPostgresFixture _fx;

    public ReviewsV1DevSeederTests(ReviewsPostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Seeder_idempotent_across_two_runs()
    {
        await PrimeReferenceDataAsync();
        var seeder = new ReviewsV1DevSeeder();
        var ctx = MakeSeedContext(Environments.Development);

        await seeder.ApplyAsync(ctx, CancellationToken.None);
        var (firstReviews, firstFlags, firstDecisions) = await CountAsync();

        await seeder.ApplyAsync(ctx, CancellationToken.None);
        var (secondReviews, secondFlags, secondDecisions) = await CountAsync();

        secondReviews.Should().Be(firstReviews);
        secondFlags.Should().Be(firstFlags);
        secondDecisions.Should().Be(firstDecisions);
    }

    [Fact]
    public async Task Seeder_state_coverage_includes_every_state()
    {
        await PrimeReferenceDataAsync();
        await new ReviewsV1DevSeeder().ApplyAsync(
            MakeSeedContext(Environments.Development), CancellationToken.None);

        await using var db = _fx.NewContext();
        var states = await db.Reviews.AsNoTracking()
            .Select(r => r.State)
            .Distinct()
            .ToListAsync();
        states.Should().Contain(new[]
        {
            ReviewState.Visible,
            ReviewState.PendingModeration,
            ReviewState.Flagged,
            ReviewState.Hidden,
            ReviewState.Deleted,
        }, "SC-008 — synthetic dataset must cover every state");
    }

    [Fact]
    public async Task Seeder_short_circuits_in_production_environment()
    {
        await PrimeReferenceDataAsync();

        // Other tests in this collection may have already populated the
        // synthetic dataset (xUnit doesn't guarantee test ordering and the
        // ReviewsPostgresFixture is shared). Measure deltas, not absolutes.
        int before;
        await using (var pre = _fx.NewContext())
        {
            before = await pre.Reviews.CountAsync();
        }

        var seeder = new ReviewsV1DevSeeder();
        var ctx = MakeSeedContext(Environments.Production);
        await seeder.ApplyAsync(ctx, CancellationToken.None);

        int after;
        await using (var post = _fx.NewContext())
        {
            after = await post.Reviews.CountAsync();
        }
        (after - before).Should().Be(0,
            "dev seeder MUST NOT insert rows in Production environment");
    }

    [Fact]
    public async Task Seeder_creates_aggregate_rows_for_visible_and_flagged_products()
    {
        await PrimeReferenceDataAsync();
        var seeder = new ReviewsV1DevSeeder();
        var ctx = MakeSeedContext(Environments.Development, includeRecomputer: true);

        await seeder.ApplyAsync(ctx, CancellationToken.None);

        await using var db = _fx.NewContext();
        var aggregates = await db.RatingAggregates.AsNoTracking().ToListAsync();
        aggregates.Should().NotBeEmpty();
        aggregates.Should().OnlyContain(a => a.ReviewCount > 0);
    }

    private async Task PrimeReferenceDataAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(_fx.ConnectionString));
        var provider = services.BuildServiceProvider();
        var seeder = new ReviewsReferenceDataSeeder();
        var ctx = new SeedContext(null!, provider, DatasetSize.Small, new TestHostEnv(), NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private SeedContext MakeSeedContext(string environmentName, bool includeRecomputer = false)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(_fx.ConnectionString));
        if (includeRecomputer)
        {
            services.AddScoped<RatingAggregateRecomputer>();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));
        }
        var provider = services.BuildServiceProvider();
        return new SeedContext(null!, provider, DatasetSize.Small,
            new TestHostEnv { EnvironmentName = environmentName }, NullLogger.Instance);
    }

    private async Task<(int reviews, int flags, int decisions)> CountAsync()
    {
        await using var db = _fx.NewContext();
        return (
            await db.Reviews.CountAsync(),
            await db.Flags.CountAsync(),
            await db.ModerationDecisions.CountAsync());
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
