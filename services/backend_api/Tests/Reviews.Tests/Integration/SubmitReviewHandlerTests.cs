using BackendApi.Modules.Reviews.Hooks;
using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Reviews.Aggregate;
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

namespace Reviews.Tests.Integration;

/// <summary>
/// Spec 022 T054 + T055 + T058 — happy path + eligibility paths + edit flow
/// against a Testcontainers Postgres. Exercises the full SubmitReview pipeline
/// including the migration (schema, triggers, unique-partial index) and the
/// aggregate inline recompute.
/// </summary>
public sealed class SubmitReviewHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_submit_test")
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

    [Fact]
    public async Task Happy_path_eligible_clean_text_no_media_lands_visible()
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-10);

        var (handler, _, _) = NewHandler(new FakeEligibility(true, deliveredAt, orderLineId));
        var result = await handler.HandleAsync(
            customerId,
            "SA",
            new SubmitReviewRequest(productId, 5, "Great gloves", "Comfortable fit, durable through 3 procedures.", "en", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("visible");
        result.Response.PendingReview.Should().BeFalse();

        await using var db = NewContext();
        var row = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == result.Response.Id);
        row.State.Should().Be(ReviewState.Visible);
        row.OrderLineId.Should().Be(orderLineId);
        row.DeliveredAtUtc.Should().BeCloseTo(deliveredAt, TimeSpan.FromSeconds(1));

        var aggregate = await db.RatingAggregates.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        aggregate.Should().NotBeNull();
        aggregate!.ReviewCount.Should().Be(1);
        aggregate.AvgRating.Should().Be(5.0m);
    }

    [Fact]
    public async Task No_delivered_purchase_returns_eligibility_reason()
    {
        var (handler, _, _) = NewHandler(new FakeEligibility(false, null, null));
        var result = await handler.HandleAsync(
            Guid.NewGuid(), "SA",
            new SubmitReviewRequest(Guid.NewGuid(), 4, "x", "Body text long enough.", "en", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(ReviewReasonCode.EligibilityNoDeliveredPurchase);
    }

    [Fact]
    public async Task Eligibility_window_closed_returns_window_closed_reason()
    {
        var deliveredLongAgo = DateTimeOffset.UtcNow.AddDays(-300);
        var (handler, _, _) = NewHandler(new FakeEligibility(true, deliveredLongAgo, Guid.NewGuid()));
        var result = await handler.HandleAsync(
            Guid.NewGuid(), "SA",
            new SubmitReviewRequest(Guid.NewGuid(), 4, "x", "Body text long enough.", "en", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(ReviewReasonCode.EligibilityWindowClosed);
    }

    [Fact]
    public async Task Profanity_trip_holds_for_moderation_and_does_not_count_in_aggregate()
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-1);

        var (handler, _, _) = NewHandler(new FakeEligibility(true, deliveredAt, Guid.NewGuid()));
        var result = await handler.HandleAsync(
            customerId, "SA",
            // "spam" is in the SA seed wordlist (ReviewsReferenceDataSeeder).
            new SubmitReviewRequest(productId, 4, "Beware", "This product is spam pretending to be useful.", "en", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("pending_moderation");
        result.Response.PendingReview.Should().BeTrue();

        await using var db = NewContext();
        var row = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == result.Response.Id);
        row.State.Should().Be(ReviewState.PendingModeration);
        row.FilterTripTerms.Should().Contain("spam");
        row.PendingModerationStartedAt.Should().NotBeNull();

        var aggregate = await db.RatingAggregates.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        aggregate.Should().BeNull("pending_moderation reviews are not counted in the aggregate");
    }

    [Fact]
    public async Task Media_attachment_holds_for_moderation_regardless_of_clean_text()
    {
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-1);
        var (handler, _, _) = NewHandler(new FakeEligibility(true, deliveredAt, Guid.NewGuid()));
        var result = await handler.HandleAsync(
            Guid.NewGuid(), "SA",
            new SubmitReviewRequest(Guid.NewGuid(), 5, "All good", "Clean review with a photo attached.", "en",
                new[] { "https://storage.test/abc" }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("pending_moderation");
        result.Response.PendingReview.Should().BeTrue();
    }

    [Fact]
    public async Task Duplicate_review_for_same_customer_product_rejected_via_unique_partial()
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-2);
        var (handler, _, _) = NewHandler(new FakeEligibility(true, deliveredAt, Guid.NewGuid()));

        var first = await handler.HandleAsync(
            customerId, "SA",
            new SubmitReviewRequest(productId, 4, "first", "First body content.", "en", null),
            CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        var second = await handler.HandleAsync(
            customerId, "SA",
            new SubmitReviewRequest(productId, 5, "second", "Second body content.", "en", null),
            CancellationToken.None);
        second.IsSuccess.Should().BeFalse();
        second.ReasonCode.Should().Be(ReviewReasonCode.EligibilityAlreadyReviewed);
    }

    private (SubmitReviewHandler handler, ReviewsDbContext db, FakeTimeProvider clock) NewHandler(IOrderLineDeliveryEligibilityQuery eligibility)
    {
        var db = NewContext();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var profanity = new ProfanityFilter(provider.GetRequiredService<IServiceScopeFactory>(), new ArabicNormalizer(), TimeSpan.Zero);
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var handler = new SubmitReviewHandler(db, eligibility, profanity, aggregate, new NullReviewDomainEventPublisher(), clock);
        return (handler, db, clock);
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

        public Task<OrderLineDeliveryEligibilityResult> IsEligibleForReviewAsync(
            Guid customerId, Guid productId, CancellationToken ct)
        {
            return Task.FromResult(new OrderLineDeliveryEligibilityResult(
                Eligible: _eligible,
                ReasonCode: _eligible ? null : "review.eligibility.no_delivered_purchase",
                DeliveredAt: _deliveredAt,
                OrderLineId: _orderLineId));
        }
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
