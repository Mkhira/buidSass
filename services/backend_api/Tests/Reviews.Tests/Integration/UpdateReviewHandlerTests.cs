using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Customer.UpdateReview;
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

namespace Reviews.Tests.Integration;

/// <summary>
/// Spec 022 T058–T060 + T070–T071 — UpdateReview within / after window,
/// not-author guard, edit-trips-filter, edit during pending re-stamps the
/// pending-started clock + advances xmin (R9).
/// </summary>
public sealed class UpdateReviewHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_update_test")
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
    public async Task Edit_within_window_updates_fields_advances_xmin_increments_edit_count()
    {
        var (customerId, reviewId, _, _) = await SubmitVisibleReviewAsync();
        await using var beforeCtx = NewContext();
        var beforeXmin = await beforeCtx.Reviews.AsNoTracking()
            .Where(r => r.Id == reviewId).Select(r => r.Xmin).FirstAsync();

        var handler = NewUpdateHandler(out _);
        var result = await handler.HandleAsync(
            customerId, reviewId, ifMatchRowVersion: null,
            new UpdateReviewRequest(Rating: 4, Headline: "Edited", Body: null, Locale: null, MediaUrls: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.EditCount.Should().Be(1);
        result.Response.RowVersion.Should().NotBe(beforeXmin);

        await using var db = NewContext();
        var row = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        row.Rating.Should().Be(4);
        row.Headline.Should().Be("Edited");
        row.State.Should().Be(ReviewState.Visible);
    }

    [Fact]
    public async Task Edit_after_window_returns_window_closed()
    {
        var (customerId, reviewId, _, clock) = await SubmitVisibleReviewAsync();
        clock.Advance(TimeSpan.FromDays(40)); // > default 30-day window

        var handler = NewUpdateHandler(clock);
        var result = await handler.HandleAsync(
            customerId, reviewId, null,
            new UpdateReviewRequest(Rating: 3, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(400);
        result.ReasonCode.Should().Be(ReviewReasonCode.EditWindowClosed);
    }

    [Fact]
    public async Task Edit_by_non_author_returns_not_author()
    {
        var (_, reviewId, _, _) = await SubmitVisibleReviewAsync();
        var imposter = Guid.NewGuid();

        var handler = NewUpdateHandler(out _);
        var result = await handler.HandleAsync(
            imposter, reviewId, null,
            new UpdateReviewRequest(Rating: 1, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(403);
        result.ReasonCode.Should().Be(ReviewReasonCode.EditNotAuthor);
    }

    [Fact]
    public async Task Edit_that_introduces_profanity_transitions_to_pending_moderation()
    {
        var (customerId, reviewId, productId, _) = await SubmitVisibleReviewAsync();
        var handler = NewUpdateHandler(out _);

        var result = await handler.HandleAsync(
            customerId, reviewId, null,
            // "spam" is in the SA seed wordlist.
            new UpdateReviewRequest(null, null, "Edited body now contains spam content.", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("pending_moderation");
        result.Response.PendingReview.Should().BeTrue();

        await using var db = NewContext();
        var row = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        row.State.Should().Be(ReviewState.PendingModeration);
        row.FilterTripTerms.Should().Contain("spam");
        row.PendingModerationStartedAt.Should().NotBeNull();
        row.TriggeredBy.Should().Be(ReviewTriggerKind.CustomerEdit);

        // Aggregate row should drop from review_count=1 to 0 since the row left visible.
        var aggregate = await db.RatingAggregates.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        aggregate.Should().NotBeNull();
        aggregate!.ReviewCount.Should().Be(0);
    }

    [Fact]
    public async Task Edit_during_pending_restamps_started_at_and_advances_xmin()
    {
        // Submit a review that lands in pending_moderation by including media.
        var (customerId, reviewId, _, _) = await SubmitPendingReviewAsync();

        await using var beforeCtx = NewContext();
        var before = await beforeCtx.Reviews.AsNoTracking()
            .Where(r => r.Id == reviewId)
            .Select(r => new { r.PendingModerationStartedAt, r.Xmin })
            .FirstAsync();
        before.PendingModerationStartedAt.Should().NotBeNull();

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        var handler = NewUpdateHandler(out _);
        var result = await handler.HandleAsync(
            customerId, reviewId, null,
            new UpdateReviewRequest(null, "Cleaned up", null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("pending_moderation"); // remains held
        result.Response.RowVersion.Should().NotBe(before.Xmin);

        await using var db = NewContext();
        var after = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        after.PendingModerationStartedAt.Should().NotBeNull();
        after.PendingModerationStartedAt!.Value.Should().BeAfter(before.PendingModerationStartedAt!.Value);
        after.EditCount.Should().Be(1);
    }

    [Fact]
    public async Task Stale_if_match_returns_version_conflict()
    {
        var (customerId, reviewId, _, _) = await SubmitVisibleReviewAsync();

        var handler = NewUpdateHandler(out _);
        var result = await handler.HandleAsync(
            customerId, reviewId, ifMatchRowVersion: 99999u, // wrong xmin
            new UpdateReviewRequest(3, null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(409);
        result.ReasonCode.Should().Be(ReviewReasonCode.RowVersionConflict);
    }

    // ──────────── helpers ────────────

    private ReviewsDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReviewsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new ReviewsDbContext(options);
    }

    private async Task SeedSchemasAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var seeder = new ReviewsReferenceDataSeeder();
        var ctx = new SeedContext(
            Db: null!,
            Services: provider,
            Size: DatasetSize.Small,
            Env: new TestHostEnv(),
            Logger: NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private async Task<(Guid customerId, Guid reviewId, Guid productId, FakeTimeProvider clock)> SubmitVisibleReviewAsync()
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
        var submit = new SubmitReviewHandler(db, new FakeEligibility(true, deliveredAt, Guid.NewGuid()), profanity, aggregate, clock);

        var result = await submit.HandleAsync(customerId, "SA",
            new SubmitReviewRequest(productId, 5, "Headline", "Body content long enough to satisfy validation.", "en", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        return (customerId, result.Response!.Id, productId, clock);
    }

    private async Task<(Guid customerId, Guid reviewId, Guid productId, FakeTimeProvider clock)> SubmitPendingReviewAsync()
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
        var submit = new SubmitReviewHandler(db, new FakeEligibility(true, deliveredAt, Guid.NewGuid()), profanity, aggregate, clock);

        var result = await submit.HandleAsync(customerId, "SA",
            new SubmitReviewRequest(productId, 4, "With media", "Clean text but with a media attachment.", "en",
                new[] { "https://storage.test/abc" }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("pending_moderation");
        return (customerId, result.Response.Id, productId, clock);
    }

    private UpdateReviewHandler NewUpdateHandler(FakeTimeProvider clock)
    {
        var db = NewContext();
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var profanity = new ProfanityFilter(provider.GetRequiredService<IServiceScopeFactory>(), new ArabicNormalizer(), TimeSpan.Zero);
        var aggregate = new RatingAggregateRecomputer(db, clock);
        return new UpdateReviewHandler(db, profanity, aggregate, clock);
    }

    private UpdateReviewHandler NewUpdateHandler(out FakeTimeProvider clock)
    {
        clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        return NewUpdateHandler(clock);
    }

    private sealed class FakeEligibility : IOrderLineDeliveryEligibilityQuery
    {
        private readonly bool _eligible;
        private readonly DateTimeOffset? _deliveredAt;
        private readonly Guid? _orderLineId;

        public FakeEligibility(bool eligible, DateTimeOffset? deliveredAt, Guid? orderLineId)
        {
            _eligible = eligible;
            _deliveredAt = deliveredAt;
            _orderLineId = orderLineId;
        }

        public Task<OrderLineDeliveryEligibilityResult> IsEligibleForReviewAsync(Guid c, Guid p, CancellationToken ct) =>
            Task.FromResult(new OrderLineDeliveryEligibilityResult(
                _eligible,
                _eligible ? null : "review.eligibility.no_delivered_purchase",
                _deliveredAt,
                _orderLineId));
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
