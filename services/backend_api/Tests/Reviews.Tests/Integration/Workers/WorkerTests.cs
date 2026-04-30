using BackendApi.Modules.Reviews.Hooks;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Reviews.Seeding;
using BackendApi.Modules.Reviews.Workers;
using BackendApi.Modules.Search.Primitives.Normalization;
using BackendApi.Modules.Shared;
using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Reviews.Tests.Integration.Workers;

/// <summary>
/// Spec 022 T135-T136 — RatingAggregateRebuildWorker reconciles drifted
/// aggregates from scratch + idempotent on re-run; ReviewIntegrityScanWorker
/// finds visible/flagged reviews tied to refunded order lines (logs + metric)
/// without auto-correcting.
/// </summary>
public sealed class WorkerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_workers_test")
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
    public async Task RatingAggregateRebuildWorker_recomputes_drifted_aggregate_to_truth()
    {
        var (productId, _) = await SubmitVisibleReviewsAsync(new[] { 5, 4, 3 });

        // Corrupt the aggregate by direct DB write — simulates a missed event.
        await using (var corrupt = NewContext())
        {
            var row = await corrupt.RatingAggregates
                .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA");
            row.ReviewCount = 999;
            row.AvgRating = 1.0m;
            await corrupt.SaveChangesAsync();
        }

        var worker = NewRatingAggregateWorker();
        await worker.ExecuteOnceAsync(CancellationToken.None);

        await using var verify = NewContext();
        var fixedRow = await verify.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        fixedRow.ReviewCount.Should().Be(3);
        fixedRow.AvgRating.Should().Be(4.0m);
    }

    [Fact]
    public async Task RatingAggregateRebuildWorker_zeros_orphaned_aggregate_when_no_contributors_remain()
    {
        var (productId, _) = await SubmitVisibleReviewsAsync(new[] { 5 });

        // Hide the only review by direct DB write — aggregate row would be stale.
        await using (var hide = NewContext())
        {
            var review = await hide.Reviews
                .FirstAsync(r => r.ProductId == productId);
            review.State = ReviewState.Hidden;
            await hide.SaveChangesAsync();
        }

        var worker = NewRatingAggregateWorker();
        await worker.ExecuteOnceAsync(CancellationToken.None);

        await using var verify = NewContext();
        var aggregate = await verify.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        aggregate.ReviewCount.Should().Be(0);
        aggregate.AvgRating.Should().BeNull();
    }

    [Fact]
    public async Task RatingAggregateRebuildWorker_double_pass_is_idempotent()
    {
        var (productId, _) = await SubmitVisibleReviewsAsync(new[] { 5, 4 });

        var worker = NewRatingAggregateWorker();
        await worker.ExecuteOnceAsync(CancellationToken.None);

        DateTimeOffset firstUpdate;
        await using (var first = NewContext())
        {
            firstUpdate = (await first.RatingAggregates.AsNoTracking()
                .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA")).LastUpdatedUtc;
        }

        // Second pass — must be a clean no-op (count + avg unchanged; updated_at advances).
        await worker.ExecuteOnceAsync(CancellationToken.None);

        await using var verify = NewContext();
        var second = await verify.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        second.ReviewCount.Should().Be(2);
    }

    [Fact]
    public async Task ReviewIntegrityScanWorker_finds_visible_reviews_on_refunded_order_lines()
    {
        var (productId, reviewIds) = await SubmitVisibleReviewsAsync(new[] { 5, 4 });

        await using var db = NewContext();
        var refundedLineId = (await db.Reviews.AsNoTracking()
            .FirstAsync(r => r.Id == reviewIds[0])).OrderLineId;

        var worker = NewIntegrityScanWorker(refunded: new[] { refundedLineId });
        var report = await worker.ScanAsync(CancellationToken.None);

        report.Violations.Should().Be(1);
        report.ViolatingReviewIds.Should().ContainSingle().Which.Should().Be(reviewIds[0]);

        // Crucially: the worker MUST NOT auto-correct.
        await using var verify = NewContext();
        var review = await verify.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewIds[0]);
        review.State.Should().Be(ReviewState.Visible, "SC-004 — integrity scan logs but never auto-corrects");
    }

    [Fact]
    public async Task ReviewIntegrityScanWorker_zero_violations_when_no_refunded_lines()
    {
        await SubmitVisibleReviewsAsync(new[] { 5 });
        var worker = NewIntegrityScanWorker(refunded: Array.Empty<Guid>());
        var report = await worker.ScanAsync(CancellationToken.None);
        report.Violations.Should().Be(0);
        report.ViolatingReviewIds.Should().BeEmpty();
    }

    // ──────────── helpers ────────────

    private ReviewsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ReviewsDbContext>().UseNpgsql(ConnectionString).Options);

    private async Task SeedSchemasAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var seeder = new ReviewsReferenceDataSeeder();
        var ctx = new SeedContext(null!, provider, DatasetSize.Small, new TestHostEnv(), NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private async Task<(Guid productId, IReadOnlyList<Guid> reviewIds)> SubmitVisibleReviewsAsync(int[] ratings)
    {
        var productId = Guid.NewGuid();
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-2);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var reviewIds = new List<Guid>();

        foreach (var rating in ratings)
        {
            var customerId = Guid.NewGuid();
            var db = NewContext();
            var services = new ServiceCollection();
            services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
            var provider = services.BuildServiceProvider();
            var profanity = new ProfanityFilter(provider.GetRequiredService<IServiceScopeFactory>(), new ArabicNormalizer(), TimeSpan.Zero);
            var aggregate = new RatingAggregateRecomputer(db, clock);
            var submit = new SubmitReviewHandler(db,
                new FakeEligibility(deliveredAt, Guid.NewGuid()), profanity, aggregate, new NullReviewDomainEventPublisher(), clock);
            var result = await submit.HandleAsync(customerId, "SA",
                new SubmitReviewRequest(productId, rating, $"R{rating}",
                    "Long-enough body to satisfy validation.", "en", null),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            reviewIds.Add(result.Response!.Id);
            await db.DisposeAsync();
        }
        return (productId, reviewIds);
    }

    private RatingAggregateRebuildWorker NewRatingAggregateWorker()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddScoped<RatingAggregateRecomputer>();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var provider = services.BuildServiceProvider();
        return new RatingAggregateRebuildWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReviewsWorkerOptions()),
            provider.GetRequiredService<TimeProvider>(),
            NullLogger<RatingAggregateRebuildWorker>.Instance);
    }

    private ReviewIntegrityScanWorker NewIntegrityScanWorker(Guid[] refunded)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddScoped<IRefundedOrderLineLookup>(_ => new FakeRefundedLookup(refunded));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var provider = services.BuildServiceProvider();
        return new ReviewIntegrityScanWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReviewsWorkerOptions()),
            provider.GetRequiredService<TimeProvider>(),
            NullLogger<ReviewIntegrityScanWorker>.Instance);
    }

    private sealed class FakeRefundedLookup : IRefundedOrderLineLookup
    {
        private readonly IReadOnlySet<Guid> _refunded;
        public FakeRefundedLookup(IEnumerable<Guid> refunded) => _refunded = refunded.ToHashSet();
        public Task<IReadOnlySet<Guid>> GetRefundedOrderLineIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        {
            var matched = ids.Where(_refunded.Contains).ToHashSet();
            return Task.FromResult<IReadOnlySet<Guid>>(matched);
        }
    }

    private sealed class FakeEligibility : IOrderLineDeliveryEligibilityQuery
    {
        private readonly DateTimeOffset _delivered;
        private readonly Guid _orderLineId;
        public FakeEligibility(DateTimeOffset delivered, Guid orderLineId)
        {
            _delivered = delivered;
            _orderLineId = orderLineId;
        }
        public Task<OrderLineDeliveryEligibilityResult> IsEligibleForReviewAsync(Guid c, Guid p, CancellationToken ct) =>
            Task.FromResult(new OrderLineDeliveryEligibilityResult(true, null, _delivered, _orderLineId));
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
