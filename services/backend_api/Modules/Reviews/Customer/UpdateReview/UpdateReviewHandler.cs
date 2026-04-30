using System.Text.Json;
using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Filtering;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Customer.UpdateReview;

/// <summary>
/// Handles PATCH /api/customer/reviews/{id} per contract §2.2.
///
/// Edit policy (data-model §3 + Clarification Q3):
/// <list type="bullet">
///   <item>Author-only — caller MUST own the review row.</item>
///   <item><c>deleted</c> is terminal — edits forbidden once there.</item>
///   <item>Edit window enforced from <c>created_at_utc</c> + market policy.</item>
///   <item>Filter + media re-evaluated on every edit; transitions to / re-stamps
///   <c>pending_moderation</c> when either trips, even if currently in
///   <c>visible</c> / <c>flagged</c> / <c>pending_moderation</c>.</item>
///   <item>Optimistic concurrency via xmin — concurrent moderator decision
///   would race; the EF row-version guard surfaces the conflict.</item>
/// </list>
/// </summary>
public sealed class UpdateReviewHandler
{
    private readonly ReviewsDbContext _db;
    private readonly ProfanityFilter _profanity;
    private readonly RatingAggregateRecomputer _aggregate;
    private readonly BackendApi.Modules.Shared.IReviewDomainEventPublisher _events;
    private readonly TimeProvider _time;

    public UpdateReviewHandler(
        ReviewsDbContext db,
        ProfanityFilter profanity,
        RatingAggregateRecomputer aggregate,
        BackendApi.Modules.Shared.IReviewDomainEventPublisher events,
        TimeProvider time)
    {
        _db = db;
        _profanity = profanity;
        _aggregate = aggregate;
        _events = events;
        _time = time;
    }

    public async Task<UpdateReviewResult> HandleAsync(
        Guid customerId,
        Guid reviewId,
        uint? ifMatchRowVersion,
        UpdateReviewRequest body,
        CancellationToken ct)
    {
        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            return UpdateReviewResult.Reject(404, ReviewReasonCode.EditNotAuthor, "Review not found.");
        }
        if (review.CustomerId != customerId)
        {
            return UpdateReviewResult.Reject(403, ReviewReasonCode.EditNotAuthor, "Caller is not the review's author.");
        }
        if (review.State == ReviewState.Deleted)
        {
            return UpdateReviewResult.Reject(400, ReviewReasonCode.EditDeletedTerminal, "Cannot edit a deleted review.");
        }

        if (ifMatchRowVersion is not null && ifMatchRowVersion.Value != review.Xmin)
        {
            return UpdateReviewResult.Reject(409, ReviewReasonCode.RowVersionConflict, "Row version conflict.");
        }

        var policy = await ResolvePolicyAsync(review.MarketCode, ct);
        var nowUtc = _time.GetUtcNow();
        if ((nowUtc - review.CreatedAtUtc).TotalDays > policy.EditWindowDays)
        {
            return UpdateReviewResult.Reject(400, ReviewReasonCode.EditWindowClosed,
                $"Edit window of {policy.EditWindowDays} days has closed.");
        }

