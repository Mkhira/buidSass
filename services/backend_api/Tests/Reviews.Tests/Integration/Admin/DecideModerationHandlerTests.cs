using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Reviews.Admin.DecideModeration;
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

namespace Reviews.Tests.Integration.Admin;

/// <summary>
/// Spec 022 T084-T101 — DecideModeration handler covers the core moderation
/// lifecycle: pending→visible (reinstate), visible→hidden, hidden→visible,
/// super_admin-only delete chord, terminal-deleted rejection, version conflict,
/// 405 hard-delete forbidden.
/// </summary>
public sealed class DecideModerationHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_decide_test")
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
    public async Task PendingModeration_to_visible_requires_admin_note_and_advances_state()
    {
        var (_, reviewId, productId, _) = await SubmitPendingReviewAsync();

        var (handler, _) = NewDecideHandler();
        var moderator = Guid.NewGuid();
        var result = await handler.HandleAsync(
            moderator, hasModerator: true, hasSuperAdmin: false,
            reviewId, ifMatchRowVersion: null,
            new DecideModerationRequest("visible", null, "Looks fine after re-review."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("visible");

        await using var db = NewContext();
        var review = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Visible);
        review.StateChangedByActorId.Should().Be(moderator);
        review.TriggeredBy.Should().Be(ReviewTriggerKind.ModeratorAction);

        var aggregate = await db.RatingAggregates.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        aggregate.Should().NotBeNull("pending→visible enters the counted set, aggregate refreshes inline");
        aggregate!.ReviewCount.Should().Be(1);
    }

    [Fact]
    public async Task Visible_to_hidden_requires_reason_note_and_drops_aggregate()
    {
        var (_, reviewId, productId, _) = await SubmitVisibleReviewAsync();

        var (handler, _) = NewDecideHandler();
        var result = await handler.HandleAsync(
            Guid.NewGuid(), true, false, reviewId, null,
            new DecideModerationRequest("hidden", "Violates community guideline 3.2.", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("hidden");

        await using var db = NewContext();
        var review = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Hidden);
        review.StateChangedReasonNote.Should().Contain("community guideline");

        var aggregate = await db.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        aggregate.ReviewCount.Should().Be(0, "visible→hidden flips counted=true → false");
    }

    [Fact]
    public async Task Hidden_to_visible_reversibility_is_supported()
    {
        var (_, reviewId, productId, _) = await SubmitVisibleReviewAsync();

        // First hide it.
        var (handler1, _) = NewDecideHandler();
        await handler1.HandleAsync(Guid.NewGuid(), true, false, reviewId, null,
            new DecideModerationRequest("hidden", "Initial hide for false-positive test.", null),
            CancellationToken.None);

        // Then reinstate.
        var (handler2, _) = NewDecideHandler();
        var result = await handler2.HandleAsync(Guid.NewGuid(), true, false, reviewId, null,
            new DecideModerationRequest("visible", null, "Re-evaluated; original hide was incorrect."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("visible");

        await using var db = NewContext();
        var aggregate = await db.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        aggregate.ReviewCount.Should().Be(1, "review re-enters the counted set");
    }

    [Fact]
    public async Task Delete_requires_super_admin_chord()
    {
        var (_, reviewId, _, _) = await SubmitVisibleReviewAsync();

        var (handler, _) = NewDecideHandler();
        var result = await handler.HandleAsync(
            Guid.NewGuid(), hasModerator: true, hasSuperAdmin: false,
            reviewId, null,
            new DecideModerationRequest("deleted", "Repeated abuse despite hides.", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(403);
        result.ReasonCode.Should().Be(ReviewReasonCode.ModerationDeleteRequiresSuperAdmin);
    }

    [Fact]
    public async Task Delete_succeeds_with_super_admin_and_drops_from_aggregate()
    {
        var (_, reviewId, productId, _) = await SubmitVisibleReviewAsync();

        var (handler, _) = NewDecideHandler();
        var result = await handler.HandleAsync(
            Guid.NewGuid(), hasModerator: true, hasSuperAdmin: true,
            reviewId, null,
            new DecideModerationRequest("deleted", "Repeated abuse despite hides.", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("deleted");

        await using var db = NewContext();
        var review = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Deleted);
        review.TriggeredBy.Should().Be(ReviewTriggerKind.ManualSuperAdmin);

        var aggregate = await db.RatingAggregates.AsNoTracking()
            .FirstAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        aggregate.ReviewCount.Should().Be(0);
    }

    [Fact]
    public async Task Decision_on_deleted_review_returns_terminal_rejection()
    {
        var (_, reviewId, _, _) = await SubmitVisibleReviewAsync();

        // First delete it.
        var (handler, _) = NewDecideHandler();
        await handler.HandleAsync(Guid.NewGuid(), true, true, reviewId, null,
            new DecideModerationRequest("deleted", "Initial delete.", null),
            CancellationToken.None);

        // Any further decision must be rejected with delete_terminal.
        var (handler2, _) = NewDecideHandler();
        var result = await handler2.HandleAsync(Guid.NewGuid(), true, true, reviewId, null,
            new DecideModerationRequest("visible", null, "Trying to undelete should fail."),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ReasonCode.Should().Be(ReviewReasonCode.ModerationDeleteTerminal);
    }

    [Fact]
    public async Task Stale_if_match_returns_version_conflict()
    {
        var (_, reviewId, _, _) = await SubmitVisibleReviewAsync();

        var (handler, _) = NewDecideHandler();
        var result = await handler.HandleAsync(
            Guid.NewGuid(), true, false, reviewId,
            ifMatchRowVersion: 99999u,
            new DecideModerationRequest("hidden", "Conflicting decision attempt.", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(409);
        result.ReasonCode.Should().Be(ReviewReasonCode.ModerationVersionConflict);
    }

    [Fact]
    public async Task Caller_without_moderator_permission_returns_forbidden()
    {
        var (_, reviewId, _, _) = await SubmitVisibleReviewAsync();

        var (handler, _) = NewDecideHandler();
        var result = await handler.HandleAsync(
            Guid.NewGuid(), hasModerator: false, hasSuperAdmin: false,
            reviewId, null,
            new DecideModerationRequest("hidden", "Some valid-looking reason.", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(403);
        result.ReasonCode.Should().Be(ReviewReasonCode.ModerationForbidden);
    }

    [Fact]
    public async Task Reinstate_to_visible_without_admin_note_is_rejected_at_validator()
    {
        var (ok, reason, _) = DecideModerationValidator.Validate(
            new DecideModerationRequest("visible", null, "short"));
        ok.Should().BeFalse();
        reason.Should().Be(ReviewReasonCode.ModerationReasonRequired);

        var (ok2, _, _) = DecideModerationValidator.Validate(
            new DecideModerationRequest("visible", null, "Detailed reinstatement note."));
        ok2.Should().BeTrue();
    }

    [Fact]
    public async Task Hide_without_reason_note_is_rejected_at_validator()
    {
        var (ok, reason, _) = DecideModerationValidator.Validate(
            new DecideModerationRequest("hidden", "short", null));
        ok.Should().BeFalse();
        reason.Should().Be(ReviewReasonCode.ModerationReasonRequired);
    }

    [Fact]
    public async Task Idempotent_same_state_decision_is_a_noop()
    {
        var (_, reviewId, _, _) = await SubmitVisibleReviewAsync();

        int preCount;
        await using (var pre = NewContext())
        {
            preCount = await pre.ModerationDecisions.CountAsync(d => d.ReviewId == reviewId);
        }

        var (handler, _) = NewDecideHandler();
        var result = await handler.HandleAsync(
            Guid.NewGuid(), true, false, reviewId, null,
            // visible → visible: state-machine permits + handler short-circuits.
            new DecideModerationRequest("visible", null, "Re-affirm visible."),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await using var db = NewContext();
        var postCount = await db.ModerationDecisions.CountAsync(d => d.ReviewId == reviewId);
        postCount.Should().Be(preCount, "no-op decisions don't write a second audit row");
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
            new SubmitReviewRequest(productId, 5, "Headline",
                "Long-enough body to satisfy validation.", "en", null),
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
            new SubmitReviewRequest(productId, 4, "With media", "Clean text + media attached.", "en",
                new[] { "https://storage.test/abc" }),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Response!.State.Should().Be("pending_moderation");
        return (customerId, result.Response.Id, productId, clock);
    }

    private (DecideModerationHandler handler, FakeTimeProvider clock) NewDecideHandler()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var db = NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        return (new DecideModerationHandler(db, aggregate, clock), clock);
    }

    private sealed class FakeEligibility : IOrderLineDeliveryEligibilityQuery
    {
        private readonly bool _eligible;
        private readonly DateTimeOffset? _delivered;
        private readonly Guid? _orderLineId;

        public FakeEligibility(bool eligible, DateTimeOffset? delivered, Guid? orderLineId)
        {
            _eligible = eligible;
            _delivered = delivered;
            _orderLineId = orderLineId;
        }

        public Task<OrderLineDeliveryEligibilityResult> IsEligibleForReviewAsync(Guid c, Guid p, CancellationToken ct) =>
            Task.FromResult(new OrderLineDeliveryEligibilityResult(
                _eligible,
                _eligible ? null : "review.eligibility.no_delivered_purchase",
                _delivered, _orderLineId));
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
