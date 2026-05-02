using System.Text.Json;
using BackendApi.Features.Seeding;
using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BackendApi.Modules.Reviews.Seeding;

/// <summary>
/// Spec 022 task T124 — dev / staging-only synthetic dataset spanning all 5
/// review states with profanity-tripped + community-reported + auto-hidden
/// samples. Powers QA + training without requiring operator workflow drives.
///
/// <para>Hard-gated: <c>RunInProduction = false</c> (the seeding framework's
/// SeedGuard short-circuits Production; this seeder additionally bails when
/// the host environment isn't Development). Idempotent — re-runs are no-ops
/// once the synthetic ids exist.</para>
///
/// <para>State coverage (SC-008):</para>
/// <list type="bullet">
///   <item><b>visible</b>: 4 reviews across 2 products (ratings 5/4/4/3)</item>
///   <item><b>pending_moderation</b>: 2 reviews (one filter-trip, one media-attached)</item>
///   <item><b>flagged</b>: 1 review with 3 qualified community-reports</item>
///   <item><b>hidden</b>: 1 review hidden by moderator action</item>
///   <item><b>deleted</b>: 1 review deleted by super_admin</item>
/// </list>
/// </summary>
public sealed class ReviewsV1DevSeeder : ISeeder
{
    public string Name => "reviews.v1-dev-data";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => ["reviews.reference-data"];

    private static readonly DateTimeOffset BaseTime = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SystemActor = Guid.Empty;

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        if (!ctx.Env.IsDevelopment() && !ctx.Env.IsStaging())
        {
            return;
        }

        var db = ctx.Services.GetRequiredService<ReviewsDbContext>();

        var schemaSeeded = await db.MarketSchemas.AsNoTracking()
            .AnyAsync(s => s.MarketCode == "SA", ct);
        if (!schemaSeeded)
        {
            // Reference data hasn't run yet — bail rather than create reviews
            // pointing at policies that haven't been seeded.
            return;
        }

        var seeds = BuildSyntheticReviews();

        // Idempotency by id — load existing review ids and skip them.
        var existing = await db.Reviews.AsNoTracking()
            .Where(r => seeds.Select(s => s.Review.Id).Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        foreach (var seed in seeds)
        {
            if (existingSet.Contains(seed.Review.Id)) continue;

            db.Reviews.Add(seed.Review);
            db.ModerationDecisions.Add(seed.SubmissionTransition);
            foreach (var follow in seed.FollowUpTransitions ?? Array.Empty<ReviewModerationDecision>())
            {
                db.ModerationDecisions.Add(follow);
            }
            foreach (var flag in seed.Flags ?? Array.Empty<ReviewFlag>())
            {
                db.Flags.Add(flag);
            }
        }

        await db.SaveChangesAsync(ct);

        // Materialize aggregate rows for the visible/flagged products so the
        // public read path returns realistic data immediately after seeding.
        var pairs = seeds
            .Where(s => s.Review.State == ReviewState.Visible
                     || s.Review.State == ReviewState.Flagged)
            .Select(s => (s.Review.ProductId, s.Review.MarketCode))
            .Distinct()
            .ToList();
        var recomputer = ctx.Services.GetService<Aggregate.RatingAggregateRecomputer>();
        if (recomputer is not null)
        {
            foreach (var (productId, marketCode) in pairs)
            {
                await recomputer.RecomputeAsync(productId, marketCode, ct);
            }
        }
    }

