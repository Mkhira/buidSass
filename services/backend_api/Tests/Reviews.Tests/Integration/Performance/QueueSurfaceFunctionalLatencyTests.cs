using BackendApi.Modules.Reviews.Hooks;
using BackendApi.Modules.Reviews.Admin.ListModerationQueue;
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
using Reviews.Tests.Infrastructure;

namespace Reviews.Tests.Integration.Performance;

/// <summary>
/// Spec 022 T143a — queue-surface functional latency. The SC-006 SLA target
/// is "p95 ≤ 60 s for filter-tripped or media-bearing reviews to appear in
/// the moderator queue". Submission writes the review row + audit row in the
/// same transaction the queue reads from; the functional invariant we assert
/// here is the stronger one: every <c>pending_moderation</c> review appears
/// in <c>GET /queue</c> on the very next read AFTER the submit transaction
/// commits. Wall-clock 60s p95 belongs in a dedicated benchmark project,
/// noted in tasks.md.
/// </summary>
public sealed class QueueSurfaceFunctionalLatencyTests : IAsyncLifetime
{
    private readonly Testcontainers.PostgreSql.PostgreSqlContainer _postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_queue_latency")
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
    public async Task Twenty_pending_moderation_submissions_all_appear_in_queue_on_next_read()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var ids = new List<Guid>();

        for (var i = 0; i < 20; i++)
        {
            var hasMedia = i % 2 == 0;
            var id = await SubmitPendingAsync(hasMedia, clock);
            ids.Add(id);
            clock.Advance(TimeSpan.FromMilliseconds(50));
        }

        await using var queueDb = NewContext();
        var listHandler = new ListModerationQueueHandler(queueDb);
        var response = await listHandler.HandleAsync(
            new ListModerationQueueQuery("pending_moderation", "SA", null, null, null, null, 200),
            CancellationToken.None);

        response.Items.Should().HaveCountGreaterThanOrEqualTo(20,
            "every just-submitted pending_moderation review surfaces in the queue on the next read");
        var queueIds = response.Items.Select(i => i.Id).ToHashSet();
        ids.Should().AllSatisfy(id => queueIds.Should().Contain(id));
    }

    [Fact]
    public async Task Submission_to_queue_visibility_uses_the_pending_started_at_index_for_ordering()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var firstId = await SubmitPendingAsync(hasMedia: true, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        var secondId = await SubmitPendingAsync(hasMedia: true, clock);

        await using var queueDb = NewContext();
        var listHandler = new ListModerationQueueHandler(queueDb);
        var response = await listHandler.HandleAsync(
            new ListModerationQueueQuery("pending_moderation", "SA", null, null, null, null, 200),
            CancellationToken.None);

        var queueIds = response.Items.Select(i => i.Id).ToList();
        var firstIndex = queueIds.IndexOf(firstId);
        var secondIndex = queueIds.IndexOf(secondId);
        firstIndex.Should().BeGreaterThanOrEqualTo(0);
        secondIndex.Should().BeGreaterThanOrEqualTo(0);
        firstIndex.Should().BeLessThan(secondIndex,
            "queue must order by pending_moderation_started_at ASC — oldest first → SLA priority");
    }

    private async Task<Guid> SubmitPendingAsync(bool hasMedia, FakeTimeProvider clock)
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var db = NewContext();
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var profanity = new ProfanityFilter(provider.GetRequiredService<IServiceScopeFactory>(), new ArabicNormalizer(), TimeSpan.Zero);
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var submit = new SubmitReviewHandler(db,
            new FakeOrderLineDeliveryEligibilityQuery(true, null, clock.GetUtcNow().AddDays(-1), Guid.NewGuid()),
            profanity, aggregate, new NullReviewDomainEventPublisher(), clock);

        var body = hasMedia
            ? "Clean text but with a media attachment"
            : "This contains spam to trip the SA wordlist seed term.";
        var media = hasMedia ? new[] { "https://storage.test/abc" } : null;

        var result = await submit.HandleAsync(customerId, "SA",
            new SubmitReviewRequest(productId, 3, "Pending headline", body, "en", media),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("pending_moderation");
        return result.Response.Id;
    }

    private ReviewsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ReviewsDbContext>().UseNpgsql(ConnectionString).Options);

    private async Task SeedSchemasAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var seeder = new BackendApi.Modules.Reviews.Seeding.ReviewsReferenceDataSeeder();
        var ctx = new BackendApi.Features.Seeding.SeedContext(
            null!, provider,
            BackendApi.Features.Seeding.Datasets.DatasetSize.Small,
            new TestHostEnv(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
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
