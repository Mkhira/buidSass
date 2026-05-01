using BackendApi.Features.Seeding;
using BackendApi.Features.Seeding.Datasets;
using BackendApi.Modules.Reviews.Admin.AddAdminNote;
using BackendApi.Modules.Reviews.Admin.GetReviewDetail;
using BackendApi.Modules.Reviews.Admin.ListAdminNotes;
using BackendApi.Modules.Reviews.Admin.ListModerationQueue;
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
/// Spec 022 T085, T086, T092, T099 — moderator queue listing, review detail
/// projection, admin-note append-only writes + ordered list, integration with
/// the unique partial index against pending_moderation rows.
/// </summary>
public sealed class AdminQueueAndNotesTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_admin_queue_test")
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
    public async Task Queue_lists_pending_moderation_reviews_oldest_first()
    {
        var first = await SubmitPendingReviewAsync();
        await Task.Delay(50);
        var second = await SubmitPendingReviewAsync();

        await using var db = NewContext();
        var handler = new ListModerationQueueHandler(db);
        var response = await handler.HandleAsync(
            new ListModerationQueueQuery("pending_moderation", null, null, null, null, null, 50),
            CancellationToken.None);

        response.Items.Should().HaveCount(2);
        // Oldest first → SLA priority.
        response.Items[0].Id.Should().Be(first);
        response.Items[1].Id.Should().Be(second);
        response.Items.Should().OnlyContain(i => i.State == "pending_moderation");
    }

    [Fact]
    public async Task Queue_filters_by_market_code()
    {
        var saReview = await SubmitPendingReviewAsync(market: "SA");
        await SubmitPendingReviewAsync(market: "EG");

        await using var db = NewContext();
        var handler = new ListModerationQueueHandler(db);
        var response = await handler.HandleAsync(
            new ListModerationQueueQuery(null, "SA", null, null, null, null, 50),
            CancellationToken.None);

        response.Items.Should().ContainSingle();
        response.Items[0].Id.Should().Be(saReview);
    }

    [Fact]
    public async Task Queue_excludes_visible_hidden_deleted()
    {
        await SubmitPendingReviewAsync();
        await SubmitVisibleReviewAsync();

        await using var db = NewContext();
        var handler = new ListModerationQueueHandler(db);
        var response = await handler.HandleAsync(
            new ListModerationQueueQuery(null, null, null, null, null, null, 50),
            CancellationToken.None);

        response.Items.Should().ContainSingle();
        response.Items[0].State.Should().Be("pending_moderation");
    }

    [Fact]
    public async Task Queue_filters_media_only()
    {
        var mediaReview = await SubmitPendingReviewAsync();  // media path
        await SubmitProfanityPendingReviewAsync();           // filter-trip path

        await using var db = NewContext();
        var handler = new ListModerationQueueHandler(db);
        var response = await handler.HandleAsync(
            new ListModerationQueueQuery(null, null, null, null, true, null, 50),
            CancellationToken.None);

        response.Items.Should().ContainSingle();
        response.Items[0].Id.Should().Be(mediaReview);
        response.Items[0].MediaAttachmentReviewRequired.Should().BeTrue();
    }

    [Fact]
    public async Task GetReviewDetail_returns_full_projection_with_audit_history()
    {
        var reviewId = await SubmitPendingReviewAsync();

        await using var db = NewContext();
        var handler = new GetReviewDetailHandler(db);
        var detail = await handler.HandleAsync(reviewId, CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.State.Should().Be("pending_moderation");
        detail.MediaAttachmentReviewRequired.Should().BeTrue();
        detail.AuditHistory.Should().NotBeEmpty("submission writes a transition row");
        detail.Flags.Should().BeEmpty();
        detail.AdminNotes.Should().BeEmpty();
        detail.RowVersion.Should().NotBe(0u);
    }

    [Fact]
    public async Task AddAdminNote_persists_append_only_with_min_length()
    {
        var reviewId = await SubmitVisibleReviewAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await using var db = NewContext();
        var handler = new AddAdminNoteHandler(db, clock);

        var tooShort = await handler.HandleAsync(Guid.NewGuid(), reviewId, "short", CancellationToken.None);
        tooShort.IsSuccess.Should().BeFalse();
        tooShort.Status.Should().Be(400);
        tooShort.ReasonCode.Should().Be(ReviewReasonCode.ModerationReasonRequired);

        var ok = await handler.HandleAsync(Guid.NewGuid(), reviewId,
            "Sufficiently detailed admin note for forensics.", CancellationToken.None);
        ok.IsSuccess.Should().BeTrue();
        ok.Status.Should().Be(201);

        await using var db2 = NewContext();
        var listHandler = new ListAdminNotesHandler(db2);
        var list = await listHandler.HandleAsync(reviewId, CancellationToken.None);
        list.Items.Should().ContainSingle();
        list.Items[0].Note.Should().Contain("forensics");
    }

    [Fact]
    public async Task AddAdminNote_for_unknown_review_returns_404()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var db = NewContext();
        var handler = new AddAdminNoteHandler(db, clock);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(),
            "Sufficiently long admin note text.", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(404);
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

    private async Task<Guid> SubmitVisibleReviewAsync()
    {
        var (id, _) = await SubmitAsync(rating: 5,
            body: "Long-enough body to satisfy validation.",
            mediaUrls: null, market: "SA");
        return id;
    }

    private async Task<Guid> SubmitPendingReviewAsync(string market = "SA")
    {
        var (id, _) = await SubmitAsync(rating: 4,
            body: "Clean text but with a media attachment.",
            mediaUrls: new[] { "https://storage.test/abc" },
            market: market);
        return id;
    }

    private async Task<Guid> SubmitProfanityPendingReviewAsync()
    {
        var (id, _) = await SubmitAsync(rating: 3,
            body: "This product is spam pretending to be useful.",
            mediaUrls: null, market: "SA");
        return id;
    }

    private async Task<(Guid reviewId, Guid productId)> SubmitAsync(
        int rating, string body, string[]? mediaUrls, string market)
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var deliveredAt = DateTimeOffset.UtcNow.AddDays(-1);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var db = NewContext();
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        var provider = services.BuildServiceProvider();
        var profanity = new ProfanityFilter(provider.GetRequiredService<IServiceScopeFactory>(), new ArabicNormalizer(), TimeSpan.Zero);
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var submit = new SubmitReviewHandler(db, new FakeEligibility(true, deliveredAt, Guid.NewGuid()), profanity, aggregate, clock);

        var result = await submit.HandleAsync(customerId, market,
            new SubmitReviewRequest(productId, rating, "headline", body, "en", mediaUrls),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return (result.Response!.Id, productId);
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
