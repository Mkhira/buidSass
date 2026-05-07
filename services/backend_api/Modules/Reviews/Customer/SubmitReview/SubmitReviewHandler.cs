using System.Text.Json;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Reviews.Customer.SubmitReview;

/// <summary>
/// Handles POST /api/customer/reviews per contract §2.1 + spec quickstart §4.
///
/// Pipeline (in order):
/// <list type="number">
///   <item>Resolve market policy (defaults to <see cref="ReviewMarketPolicy.Default"/> when row missing).</item>
///   <item>Eligibility query against spec 011's <see cref="IOrderLineDeliveryEligibilityQuery"/>.</item>
///   <item>Eligibility-window check (now − delivered_at &lt;= eligibility_window_days).</item>
///   <item>Profanity filter scan (text-only) — captures matched terms.</item>
///   <item>Media-attachment auto-hold (FR-014a) — any attached URL forces pending_moderation.</item>
///   <item>Persist in <c>visible</c> when clean + no media; else <c>pending_moderation</c>.</item>
///   <item>Append the audit-detail row in the same transaction.</item>
///   <item>Inline rating-aggregate refresh when state ends in a counted state.</item>
/// </list>
/// Concurrency: the unique partial index <c>UX_reviews_customer_product_active</c>
/// prevents two concurrent submissions for the same (customer, product); the
/// loser sees <see cref="ReviewReasonCode.EligibilityAlreadyReviewed"/>.
/// </summary>
public sealed class SubmitReviewHandler
{
    private readonly ReviewsDbContext _db;
    private readonly IOrderLineDeliveryEligibilityQuery _eligibility;
    private readonly ProfanityFilter _profanity;
    private readonly RatingAggregateRecomputer _aggregate;
    private readonly TimeProvider _time;

    public SubmitReviewHandler(
        ReviewsDbContext db,
        IOrderLineDeliveryEligibilityQuery eligibility,
        ProfanityFilter profanity,
        RatingAggregateRecomputer aggregate,
        TimeProvider time)
    {
        _db = db;
        _eligibility = eligibility;
        _profanity = profanity;
        _aggregate = aggregate;
        _time = time;
    }

    public async Task<SubmitReviewResult> HandleAsync(
        Guid customerId,
        string marketCode,
        SubmitReviewRequest body,
        CancellationToken ct)
    {
        var policy = await ResolvePolicyAsync(marketCode, ct);
        var nowUtc = _time.GetUtcNow();

        var existing = await _db.Reviews
            .Where(r => r.CustomerId == customerId && r.ProductId == body.ProductId && r.State != Primitives.ReviewState.Deleted)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return SubmitReviewResult.Reject(ReviewReasonCode.EligibilityAlreadyReviewed,
                "A review already exists for this product.",
                new Dictionary<string, object?> { ["existing_review_id"] = existing.Id });
        }

        var eligibility = await _eligibility.IsEligibleForReviewAsync(customerId, body.ProductId, ct);
        if (!eligibility.Eligible)
        {
            return SubmitReviewResult.Reject(
                eligibility.ReasonCode ?? ReviewReasonCode.EligibilityNoDeliveredPurchase,
                "Review eligibility check failed.");
        }

        if (eligibility.DeliveredAt is null || eligibility.OrderLineId is null)
        {
            return SubmitReviewResult.Reject(ReviewReasonCode.EligibilityNoDeliveredPurchase,
                "Eligibility evaluation returned without a delivery anchor.");
        }

        var deliveredAt = eligibility.DeliveredAt.Value;
        if ((nowUtc - deliveredAt).TotalDays > policy.EligibilityWindowDays)
        {
            return SubmitReviewResult.Reject(ReviewReasonCode.EligibilityWindowClosed,
                $"Review window of {policy.EligibilityWindowDays} days has closed.");
        }

        var hasMedia = body.MediaUrls is { Count: > 0 };
        var profanityResult = await _profanity.EvaluateAsync(marketCode, new[] { body.Headline, body.Body }, ct);
        var holdForModeration = profanityResult.Tripped || hasMedia;

