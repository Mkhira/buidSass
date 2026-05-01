using BackendApi.Modules.Reviews.Admin.DecideModeration;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Customer.ReportReview;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Reviews.Subscribers;
using BackendApi.Modules.Search.Primitives.Normalization;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Reviews.Tests.Integration.Events;

/// <summary>
/// Spec 022 T138 — verifies every domain event from data-model §6 fires
/// at the correct lifecycle point with the right payload shape (FR-038
/// — events fire after commit, never block lifecycle).
/// </summary>
public sealed class ReviewDomainEventsPublishedTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_events_test")
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
    public async Task Clean_visible_submission_publishes_ReviewSubmitted_and_ReviewPublished()
    {
        var collector = new FakeReviewDomainEventCollector();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await SubmitAsync(collector, customerId, productId, "Clean body content here.", null, clock);

        collector.OfType<ReviewSubmitted>().Should().HaveCount(1);
        collector.OfType<ReviewPublished>().Should().HaveCount(1);
        collector.OfType<ReviewHeldForModeration>().Should().BeEmpty();

        var submitted = collector.OfType<ReviewSubmitted>()[0];
        submitted.CustomerId.Should().Be(customerId);
        submitted.ProductId.Should().Be(productId);
        submitted.WasHeld.Should().BeFalse();
        submitted.HasMedia.Should().BeFalse();
    }

    [Fact]
    public async Task Pending_moderation_submission_publishes_ReviewHeldForModeration_not_Published()
    {
        var collector = new FakeReviewDomainEventCollector();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await SubmitAsync(collector, Guid.NewGuid(), Guid.NewGuid(),
            "Body text contains spam term.", null, clock);

        collector.OfType<ReviewSubmitted>().Should().HaveCount(1);
        collector.OfType<ReviewHeldForModeration>().Should().HaveCount(1);
        collector.OfType<ReviewPublished>().Should().BeEmpty();

        var held = collector.OfType<ReviewHeldForModeration>()[0];
        held.HoldReason.Should().Be("filter_trip");
        held.TermCount.Should().Be(1);
    }

    [Fact]
    public async Task Threshold_crossing_publishes_ReviewFlagged_exactly_once()
    {
        var collector = new FakeReviewDomainEventCollector();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        // Submit a clean visible review first.
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var reviewId = await SubmitAsync(collector, customerId, productId,
            "Clean body content here.", null, clock);

        // Three qualified reporters cross the default threshold of 3.
        for (var i = 0; i < 3; i++)
        {
            await using var reportDb = NewContext();
            var report = new ReportReviewHandler(reportDb,
                FakeReviewReporterFactsQuery.Qualified, collector, clock);
            var result = await report.HandleAsync(Guid.NewGuid(), reviewId,
                new ReportReviewRequest("personal_attack", null), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        var flagged = collector.OfType<ReviewFlagged>();
        flagged.Should().HaveCount(1, "threshold transition fires exactly once");
        flagged[0].ReviewId.Should().Be(reviewId);
        flagged[0].Threshold.Should().Be(3);
    }

    [Fact]
    public async Task Moderator_decision_publishes_ReviewHidden_and_ReviewReinstated()
    {
        var collector = new FakeReviewDomainEventCollector();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var reviewId = await SubmitAsync(collector, Guid.NewGuid(), Guid.NewGuid(),
            "Clean body content here.", null, clock);
        collector.Clear();

        await DecideAsync(collector, reviewId, "hidden",
            "Hide reason of sufficient length.", null, clock);
        collector.OfType<ReviewHidden>().Should().HaveCount(1);

        await DecideAsync(collector, reviewId, "visible", null,
            "Reinstate reason of sufficient length.", clock);
        collector.OfType<ReviewReinstated>().Should().HaveCount(1);
        collector.OfType<ReviewReinstated>()[0].PriorState.Should().Be("hidden");
    }

    [Fact]
    public async Task Refund_completed_publishes_ReviewAutoHidden_per_affected_review()
    {
        var collector = new FakeReviewDomainEventCollector();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var customerId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();

        await SubmitAsync(collector, customerId, Guid.NewGuid(),
            "Clean body content here.", null, clock, orderLineId: orderLineId);
        collector.Clear();

        await using var refundDb = NewContext();
        var aggregate = new RatingAggregateRecomputer(refundDb, clock);
        var handler = new RefundCompletedHandler(refundDb, aggregate, collector, clock);
        await handler.OnRefundCompletedAsync(
            new RefundCompletedEvent(orderLineId, customerId, clock.GetUtcNow(), Guid.NewGuid()),
            CancellationToken.None);

        var autoHidden = collector.OfType<ReviewAutoHidden>();
        autoHidden.Should().HaveCount(1);
        autoHidden[0].Trigger.Should().Be(ReviewTriggerKind.RefundEvent);
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
            null!, provider,
            BackendApi.Features.Seeding.Datasets.DatasetSize.Small,
            new TestHostEnv(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private async Task<Guid> SubmitAsync(
        FakeReviewDomainEventCollector collector,
        Guid customerId, Guid productId, string body, string[]? media, FakeTimeProvider clock,
        Guid? orderLineId = null)
    {
        var actualOrderLineId = orderLineId ?? Guid.NewGuid();
        await using var db = NewContext();
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var profanity = new ProfanityFilter(provider.GetRequiredService<IServiceScopeFactory>(), new ArabicNormalizer(), TimeSpan.Zero);
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var submit = new SubmitReviewHandler(db,
            new FakeOrderLineDeliveryEligibilityQuery(true, null, clock.GetUtcNow().AddDays(-1), actualOrderLineId),
            profanity, aggregate, collector, clock);

        var result = await submit.HandleAsync(customerId, "SA",
            new SubmitReviewRequest(productId, 4, "Headline", body, "en", media),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Response!.Id;
    }

    private async Task DecideAsync(
        FakeReviewDomainEventCollector collector,
        Guid reviewId, string toState, string? reason, string? adminNote, FakeTimeProvider clock)
    {
        await using var db = NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var handler = new DecideModerationHandler(db, aggregate, collector, clock);
        var result = await handler.HandleAsync(
            Guid.NewGuid(), hasModerator: true, hasSuperAdmin: false,
            reviewId, ifMatchRowVersion: null,
            new DecideModerationRequest(toState, reason, adminNote),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
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
