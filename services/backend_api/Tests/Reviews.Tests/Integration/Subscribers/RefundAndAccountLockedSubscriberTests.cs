using BackendApi.Modules.Reviews.Hooks;
using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Reviews.Seeding;
using BackendApi.Modules.Reviews.Subscribers;
using BackendApi.Modules.Search.Primitives.Normalization;
using BackendApi.Modules.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Reviews.Tests.Integration.Subscribers;

/// <summary>
/// Spec 022 T103-T106 — refund-completed auto-hide cascade,
/// refund-reversed advisory (NO auto-reinstate per FR-032),
/// account-locked auto-hide cascade.
/// </summary>
public sealed class RefundAndAccountLockedSubscriberTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_subscriber_test")
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
    public async Task RefundCompleted_auto_hides_visible_review_and_drops_aggregate()
    {
        var (customerId, reviewId, productId, orderLineId) = await SubmitVisibleReviewAsync();

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var handler = new RefundCompletedHandler(db, aggregate, new NullReviewDomainEventPublisher(), clock);

        await handler.OnRefundCompletedAsync(
            new RefundCompletedEvent(orderLineId, customerId, clock.GetUtcNow(), Guid.NewGuid()),
            CancellationToken.None);

        await using var verifyDb = NewContext();
        var review = await verifyDb.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Hidden);
        review.TriggeredBy.Should().Be(ReviewTriggerKind.RefundEvent);
        review.StateChangedReasonNote.Should().Contain("refunded");

        var auditRow = await verifyDb.ModerationDecisions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.ReviewId == reviewId
                                   && d.TriggeredBy == ReviewTriggerKind.RefundEvent);
        auditRow.Should().NotBeNull();
        auditRow!.ActorRole.Should().Be("system");
        auditRow.FromState.Should().Be(ReviewState.Visible);
        auditRow.ToState.Should().Be(ReviewState.Hidden);

        var agg = await verifyDb.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        agg.ReviewCount.Should().Be(0);
    }

    [Fact]
    public async Task RefundCompleted_replay_is_idempotent()
    {
        var (customerId, reviewId, _, orderLineId) = await SubmitVisibleReviewAsync();

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var handler = new RefundCompletedHandler(db, aggregate, new NullReviewDomainEventPublisher(), clock);
        var evt = new RefundCompletedEvent(orderLineId, customerId, clock.GetUtcNow(), Guid.NewGuid());

        await handler.OnRefundCompletedAsync(evt, CancellationToken.None);

        // Second delivery — handler must short-circuit (review is already hidden).
        await using var db2 = NewContext();
        var aggregate2 = new RatingAggregateRecomputer(db2, clock);
        var handler2 = new RefundCompletedHandler(db2, aggregate, new NullReviewDomainEventPublisher(), clock);
        await handler2.OnRefundCompletedAsync(evt, CancellationToken.None);

        await using var verifyDb = NewContext();
        var transitions = await verifyDb.ModerationDecisions
            .CountAsync(d => d.ReviewId == reviewId
                          && d.TriggeredBy == ReviewTriggerKind.RefundEvent);
        transitions.Should().Be(1, "replay must not write a second audit row");
    }

    [Fact]
    public async Task RefundReversed_does_NOT_auto_reinstate_FR_032()
    {
        var (customerId, reviewId, _, orderLineId) = await SubmitVisibleReviewAsync();

        // First hide via refund_completed.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var hideDb = NewContext();
        var hideHandler = new RefundCompletedHandler(hideDb, new RatingAggregateRecomputer(hideDb, clock), new NullReviewDomainEventPublisher(), clock);
        await hideHandler.OnRefundCompletedAsync(
            new RefundCompletedEvent(orderLineId, customerId, clock.GetUtcNow(), Guid.NewGuid()),
            CancellationToken.None);

        // Now reverse the refund — review should STAY hidden but get an advisory note.
        await using var reverseDb = NewContext();
        var reverseHandler = new RefundReversedHandler(reverseDb, NullLogger<RefundReversedHandler>.Instance, clock);
        await reverseHandler.OnRefundReversedAsync(
            new RefundReversedEvent(orderLineId, customerId, clock.GetUtcNow(),
                Guid.NewGuid(), "Returned by customer for partial refund."),
            CancellationToken.None);

        await using var verifyDb = NewContext();
        var review = await verifyDb.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Hidden, "FR-032 — refund reversal does NOT auto-reinstate");

        var notes = await verifyDb.AdminNotes.AsNoTracking()
            .Where(n => n.ReviewId == reviewId)
            .ToListAsync();
        notes.Should().ContainSingle();
        notes[0].Note.Should().Contain("refund was reversed");
        notes[0].Note.Should().Contain("FR-032");
    }

    [Fact]
    public async Task AccountLocked_auto_hides_all_authors_visible_reviews()
    {
        var customerId = Guid.NewGuid();
        var (_, reviewA, productA, _) = await SubmitVisibleReviewAsync(customerId);
        var (_, reviewB, productB, _) = await SubmitVisibleReviewAsync(customerId);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var handler = new CustomerAccountLifecycleHandler(db, aggregate, new NullReviewDomainEventPublisher(), clock);

        await handler.OnAccountLockedAsync(
            new CustomerAccountLocked(customerId, "Suspicious activity.", clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var both = await verify.Reviews.AsNoTracking()
            .Where(r => r.Id == reviewA || r.Id == reviewB)
            .ToListAsync();
        both.Should().OnlyContain(r => r.State == ReviewState.Hidden);
        both.Should().OnlyContain(r => r.TriggeredBy == ReviewTriggerKind.AccountLocked);

        var aggA = await verify.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productA && a.MarketCode == "SA");
        var aggB = await verify.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productB && a.MarketCode == "SA");
        aggA.ReviewCount.Should().Be(0);
        aggB.ReviewCount.Should().Be(0);
    }

    [Fact]
    public async Task AccountDeleted_auto_hides_authored_reviews_too()
    {
        var customerId = Guid.NewGuid();
        var (_, reviewId, _, _) = await SubmitVisibleReviewAsync(customerId);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var handler = new CustomerAccountLifecycleHandler(db, aggregate, new NullReviewDomainEventPublisher(), clock);

        await handler.OnAccountDeletedAsync(
            new CustomerAccountDeleted(customerId, clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var review = await verify.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Hidden);
    }

    [Fact]
    public async Task MarketChanged_is_a_noop_per_principle_5()
    {
        var customerId = Guid.NewGuid();
        var (_, reviewId, _, _) = await SubmitVisibleReviewAsync(customerId);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = NewContext();
        var handler = new CustomerAccountLifecycleHandler(db, new RatingAggregateRecomputer(db, clock), new NullReviewDomainEventPublisher(), clock);

        await handler.OnMarketChangedAsync(
            new CustomerMarketChanged(customerId, "SA", "EG", Guid.NewGuid(), clock.GetUtcNow()),
            CancellationToken.None);

        await using var verify = NewContext();
        var review = await verify.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Visible, "market changes don't touch reviews");
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

    private async Task<(Guid customerId, Guid reviewId, Guid productId, Guid orderLineId)> SubmitVisibleReviewAsync(
        Guid? customerOverride = null)
    {
        var customerId = customerOverride ?? Guid.NewGuid();
        var productId = Guid.NewGuid();
        var orderLineId = Guid.NewGuid();
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-2);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var db = NewContext();
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var profanity = new ProfanityFilter(provider.GetRequiredService<IServiceScopeFactory>(), new ArabicNormalizer(), TimeSpan.Zero);
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var submit = new SubmitReviewHandler(db, new FakeEligibility(deliveredAt, orderLineId), profanity, aggregate, new NullReviewDomainEventPublisher(), clock);

        var result = await submit.HandleAsync(customerId, "SA",
            new SubmitReviewRequest(productId, 5, "Headline",
                "Long-enough body to satisfy validation.", "en", null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return (customerId, result.Response!.Id, productId, orderLineId);
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