        // Apply patch.
        var fromState = review.State;
        if (body.Rating is { } rating)
        {
            if (rating < 1 || rating > 5)
            {
                return UpdateReviewResult.Reject(400, ReviewReasonCode.RatingOutOfRange, "Rating must be 1-5.");
            }
            review.Rating = rating;
        }
        if (body.Headline is not null)
        {
            if (string.IsNullOrWhiteSpace(body.Headline) || body.Headline.Length > 100)
            {
                return UpdateReviewResult.Reject(400, ReviewReasonCode.HeadlineLengthInvalid, "Headline must be 1-100 chars.");
            }
            review.Headline = body.Headline;
        }
        if (body.Body is not null)
        {
            if (string.IsNullOrWhiteSpace(body.Body) || body.Body.Length > 4000)
            {
                return UpdateReviewResult.Reject(400, ReviewReasonCode.BodyLengthInvalid, "Body must be 1-4000 chars.");
            }
            review.Body = body.Body;
        }
        if (body.Locale is not null)
        {
            if (body.Locale is not "ar" and not "en")
            {
                return UpdateReviewResult.Reject(400, ReviewReasonCode.LocaleInvalid, "Locale must be 'ar' or 'en'.");
            }
            review.Locale = body.Locale;
        }
        if (body.MediaUrls is not null)
        {
            if (body.MediaUrls.Count > 4)
            {
                return UpdateReviewResult.Reject(400, ReviewReasonCode.MediaTooMany, "At most 4 media URLs.");
            }
            foreach (var url in body.MediaUrls)
            {
                if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var p) || p.Scheme is not "https")
                {
                    return UpdateReviewResult.Reject(400, ReviewReasonCode.MediaInvalidSignedUrl, "Invalid media URL.");
                }
            }
            review.MediaUrlsJson = JsonSerializer.Serialize(body.MediaUrls);
        }

        // Re-evaluate filter + media against the new content.
        var hasMedia = MediaAttachmentDetector.HasMedia(review.MediaUrlsJson);
        var profanityResult = _profanity.Evaluate(review.MarketCode, review.Headline, review.Body);
        var shouldHold = profanityResult.Tripped || hasMedia;

        ReviewState toState;
        if (shouldHold)
        {
            toState = ReviewState.PendingModeration;
        }
        else if (fromState == ReviewState.PendingModeration)
        {
            // The customer's edit cleared the trip + removed media. Per Clarification Q3 the
            // moderator's prior context is invalidated and the review STAYS held — only an
            // explicit moderator decision can publish. We re-stamp the pending-started clock so
            // the queue surfaces the new content, but do not auto-transition to visible here.
            toState = ReviewState.PendingModeration;
        }
        else
        {
            toState = fromState;
        }

        review.State = toState;
        review.StateChangedAtUtc = nowUtc;
        review.StateChangedByActorId = customerId;
        review.TriggeredBy = ReviewTriggerKind.CustomerEdit;
        review.FilterTripTerms = profanityResult.MatchedTerms.ToArray();
        review.MediaAttachmentReviewRequired = hasMedia;
        review.EditCount += 1;
        if (toState == ReviewState.PendingModeration)
        {
            // Re-stamp on every edit landing in pending_moderation per FR-009 / R9.
            review.PendingModerationStartedAt = nowUtc;
        }

        _db.ModerationDecisions.Add(new ReviewModerationDecision
        {
            Id = Guid.NewGuid(),
            ReviewId = review.Id,
            ActorId = customerId,
            ActorRole = "customer",
            FromState = fromState,
            ToState = toState,
            TriggeredBy = ReviewTriggerKind.CustomerEdit,
            CreatedAtUtc = nowUtc,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UpdateReviewResult.Reject(409, ReviewReasonCode.RowVersionConflict, "Row version conflict.");
        }

        if (ReviewStateMachine.TransitionAffectsAggregate(fromState, toState))
        {
            await _aggregate.RecomputeAsync(review.ProductId, review.MarketCode, ct);
        }

        // FR-038 — fire after commit. ReviewHeldForModeration only when this
        // edit landed in pending_moderation (either via filter trip or via
        // re-stamp on an already-pending row).
        if (toState == ReviewState.PendingModeration)
        {
            await _events.PublishAsync(new BackendApi.Modules.Shared.ReviewHeldForModeration(
                ReviewId: review.Id,
                CustomerId: customerId,
                HoldReason: profanityResult.Tripped ? "edit_trip" : "media_attachment",
                TermCount: profanityResult.MatchedTerms.Count), ct);
        }

        return UpdateReviewResult.Success(new UpdateReviewResponse(
            Id: review.Id,
            State: ToWire(toState),
            RowVersion: review.Xmin,
            UpdatedAtUtc: nowUtc,
            EditCount: review.EditCount,
            PendingReview: toState == ReviewState.PendingModeration));
    }

    private async Task<ReviewMarketPolicy> ResolvePolicyAsync(string marketCode, CancellationToken ct)
    {
        var row = await _db.MarketSchemas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.MarketCode == marketCode, ct);
        if (row is null) return ReviewMarketPolicy.Default(marketCode);

        return new ReviewMarketPolicy(
            row.MarketCode,
            row.EligibilityWindowDays,
            row.EditWindowDays,
            row.CommunityReportThreshold,
            row.CommunityReportWindowDays,
            row.ReportQualifyingAccountAgeDays,
            row.ReportQualifyingRequiresVerifiedBuyer,
            row.PendingModerationSlaHours);
    }

    private static string ToWire(ReviewState s) => s switch
    {
        ReviewState.PendingModeration => "pending_moderation",
        ReviewState.Visible => "visible",
        ReviewState.Flagged => "flagged",
        ReviewState.Hidden => "hidden",
        ReviewState.Deleted => "deleted",
        _ => throw new InvalidOperationException(),
    };
}

public sealed record UpdateReviewResult(
    bool IsSuccess,
    int Status,
    string? ReasonCode,
    string? Detail,
    UpdateReviewResponse? Response)
{
    public static UpdateReviewResult Success(UpdateReviewResponse r) => new(true, 200, null, null, r);
    public static UpdateReviewResult Reject(int status, string reasonCode, string detail) => new(false, status, reasonCode, detail, null);
}