        var initialState = holdForModeration ? Primitives.ReviewState.PendingModeration : Primitives.ReviewState.Visible;
        var review = new Review
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            ProductId = body.ProductId,
            OrderLineId = eligibility.OrderLineId.Value,
            MarketCode = marketCode,
            Rating = body.Rating,
            Headline = body.Headline,
            Body = body.Body,
            Locale = body.Locale,
            MediaUrlsJson = JsonSerializer.Serialize(body.MediaUrls ?? Array.Empty<string>()),
            State = initialState,
            StateChangedAtUtc = nowUtc,
            StateChangedByActorId = customerId,
            TriggeredBy = ReviewTriggerKind.CustomerSubmission,
            PendingModerationStartedAt = holdForModeration ? nowUtc : null,
            FilterTripTerms = profanityResult.MatchedTerms.ToArray(),
            MediaAttachmentReviewRequired = hasMedia,
            EditCount = 0,
            CreatedAtUtc = nowUtc,
            DeliveredAtUtc = deliveredAt,
        };

        _db.Reviews.Add(review);
        _db.ModerationDecisions.Add(new ReviewModerationDecision
        {
            Id = Guid.NewGuid(),
            ReviewId = review.Id,
            ActorId = customerId,
            ActorRole = "customer",
            // Submission has no prior state — leave FromState null rather than
            // stamping Visible (M3): a Visible sentinel falsely read as
            // Visible→PendingModeration in the audit log when the submission
            // landed in moderation hold.
            FromState = null,
            ToState = initialState,
            TriggeredBy = ReviewTriggerKind.CustomerSubmission,
            CreatedAtUtc = nowUtc,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race-condition fallback: another request slipped a non-deleted
            // review for the same (customer, product) in between our read and
            // INSERT. Surface the same reason code as the read-side check.
            return SubmitReviewResult.Reject(ReviewReasonCode.EligibilityAlreadyReviewed,
                "A review already exists for this product.");
        }

        if (Primitives.ReviewStateMachine.CountsInAggregate(initialState))
        {
            await _aggregate.RecomputeAsync(review.ProductId, marketCode, ct);
        }

        return SubmitReviewResult.Success(new SubmitReviewResponse(
            Id: review.Id,
            State: ToWire(initialState),
            RowVersion: review.Xmin,
            CreatedAtUtc: review.CreatedAtUtc,
            PendingReview: holdForModeration));
    }

    private async Task<ReviewMarketPolicy> ResolvePolicyAsync(string marketCode, CancellationToken ct)
    {
        var row = await _db.MarketSchemas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.MarketCode == marketCode, ct);
        if (row is null) return ReviewMarketPolicy.Default(marketCode);

        return new ReviewMarketPolicy(
            MarketCode: row.MarketCode,
            EligibilityWindowDays: row.EligibilityWindowDays,
            EditWindowDays: row.EditWindowDays,
            CommunityReportThreshold: row.CommunityReportThreshold,
            CommunityReportWindowDays: row.CommunityReportWindowDays,
            ReportQualifyingAccountAgeDays: row.ReportQualifyingAccountAgeDays,
            ReportQualifyingRequiresVerifiedBuyer: row.ReportQualifyingRequiresVerifiedBuyer,
            PendingModerationSlaHours: row.PendingModerationSlaHours);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    private static string ToWire(Primitives.ReviewState s) => s switch
    {
        Primitives.ReviewState.PendingModeration => "pending_moderation",
        Primitives.ReviewState.Visible => "visible",
        Primitives.ReviewState.Flagged => "flagged",
        Primitives.ReviewState.Hidden => "hidden",
        Primitives.ReviewState.Deleted => "deleted",
        _ => throw new InvalidOperationException(),
    };
}

public sealed record SubmitReviewResult(
    bool IsSuccess,
    string? ReasonCode,
    string? Detail,
    SubmitReviewResponse? Response,
    IDictionary<string, object?>? Extensions)
{
    public static SubmitReviewResult Success(SubmitReviewResponse r) =>
        new(true, null, null, r, null);

    public static SubmitReviewResult Reject(
        string reasonCode,
        string detail,
        IDictionary<string, object?>? extensions = null) =>
        new(false, reasonCode, detail, null, extensions);
}
