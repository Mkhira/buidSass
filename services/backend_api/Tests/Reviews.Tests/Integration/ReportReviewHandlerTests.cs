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

namespace Reviews.Tests.Integration;

/// <summary>
/// Spec 022 T074-T083 — community report flow: self-report rejection,
/// idempotent same-actor reporting, qualified-reporter snapshot capture
/// (R5 / FR-023), threshold-driven Visible→Flagged auto-transition,
/// validation of fixed reason codes + the required note for "other".
/// </summary>
public sealed class ReportReviewHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_report_test")
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
    public async Task Reporter_who_is_the_author_is_rejected_with_self_report_reason()
    {
        var (authorId, reviewId, _) = await SubmitVisibleReviewAsync();
        var handler = NewReportHandler(qualified: true, out _);

        var result = await handler.HandleAsync(
            authorId, reviewId,
            new ReportReviewRequest("personal_attack", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(400);
        result.ReasonCode.Should().Be(ReviewReasonCode.ReportCannotReportOwnReview);
    }

    [Fact]
    public async Task Same_reporter_twice_returns_already_reported_and_does_not_double_count()
    {
        var (_, reviewId, _) = await SubmitVisibleReviewAsync();
        var reporter = Guid.NewGuid();
        var handler = NewReportHandler(qualified: true, out _);

        var first = await handler.HandleAsync(reporter, reviewId,
            new ReportReviewRequest("spam_or_irrelevant", null), CancellationToken.None);
        var second = await handler.HandleAsync(reporter, reviewId,
            new ReportReviewRequest("spam_or_irrelevant", null), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeFalse();
        second.Status.Should().Be(409);
        second.ReasonCode.Should().Be(ReviewReasonCode.ReportAlreadyReportedByActor);

        await using var db = NewContext();
        var flagCount = await db.Flags.CountAsync(f => f.ReviewId == reviewId);
        flagCount.Should().Be(1);
    }

    [Fact]
    public async Task Three_qualified_reporters_within_window_transition_visible_to_flagged()
    {
        var (_, reviewId, productId) = await SubmitVisibleReviewAsync();
        var handler = NewReportHandler(qualified: true, out _);

        for (var i = 0; i < 3; i++)
        {
            var result = await handler.HandleAsync(Guid.NewGuid(), reviewId,
                new ReportReviewRequest("personal_attack", null), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        await using var db = NewContext();
        var review = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Flagged);
        review.TriggeredBy.Should().Be(ReviewTriggerKind.CommunityReportThreshold);

        var transitionRow = await db.ModerationDecisions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.ReviewId == reviewId
                                   && d.FromState == ReviewState.Visible
                                   && d.ToState == ReviewState.Flagged);
        transitionRow.Should().NotBeNull();
        transitionRow!.ActorRole.Should().Be("system");

        // Aggregate row should still count this review (visible+flagged both count).
        var aggregate = await db.RatingAggregates.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProductId == productId && a.MarketCode == "SA");
        aggregate.Should().NotBeNull();
        aggregate!.ReviewCount.Should().Be(1, "visible→flagged does NOT change aggregate count");
    }

    [Fact]
    public async Task Unqualified_reporters_do_not_count_toward_threshold()
    {
        var (_, reviewId, _) = await SubmitVisibleReviewAsync();
        var handler = NewReportHandler(qualified: false, out _);

        for (var i = 0; i < 5; i++)
        {
            var result = await handler.HandleAsync(Guid.NewGuid(), reviewId,
                new ReportReviewRequest("spam_or_irrelevant", null), CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
            result.Response!.Qualified.Should().BeFalse();
        }

        await using var db = NewContext();
        var review = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
        review.State.Should().Be(ReviewState.Visible, "unqualified reports must not auto-flag");

        // All 5 flag rows persisted with is_qualified=false for moderator visibility.
        var flagsByQualified = await db.Flags
            .Where(f => f.ReviewId == reviewId)
            .GroupBy(f => f.IsQualified)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();
        flagsByQualified.Should().ContainSingle(x => x.Key == false && x.Count == 5);
    }

    [Fact]
    public async Task Qualifying_evaluation_jsonb_persists_facts_at_report_time()
    {
        var (_, reviewId, _) = await SubmitVisibleReviewAsync();
        var handler = NewReportHandler(qualified: true, out _);

        var result = await handler.HandleAsync(Guid.NewGuid(), reviewId,
            new ReportReviewRequest("false_or_misleading", null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        await using var db = NewContext();
        var flag = await db.Flags.AsNoTracking().FirstAsync(f => f.Id == result.Response!.FlagId);
        flag.IsQualified.Should().BeTrue();
        flag.QualifyingEvaluationJson.Should().Contain("account_age_days");
        flag.QualifyingEvaluationJson.Should().Contain("has_delivered_order");
        flag.QualifyingEvaluationJson.Should().Contain("qualifying_account_age_days");
    }

    [Fact]
    public async Task Other_reason_without_note_is_rejected_at_validator()
    {
        var (ok, code, _) = ReportReviewValidator.Validate(
            new ReportReviewRequest("other_with_required_note", "short"));
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.ReportNoteRequired);

        var (ok2, _, _) = ReportReviewValidator.Validate(
            new ReportReviewRequest("other_with_required_note",
                "This is a sufficiently detailed explanation."));
        ok2.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_reason_code_is_rejected()
    {
        var (ok, code, _) = ReportReviewValidator.Validate(
            new ReportReviewRequest("not_a_real_reason", null));
        ok.Should().BeFalse();
        code.Should().Be(ReviewReasonCode.ReportReasonInvalid);
    }

    [Fact]
    public async Task Threshold_progress_in_response_reflects_post_insert_count()
    {
        var (_, reviewId, _) = await SubmitVisibleReviewAsync();
        var handler = NewReportHandler(qualified: true, out _);

        var first = await handler.HandleAsync(Guid.NewGuid(), reviewId,
            new ReportReviewRequest("spam_or_irrelevant", null), CancellationToken.None);
        first.Response!.ThresholdProgress.QualifiedCount.Should().Be(1);
        first.Response.ThresholdProgress.Threshold.Should().Be(3);

        var second = await handler.HandleAsync(Guid.NewGuid(), reviewId,
            new ReportReviewRequest("spam_or_irrelevant", null), CancellationToken.None);
        second.Response!.ThresholdProgress.QualifiedCount.Should().Be(2);
    }

    [Fact]
    public async Task Reports_against_already_flagged_review_dont_re_transition()
    {
        // Push the review to flagged via 3 qualified reports.
        var (_, reviewId, _) = await SubmitVisibleReviewAsync();
        var handler = NewReportHandler(qualified: true, out _);
        for (var i = 0; i < 3; i++)
        {
            await handler.HandleAsync(Guid.NewGuid(), reviewId,
                new ReportReviewRequest("personal_attack", null), CancellationToken.None);
        }

        // 4th report should succeed but not write a second visible→flagged decision.
        var result = await handler.HandleAsync(Guid.NewGuid(), reviewId,
            new ReportReviewRequest("personal_attack", null), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        await using var db = NewContext();
        var visibleToFlaggedRows = await db.ModerationDecisions
            .CountAsync(d => d.ReviewId == reviewId
                          && d.FromState == ReviewState.Visible
                          && d.ToState == ReviewState.Flagged);
        visibleToFlaggedRows.Should().Be(1, "the threshold transition fires exactly once");
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

    private async Task<(Guid customerId, Guid reviewId, Guid productId)> SubmitVisibleReviewAsync()
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
            new SubmitReviewRequest(productId, 4, "Headline",
                "Long-enough body to satisfy validation.", "en", null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return (customerId, result.Response!.Id, productId);
    }

    private ReportReviewHandler NewReportHandler(bool qualified, out FakeTimeProvider clock)
    {
        clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var db = NewContext();
        var facts = new FakeReporterFacts(qualified);
        return new ReportReviewHandler(db, facts, clock);
    }

    private sealed class FakeReporterFacts : IReviewReporterFactsQuery
    {
        private readonly bool _qualified;
        public FakeReporterFacts(bool qualified) => _qualified = qualified;

        // Default policy is account_age_days >= 14 + has_delivered_order. Returning
        // 30/true qualifies; 0/false fails. Allows callers to pick either side
        // without coupling to the policy thresholds directly.
        public Task<ReviewReporterFacts> GetAsync(Guid customerId, CancellationToken ct) =>
            Task.FromResult(_qualified
                ? new ReviewReporterFacts(30, true)
                : new ReviewReporterFacts(0, false));
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

        public Task<OrderLineDeliveryEligibilityResult> IsEligibleForReviewAsync(
            Guid c, Guid p, CancellationToken ct) =>
            Task.FromResult(new OrderLineDeliveryEligibilityResult(
                _eligible,
                _eligible ? null : "review.eligibility.no_delivered_purchase",
                _delivered,
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
