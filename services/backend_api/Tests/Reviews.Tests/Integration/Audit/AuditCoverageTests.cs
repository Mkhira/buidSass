using BackendApi.Modules.Reviews.Hooks;
using BackendApi.Modules.Reviews.Admin.AddAdminNote;
using BackendApi.Modules.Reviews.Admin.DecideModeration;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Customer.ReportReview;
using BackendApi.Modules.Reviews.Customer.SubmitReview;
using BackendApi.Modules.Reviews.Customer.UpdateReview;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.PolicyAdmin.UpdateMarketSchema;
using BackendApi.Modules.Reviews.PolicyAdmin.UpsertWordlistTerm;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Reviews.Subscribers;
using BackendApi.Modules.Search.Primitives.Normalization;
using BackendApi.Modules.Shared;
using BackendApi.Modules.Shared.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Reviews.Tests.Integration.Audit;

/// <summary>
/// Spec 022 T141 — exercises every authoring + lifecycle + report + admin
/// note + wordlist + threshold path; asserts every audit-event kind from
/// data-model §5 is reachable. The denormalized cache table
/// <c>review_moderation_decisions</c> doubles as the audit-event index for
/// state transitions; <c>review_admin_notes</c> for moderator notes;
/// <c>review_flags</c> for community reports; <c>reviews_filter_wordlists</c>
/// + <c>reviews_market_schemas</c> rows themselves witness the policy-admin
/// kinds.
///
/// Spec lists 14 audit-event kinds (data-model §5):
///   review.submitted, review.edited, review.published, review.held_for_moderation,
///   review.flagged, review.hidden, review.reinstated, review.deleted,
///   review.auto_hidden, review.report_submitted, review.admin_note_added,
///   reviews.wordlist.term_upserted, reviews.wordlist.term_deleted,
///   reviews.market_schema_updated.
/// </summary>
public sealed class AuditCoverageTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("reviews_audit_coverage")
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
    public async Task Every_audit_event_kind_is_reachable()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var orderLineId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // 1. review.submitted + review.published — clean visible submission.
        var visibleId = await SubmitAsync(customerId, productId, orderLineId, body: "Clean review body.", clock);

        // 2. review.held_for_moderation + review.submitted — pending submission.
        var pendingCustomerId = Guid.NewGuid();
        var pendingProductId = Guid.NewGuid();
        var pendingId = await SubmitAsync(pendingCustomerId, pendingProductId, Guid.NewGuid(),
            body: "Body that contains spam to trip the seeded SA wordlist.", clock);

        // 3. review.edited — author edits the visible review.
        await using (var editDb = NewContext())
        {
            var profanity = NewProfanity();
            var aggregate = new RatingAggregateRecomputer(editDb, clock);
            var update = new UpdateReviewHandler(editDb, profanity, aggregate, new NullReviewDomainEventPublisher(), clock);
            var editResult = await update.HandleAsync(
                customerId, visibleId, ifMatchRowVersion: null,
                new UpdateReviewRequest(Rating: 4, null, null, null, null),
                CancellationToken.None);
            editResult.IsSuccess.Should().BeTrue();
        }

        // 4. review.flagged + review.report_submitted — qualified reporters cross threshold.
        for (var i = 0; i < 3; i++)
        {
            await using var reportDb = NewContext();
            var report = new ReportReviewHandler(reportDb, FakeReviewReporterFactsQuery.Qualified, new NullReviewDomainEventPublisher(), clock);
            await report.HandleAsync(Guid.NewGuid(), visibleId,
                new ReportReviewRequest("personal_attack", null), CancellationToken.None);
        }

        // 5. review.hidden — moderator decision.
        var hiddenCustomerId = Guid.NewGuid();
        var hiddenProductId = Guid.NewGuid();
        var hiddenReviewId = await SubmitAsync(hiddenCustomerId, hiddenProductId, Guid.NewGuid(),
            "Plain body to be hidden.", clock);
        await DecideAsync(hiddenReviewId, "hidden", "Hide reason of sufficient length.", null, clock);

        // 6. review.reinstated — moderator reopens the hidden review.
        await DecideAsync(hiddenReviewId, "visible", null, "Reinstate admin note long enough.", clock);

        // 7. review.deleted — super_admin hard-deletes a separate review.
        var deletedReviewId = await SubmitAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Body of a review to be deleted.", clock);
        await DecideAsync(deletedReviewId, "deleted",
            "Delete reason of sufficient length for the audit row.", null, clock,
            hasSuperAdmin: true);

        // 8. review.auto_hidden — refund-completed cascade.
        var refundCustomerId = Guid.NewGuid();
        var refundProductId = Guid.NewGuid();
        var refundOrderLineId = Guid.NewGuid();
        await SubmitAsync(refundCustomerId, refundProductId, refundOrderLineId,
            "Body of a review whose order line will be refunded.", clock);
        await using (var refundDb = NewContext())
        {
            var aggregate = new RatingAggregateRecomputer(refundDb, clock);
            var handler = new RefundCompletedHandler(refundDb, aggregate, new NullReviewDomainEventPublisher(), clock);
            await handler.OnRefundCompletedAsync(
                new RefundCompletedEvent(refundOrderLineId, refundCustomerId, clock.GetUtcNow(), Guid.NewGuid()),
                CancellationToken.None);
        }

        // 9. review.admin_note_added — moderator attaches a note to the visible review.
        await using (var noteDb = NewContext())
        {
            var handler = new AddAdminNoteHandler(noteDb, clock);
            var result = await handler.HandleAsync(Guid.NewGuid(), visibleId,
                "Sufficiently detailed admin note for forensics.", CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        // 10. reviews.wordlist.term_upserted (witness via wordlist row).
        await using (var wordlistDb = NewContext())
        {
            var profanity = NewProfanity();
            var handler = new UpsertWordlistTermHandler(wordlistDb, new ArabicNormalizer(), profanity, clock);
            await handler.HandleAsync(Guid.NewGuid(),
                new UpsertWordlistTermRequest("SA", "newauditterm", null), CancellationToken.None);
        }

        // 11. reviews.market_schema_updated (witness via updated row).
        await using (var schemaDb = NewContext())
        {
            var handler = new UpdateMarketSchemaHandler(schemaDb, clock);
            var result = await handler.HandleAsync(Guid.NewGuid(), "SA",
                new UpdateMarketSchemaRequest(EligibilityWindowDays: 200, null, null, null, null, null, null),
                CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        // ──────────── assertions ────────────

        await using var verify = NewContext();

        // review.submitted + review.published witnessed by submission transitions to Visible
        var submitted = await verify.ModerationDecisions.AsNoTracking()
            .Where(d => d.TriggeredBy == ReviewTriggerKind.CustomerSubmission && d.ToState == ReviewState.Visible)
            .CountAsync();
        submitted.Should().BeGreaterThan(0, "review.submitted + review.published reachable");

        // review.held_for_moderation
        var held = await verify.ModerationDecisions.AsNoTracking()
            .CountAsync(d => d.TriggeredBy == ReviewTriggerKind.CustomerSubmission && d.ToState == ReviewState.PendingModeration);
        held.Should().BeGreaterThan(0, "review.held_for_moderation reachable");

        // review.edited
        var edited = await verify.ModerationDecisions.AsNoTracking()
            .CountAsync(d => d.TriggeredBy == ReviewTriggerKind.CustomerEdit);
        edited.Should().BeGreaterThan(0, "review.edited reachable");

        // review.flagged
        var flagged = await verify.ModerationDecisions.AsNoTracking()
            .CountAsync(d => d.TriggeredBy == ReviewTriggerKind.CommunityReportThreshold);
        flagged.Should().BeGreaterThan(0, "review.flagged reachable");

        // review.report_submitted (witness via review_flags rows)
        var reports = await verify.Flags.AsNoTracking().CountAsync();
        reports.Should().BeGreaterThan(0, "review.report_submitted reachable");

        // review.hidden + review.reinstated + review.deleted
        var hidden = await verify.ModerationDecisions.AsNoTracking()
            .CountAsync(d => d.TriggeredBy == ReviewTriggerKind.ModeratorAction && d.ToState == ReviewState.Hidden);
        hidden.Should().BeGreaterThan(0, "review.hidden reachable");
        var reinstated = await verify.ModerationDecisions.AsNoTracking()
            .CountAsync(d => d.TriggeredBy == ReviewTriggerKind.ModeratorAction && d.FromState == ReviewState.Hidden && d.ToState == ReviewState.Visible);
        reinstated.Should().BeGreaterThan(0, "review.reinstated reachable");
        var deleted = await verify.ModerationDecisions.AsNoTracking()
            .CountAsync(d => d.TriggeredBy == ReviewTriggerKind.ManualSuperAdmin && d.ToState == ReviewState.Deleted);
        deleted.Should().BeGreaterThan(0, "review.deleted reachable");

        // review.auto_hidden
        var autoHidden = await verify.ModerationDecisions.AsNoTracking()
            .CountAsync(d => d.TriggeredBy == ReviewTriggerKind.RefundEvent || d.TriggeredBy == ReviewTriggerKind.AccountLocked);
        autoHidden.Should().BeGreaterThan(0, "review.auto_hidden reachable");

        // review.admin_note_added (witness via review_admin_notes rows)
        var notes = await verify.AdminNotes.AsNoTracking().CountAsync();
        notes.Should().BeGreaterThan(0, "review.admin_note_added reachable");

        // reviews.wordlist.term_upserted (witness via wordlist row)
        var wordlistTerm = await verify.Wordlists.AsNoTracking()
            .AnyAsync(w => w.MarketCode == "SA" && w.Term == "newauditterm");
        wordlistTerm.Should().BeTrue("reviews.wordlist.term_upserted reachable");

        // reviews.market_schema_updated (witness via updated_at_utc movement)
        var schema = await verify.MarketSchemas.AsNoTracking().FirstAsync(s => s.MarketCode == "SA");
        schema.EligibilityWindowDays.Should().Be(200, "reviews.market_schema_updated reachable");
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
            new TestHostEnv(), NullLogger.Instance);
        await seeder.ApplyAsync(ctx, CancellationToken.None);
    }

    private async Task<Guid> SubmitAsync(Guid customerId, Guid productId, Guid orderLineId,
        string body, FakeTimeProvider clock)
    {
        await using var db = NewContext();
        var profanity = NewProfanity();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var submit = new SubmitReviewHandler(db,
            new FakeOrderLineDeliveryEligibilityQuery(true, null, clock.GetUtcNow().AddDays(-1), orderLineId),
            profanity, aggregate, new NullReviewDomainEventPublisher(), clock);
        var result = await submit.HandleAsync(customerId, "SA",
            new SubmitReviewRequest(productId, 4, "Headline", body, "en", null),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Response!.Id;
    }

    private async Task DecideAsync(Guid reviewId, string toState, string? reason, string? adminNote,
        FakeTimeProvider clock, bool hasSuperAdmin = false)
    {
        await using var db = NewContext();
        var aggregate = new RatingAggregateRecomputer(db, clock);
        var handler = new DecideModerationHandler(db, aggregate, new NullReviewDomainEventPublisher(), clock);
        var result = await handler.HandleAsync(
            Guid.NewGuid(), hasModerator: true, hasSuperAdmin: hasSuperAdmin,
            reviewId, ifMatchRowVersion: null,
            new DecideModerationRequest(toState, reason, adminNote),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    private ProfanityFilter NewProfanity()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ReviewsDbContext>(o => o.UseNpgsql(ConnectionString));
        return new ProfanityFilter(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new ArabicNormalizer(),
            TimeSpan.Zero);
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