    private static IReadOnlyList<SyntheticReview> BuildSyntheticReviews()
    {
        // Stable customer / product / order-line ids per category so reseeding
        // keeps idempotency by id rather than by content fingerprint.
        var customerA = new Guid("33333333-0000-0000-0000-000000000001");
        var customerB = new Guid("33333333-0000-0000-0000-000000000002");
        var customerC = new Guid("33333333-0000-0000-0000-000000000003");
        var customerD = new Guid("33333333-0000-0000-0000-000000000004");
        var productX = new Guid("44444444-0000-0000-0000-000000000001");
        var productY = new Guid("44444444-0000-0000-0000-000000000002");

        // Spec 022 T125 — bilingual editorial-grade seed content. Every
        // Arabic string below MUST be reviewed by an editorial-grade Arabic
        // speaker before launch (T142). The current strings are DRAFT and
        // tracked in Modules/Reviews/Messages/AR_EDITORIAL_REVIEW.md under
        // the "Seeder strings" section. Until sign-off, this seeder is
        // dev / staging only (gated above by IsDevelopment / IsStaging).
        var seeds = new List<SyntheticReview>
        {
            // --- Visible — English (KSA market, dental-procedure tone) ---
            BuildVisible(new("55555555-0000-0000-0000-000000000001"), customerA, productX, "SA", 5,
                "Premium grip on extended procedures",
                "Used these gloves through three back-to-back implant placements. Tactile sensitivity stayed sharp; no tearing at the cuff. Re-ordering."),
            BuildVisible(new("55555555-0000-0000-0000-000000000002"), customerB, productX, "SA", 4,
                "Solid build, fair value",
                "Comparable to the brand we used in residency. Slightly tighter at the wrist than expected; otherwise reliable for a busy operatory."),

            // --- Visible — Arabic (EG market) — DRAFT pending T142 ---
            BuildVisible(new("55555555-0000-0000-0000-000000000003"), customerC, productY, "EG", 4,
                "أداء ممتاز يومياً في العيادة",
                "استخدمته في عدة جلسات تنظيف وحشوة، نتائج ثابتة. وصل التغليف بحالة جيدة لكن مع تأخر يوم عن الموعد المتوقع."),
            BuildVisible(new("55555555-0000-0000-0000-000000000004"), customerD, productY, "EG", 3,
                "مقبول للاستخدام المتكرر",
                "يفي بالغرض في الإجراءات الاعتيادية. الجودة متوسطة مقارنة بالسعر؛ سأبحث عن بديل قبل الطلب القادم."),

            // --- Pending moderation: profanity-tripped (filter wordlist) ---
            BuildPending(new("55555555-0000-0000-0000-000000000005"), customerA, productY, "SA",
                "Beware spam content",
                "This listing seems like spam pretending to be a clinical product. Photos do not match what was delivered.",
                tripTerms: new[] { "spam" }, hasMedia: false),

            // --- Pending moderation: media-attached auto-hold (FR-014a) ---
            BuildPending(new("55555555-0000-0000-0000-000000000006"), customerB, productY, "SA",
                "Photo of packaging issue",
                "Body content is professional; attaching a photo of the seal damage on arrival for the moderator to verify.",
                tripTerms: Array.Empty<string>(), hasMedia: true),

            // --- Flagged: 3 qualified community reports escalated visible→flagged ---
            BuildFlagged(new("55555555-0000-0000-0000-000000000007"), customerC, productX, "SA", 1,
                "Marketing claims do not match",
                "Product description on the listing is materially different from what arrived. Reported by multiple verified buyers.",
                qualifiedReporters: 3),

            // --- Hidden: moderator action with structured operator reason ---
            BuildHidden(new("55555555-0000-0000-0000-000000000008"), customerD, productX, "SA", 2,
                "Persistent odor on use",
                "Latex odor remained noticeable through three procedures despite airing the box overnight.",
                hideReason: "Suspected coordinated-review pattern; under support investigation pending verification."),

            // --- Deleted: super_admin terminal action ---
            BuildDeleted(new("55555555-0000-0000-0000-000000000009"), customerA, productX, "EG", 1,
                "Body removed by moderator",
                "Body content removed by super_admin per repeated personal-attack policy violations across multiple hides.",
                deleteReason: "Repeated personal-attack content despite three prior hides; FR-005a forbids hard-delete, super_admin terminal applied per §3.3 RBAC."),
        };

        return seeds;
    }

    private static SyntheticReview BuildVisible(
        Guid id, Guid customerId, Guid productId, string market, int rating,
        string headline, string body)
    {
        var review = NewReview(id, customerId, productId, market, rating, headline, body,
            ReviewState.Visible, ReviewTriggerKind.CustomerSubmission);
        var submission = NewTransition(review.Id, customerId, "customer",
            ReviewState.Visible, ReviewState.Visible, ReviewTriggerKind.CustomerSubmission);
        return new SyntheticReview(review, submission);
    }

    private static SyntheticReview BuildPending(
        Guid id, Guid customerId, Guid productId, string market,
        string headline, string body, string[] tripTerms, bool hasMedia)
    {
        var review = NewReview(id, customerId, productId, market, 3, headline, body,
            ReviewState.PendingModeration, ReviewTriggerKind.CustomerSubmission);
        review.PendingModerationStartedAt = BaseTime;
        review.FilterTripTerms = tripTerms;
        review.MediaAttachmentReviewRequired = hasMedia;
        if (hasMedia)
        {
            review.MediaUrlsJson = JsonSerializer.Serialize(new[] { "https://storage.test/dev-seed-media" });
        }
        var submission = NewTransition(review.Id, customerId, "customer",
            ReviewState.Visible, ReviewState.PendingModeration, ReviewTriggerKind.CustomerSubmission);
        return new SyntheticReview(review, submission);
    }

