using BackendApi.Modules.Reviews.Hooks;
using BackendApi.Modules.Reviews.Admin.DecideModeration;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Search.Primitives.Normalization;
using BackendApi.Modules.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Reviews.Tests.Integration.Aggregate;

/// <summary>
/// Spec 022 T112 — light soak: the rating aggregate stays consistent across
/// a series of mixed lifecycle transitions (submit → hide → reinstate →
/// delete). <c>last_updated_utc</c> advances on every counted-state-affecting
/// transition; review_count + avg_rating + distribution always reflect the
/// current truth (visible + flagged rows only).
///
/// Skips the wall-clock 60s SC-005 assertion intentionally — the recompute
/// happens inline in the same transaction as the transition, so the
/// functional invariant is much stronger than the SLA. A perf test would
/// add the wall-clock dimension, deferred to a dedicated benchmark project.
/// </summary>
public sealed class AggregateRefreshSoakTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_aggregate_soak")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var ctx = NewContext();
        await ctx.Database.MigrateAsync();
        await SeedSchemasAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Submit_hide_reinstate_cycle_keeps_aggregate_consistent()
    {
        var productId = Guid.NewGuid();
        var ratings = new[] { 5, 4, 3, 5, 2, 4, 4, 5, 3, 5 };
        var reviewIds = new List<Guid>();

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        // Submit 10 visible reviews.
        foreach (var rating in ratings)
        {
            var id = await SubmitVisibleAsync(productId, rating, clock);
            reviewIds.Add(id);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        await using (var afterSubmit = NewContext())
        {
            var aggregate = await afterSubmit.RatingAggregates.AsNoTracking()
                .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA");
            aggregate.ReviewCount.Should().Be(ratings.Length);
            aggregate.AvgRating.Should().Be(4.0m);
            aggregate.Distribution2.Should().Be(1);
            aggregate.Distribution3.Should().Be(2);
            aggregate.Distribution4.Should().Be(3);
            aggregate.Distribution5.Should().Be(4);
        }

        // Hide every other review.
        var prevLastUpdated = await ReadLastUpdatedAsync(productId);
        for (var i = 0; i < reviewIds.Count; i += 2)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await HideAsync(reviewIds[i], clock);
        }

        var afterHides = await ReadAggregateAsync(productId);
        afterHides.ReviewCount.Should().Be(5,
            "hiding 5 of 10 visible reviews drops review_count to 5");
        afterHides.LastUpdatedUtc.Should().BeAfter(prevLastUpdated);

        // Reinstate one of the hidden reviews. last_updated_utc must advance,
        // count returns to 6.
        prevLastUpdated = afterHides.LastUpdatedUtc;
        clock.Advance(TimeSpan.FromMinutes(1));
        await ReinstateAsync(reviewIds[0], clock);

        var afterReinstate = await ReadAggregateAsync(productId);
        afterReinstate.ReviewCount.Should().Be(6);
        afterReinstate.LastUpdatedUtc.Should().BeAfter(prevLastUpdated);
    }

    [Fact]
    public async Task Aggregate_drops_to_zero_when_every_review_leaves_counted_set()
    {
        var productId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var idA = await SubmitVisibleAsync(productId, 5, clock);
        var idB = await SubmitVisibleAsync(productId, 3, clock);

        clock.Advance(TimeSpan.FromMinutes(1));
        await HideAsync(idA, clock);
        await HideAsync(idB, clock);

        var aggregate = await ReadAggregateAsync(productId);
        aggregate.ReviewCount.Should().Be(0);
        aggregate.AvgRating.Should().BeNull("FR-028 — null avg when count = 0");
        aggregate.Distribution1.Should().Be(0);
        aggregate.Distribution5.Should().Be(0);
    }

    // ──────────── helpers ────────────

    private ReviewsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ReviewsDbContext>().UseNpgsql(ConnectionString).Options);

    private async Task SeedSchemasAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var seeder = new BackendApi.Modules.Reviews.Seeding.ReviewsReferenceDataSeeder();
        var ctx = new BackendApi.Features.Seeding.SeedContext(
            null!,
            provider,
            BackendApi.Features.Seeding.Datasets.DatasetSize.Small,
            new TestHostEnv(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private async Task<Guid> SubmitVisibleAsync(Guid productId, int rating, FakeTimeProvider clock)
    {
        var customerId = Guid.NewGuid();
        var db = NewContext();
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var profanity = new ProfanityFilter(provider.GetRequiredService<IServiceScopeFactory>(), new ArabicNormalizer(), TimeSpan.Zero);
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var submit = new SubmitReviewHandler(db,
            new FakeOrderLineDeliveryEligibilityQuery(true, null, clock.GetUtcNow().AddDays(-1), Guid.NewGuid()),
            profanity, aggregate, new NullReviewDomainEventPublisher(), clock);
        var result = await submit.HandleAsync(customerId, "SA",
            new SubmitReviewRequest(productId, rating, $"R{rating}",
                "Long-enough body to satisfy validation.", "en", null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        await db.DisposeAsync();
        return result.Response!.Id;
    }

    private async Task HideAsync(Guid reviewId, FakeTimeProvider clock)
    {
        await using var db = NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var handler = new DecideModerationHandler(db, aggregate, new NullReviewDomainEventPublisher(), clock);
        var result = await handler.HandleAsync(
            Guid.NewGuid(), hasModerator: true, hasSuperAdmin: false,
            reviewId, ifMatchRowVersion: null,
            new DecideModerationRequest("hidden", "Long enough hide reason for soak test.", null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private async Task ReinstateAsync(Guid reviewId, FakeTimeProvider clock)
    {
        await using var db = NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var handler = new DecideModerationHandler(db, aggregate, new NullReviewDomainEventPublisher(), clock);
        var result = await handler.HandleAsync(
            Guid.NewGuid(), hasModerator: true, hasSuperAdmin: false,
            reviewId, ifMatchRowVersion: null,
            new DecideModerationRequest("visible", null, "Long enough admin note for soak test."),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private async Task<DateTimeOffset> ReadLastUpdatedAsync(Guid productId)
    {
        await using var db = NewContext();
        return (await db.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA")).LastUpdatedUtc;
    }

    private async Task<BackendApi.Modules.Reviews.Entities.ProductRatingAggregate> ReadAggregateAsync(Guid productId)
    {
        await using var db = NewContext();
        return await db.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA");
    }

    private sealed class TestHostEnv : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Microsoft.Extensions.Hosting.Environments.Development;
        public string ApplicationName { get; set; } = "Reviews.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
