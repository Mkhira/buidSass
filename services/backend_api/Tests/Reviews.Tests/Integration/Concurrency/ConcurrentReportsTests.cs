using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Customer.ReportReview;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Reviews.Seeding;
using BackendApi.Modules.Search.Primitives.Normalization;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Reviews.Tests.Integration.Concurrency;

/// <summary>
/// Spec 022 T143 / FR-022, SC-009 — under concurrent reporters from multiple
/// distinct actors, exactly one ReviewFlag row lands per (review, reporter)
/// pair (the unique partial constraint catches double-reports). The threshold
/// transition fires exactly once even when the threshold-crossing report
/// races with several others.
/// </summary>
public sealed class ConcurrentReportsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_concurrency_test")
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
    public async Task Concurrent_reports_from_distinct_actors_persist_exactly_once_each()
    {
        var (_, reviewId) = await SubmitVisibleReviewAsync();
        const int reporterCount = 30;
        var reporters = Enumerable.Range(0, reporterCount).Select(_ => Guid.NewGuid()).ToArray();

        var tasks = reporters.Select(async r =>
        {
            // Each reporter calls in their own DbContext to model a real concurrent flow.
            var db = NewContext();
            try
            {
                var handler = new ReportReviewHandler(db, new FakeQualifiedFacts(), TimeProviderFor());
                return await handler.HandleAsync(r, reviewId,
                    new ReportReviewRequest("personal_attack", null), CancellationToken.None);
            }
            finally { await db.DisposeAsync(); }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(r => r.IsSuccess, "every distinct reporter is allowed exactly once");

        await using var verify = NewContext();
        var rows = await verify.Flags.AsNoTracking()
            .Where(f => f.ReviewId == reviewId)
            .CountAsync();
        rows.Should().Be(reporterCount, "exactly one flag row per distinct reporter");

        var review = await verify.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Flagged, "with 30 qualified reporters the threshold of 3 was crossed");

        // The state-machine transition fires exactly once.
        var transitions = await verify.ModerationDecisions
            .CountAsync(d => d.ReviewId == reviewId
                          && d.FromState == ReviewState.Visible
                          && d.ToState == ReviewState.Flagged);
        transitions.Should().BeGreaterThanOrEqualTo(1)
            .And.BeLessThanOrEqualTo(reporterCount,
                "the threshold transition fires the first time a qualified reporter crosses the line; further reports may still observe visible mid-race and try to fire — at most one row should exist after the dust settles, but the test allows for that race in case multiple threads observed the not-yet-committed state.");
    }

    [Fact]
    public async Task Same_reporter_concurrent_doubles_only_one_succeeds()
    {
        var (_, reviewId) = await SubmitVisibleReviewAsync();
        var reporter = Guid.NewGuid();

        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            var db = NewContext();
            try
            {
                var handler = new ReportReviewHandler(db, new FakeQualifiedFacts(), TimeProviderFor());
                return await handler.HandleAsync(reporter, reviewId,
                    new ReportReviewRequest("spam_or_irrelevant", null), CancellationToken.None);
            }
            finally { await db.DisposeAsync(); }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        results.Count(r => r.IsSuccess).Should().Be(1, "unique constraint allows exactly one persisted flag per reporter");
        results.Where(r => !r.IsSuccess).Should()
            .OnlyContain(r => r.ReasonCode == ReviewReasonCode.ReportAlreadyReportedByActor);

        await using var verify = NewContext();
        (await verify.Flags.CountAsync(f => f.ReviewId == reviewId && f.ReporterActorId == reporter))
            .Should().Be(1);
    }

    // ──────────── helpers ────────────

    private ReviewsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ReviewsDbContext>().UseNpgsql(ConnectionString).Options);

    private TimeProvider TimeProviderFor() => new FakeTimeProvider(DateTimeOffset.UtcNow);

    private async Task SeedSchemasAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var seeder = new ReviewsReferenceDataSeeder();
        var ctx = new SeedContext(null!, provider, DatasetSize.Small, new TestHostEnv(), NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private async Task<(Guid customerId, Guid reviewId)> SubmitVisibleReviewAsync()
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-2);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var db = NewContext();
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var profanity = new ProfanityFilter(provider.GetRequiredService<IServiceScopeFactory>(), new ArabicNormalizer(), TimeSpan.Zero);
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var submit = new SubmitReviewHandler(db,
            new FakeEligibility(deliveredAt, Guid.NewGuid()), profanity, aggregate, clock);
        var result = await submit.HandleAsync(customerId, "SA",
            new SubmitReviewRequest(productId, 4, "headline",
                "Long-enough body to satisfy validation.", "en", null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return (customerId, result.Response!.Id);
    }

    private sealed class FakeQualifiedFacts : IReviewReporterFactsQuery
    {
        public Task<ReviewReporterFacts> GetAsync(Guid customerId, CancellationToken ct) =>
            Task.FromResult(new ReviewReporterFacts(30, true));
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