    private static SyntheticReview BuildFlagged(
        Guid id, Guid customerId, Guid productId, string market, int rating,
        string headline, string body, int qualifiedReporters)
    {
        var review = NewReview(id, customerId, productId, market, rating, headline, body,
            ReviewState.Flagged, ReviewTriggerKind.CommunityReportThreshold);
        review.StateChangedAtUtc = BaseTime.AddHours(2);
        var submission = NewTransition(review.Id, customerId, "customer",
            ReviewState.Visible, ReviewState.Visible, ReviewTriggerKind.CustomerSubmission);
        var flagTransition = NewTransition(review.Id, SystemActor, "system",
            ReviewState.Visible, ReviewState.Flagged, ReviewTriggerKind.CommunityReportThreshold);
        flagTransition.CreatedAtUtc = BaseTime.AddHours(2);

        var flags = new List<ReviewFlag>();
        for (var i = 0; i < qualifiedReporters; i++)
        {
            flags.Add(new ReviewFlag
            {
                Id = Guid.NewGuid(),
                ReviewId = review.Id,
                ReporterActorId = new Guid($"77777777-0000-0000-0000-{i:D12}"),
                Reason = "false_or_misleading",
                IsQualified = true,
                QualifyingEvaluationJson = JsonSerializer.Serialize(new
                {
                    account_age_days = 60,
                    has_delivered_order = true,
                    qualifying_account_age_days = 14,
                    qualifying_requires_verified_buyer = true,
                    evaluated_at_utc = BaseTime.AddHours(1).UtcDateTime,
                }),
                CreatedAtUtc = BaseTime.AddHours(1),
            });
        }

        return new SyntheticReview(review, submission, new[] { flagTransition }, flags);
    }

    private static SyntheticReview BuildHidden(
        Guid id, Guid customerId, Guid productId, string market, int rating,
        string headline, string body, string hideReason)
    {
        var review = NewReview(id, customerId, productId, market, rating, headline, body,
            ReviewState.Hidden, ReviewTriggerKind.ModeratorAction);
        review.StateChangedReasonNote = hideReason;
        review.StateChangedAtUtc = BaseTime.AddHours(3);

        var submission = NewTransition(review.Id, customerId, "customer",
            ReviewState.Visible, ReviewState.Visible, ReviewTriggerKind.CustomerSubmission);
        var hideTransition = NewTransition(review.Id, new Guid("66666666-0000-0000-0000-000000000001"),
            "reviews.moderator", ReviewState.Visible, ReviewState.Hidden, ReviewTriggerKind.ModeratorAction);
        hideTransition.ReasonNote = hideReason;
        hideTransition.CreatedAtUtc = BaseTime.AddHours(3);

        return new SyntheticReview(review, submission, new[] { hideTransition });
    }

    private static SyntheticReview BuildDeleted(
        Guid id, Guid customerId, Guid productId, string market, int rating,
        string headline, string body, string deleteReason)
    {
        var review = NewReview(id, customerId, productId, market, rating, headline, body,
            ReviewState.Deleted, ReviewTriggerKind.ManualSuperAdmin);
        review.StateChangedReasonNote = deleteReason;
        review.StateChangedAtUtc = BaseTime.AddHours(4);

        var submission = NewTransition(review.Id, customerId, "customer",
            ReviewState.Visible, ReviewState.Visible, ReviewTriggerKind.CustomerSubmission);
        var hideTransition = NewTransition(review.Id, new Guid("66666666-0000-0000-0000-000000000001"),
            "reviews.moderator", ReviewState.Visible, ReviewState.Hidden, ReviewTriggerKind.ModeratorAction);
        hideTransition.ReasonNote = "Initial hide before delete.";
        hideTransition.CreatedAtUtc = BaseTime.AddHours(3);
        var deleteTransition = NewTransition(review.Id, new Guid("66666666-0000-0000-0000-000000000002"),
            "super_admin", ReviewState.Hidden, ReviewState.Deleted, ReviewTriggerKind.ManualSuperAdmin);
        deleteTransition.ReasonNote = deleteReason;
        deleteTransition.CreatedAtUtc = BaseTime.AddHours(4);

        return new SyntheticReview(review, submission, new[] { hideTransition, deleteTransition });
    }

    private static Review NewReview(
        Guid id, Guid customerId, Guid productId, string market, int rating,
        string headline, string body, ReviewState state, string triggeredBy) => new()
        {
            Id = id,
            CustomerId = customerId,
            ProductId = productId,
            OrderLineId = Guid.NewGuid(),
            MarketCode = market,
            Rating = rating,
            Headline = headline,
            Body = body,
            Locale = market == "EG" ? "ar" : "en",
            MediaUrlsJson = "[]",
            State = state,
            StateChangedAtUtc = BaseTime,
            StateChangedByActorId = customerId,
            TriggeredBy = triggeredBy,
            FilterTripTerms = Array.Empty<string>(),
            EditCount = 0,
            CreatedAtUtc = BaseTime,
            DeliveredAtUtc = BaseTime.AddDays(-7),
        };

    private static ReviewModerationDecision NewTransition(
        Guid reviewId, Guid actorId, string actorRole,
        ReviewState fromState, ReviewState toState, string trigger) => new()
        {
            Id = Guid.NewGuid(),
            ReviewId = reviewId,
            ActorId = actorId,
            ActorRole = actorRole,
            FromState = fromState,
            ToState = toState,
            TriggeredBy = trigger,
            CreatedAtUtc = BaseTime,
        };

    private sealed record SyntheticReview(
        Review Review,
        ReviewModerationDecision SubmissionTransition,
        IReadOnlyList<ReviewModerationDecision>? FollowUpTransitions = null,
        IReadOnlyList<ReviewFlag>? Flags = null);
}
