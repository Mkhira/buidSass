using System.Text.Json;
using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackendApi.Modules.Reviews.Customer.ReportReview;

/// <summary>
/// Handles POST /api/customer/reviews/{id}/report per contract §2.5.
///
/// Pipeline:
/// <list type="number">
///   <item>Reject self-reports (caller is the review's author).</item>
///   <item>Resolve the per-market policy (threshold, window, qualifying-reporter rules).</item>
///   <item>Lookup reporter facts via <see cref="IReviewReporterFactsQuery"/>; evaluate
///   <see cref="QualifiedReporterPolicy"/>; capture the verdict + raw inputs on the row.</item>
///   <item>Insert the <see cref="ReviewFlag"/> row. The unique constraint
///   <c>UX_review_flags_review_reporter</c> catches duplicate (review, reporter)
///   pairs idempotently — the loser surfaces <c>review.report.already_reported_by_actor</c>.</item>
///   <item>If the flag is qualified AND the review is currently in
///   <see cref="ReviewState.Visible"/>, recount qualified reports within the
///   per-market window. When the count crosses the threshold, transition
///   <c>visible → flagged</c> in the same transaction (state machine validates;
///   audit row written via <see cref="ReviewModerationDecision"/>; aggregate
///   refresh skipped because both states count in the aggregate per
///   <see cref="ReviewStateMachine.TransitionAffectsAggregate"/>).</item>
/// </list>
/// </summary>
public sealed class ReportReviewHandler
{
    private readonly ReviewsDbContext _db;
    private readonly IReviewReporterFactsQuery _facts;
    private readonly TimeProvider _time;

    public ReportReviewHandler(ReviewsDbContext db, IReviewReporterFactsQuery facts, TimeProvider time)
    {
        _db = db;
        _facts = facts;
        _time = time;
    }

    public async Task<ReportReviewResult> HandleAsync(
        Guid reporterId,
        Guid reviewId,
        ReportReviewRequest body,
        CancellationToken ct)
    {
        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            // 404 → dedicated row.not_found code so consumers/telemetry don't
            // misclassify it as an optimistic-concurrency failure.
            return ReportReviewResult.Reject(404, ReviewReasonCode.RowNotFound,
                "Review not found.");
        }
        if (review.CustomerId == reporterId)
        {
            return ReportReviewResult.Reject(400, ReviewReasonCode.ReportCannotReportOwnReview,
                "You cannot report your own review.");
        }

        var policy = await ResolvePolicyAsync(review.MarketCode, ct);
        var reporterFacts = await _facts.GetAsync(reporterId, ct);
        var qualified = QualifiedReporterPolicy.Evaluate(
            new QualifiedReporterPolicy.ReporterFacts(reporterFacts.AccountAgeDays, reporterFacts.HasDeliveredOrder),
            policy);

        var nowUtc = _time.GetUtcNow();
        var evaluationJson = JsonSerializer.Serialize(new
        {
            account_age_days = reporterFacts.AccountAgeDays,
            has_delivered_order = reporterFacts.HasDeliveredOrder,
            qualifying_account_age_days = policy.ReportQualifyingAccountAgeDays,
            qualifying_requires_verified_buyer = policy.ReportQualifyingRequiresVerifiedBuyer,
            evaluated_at_utc = nowUtc.UtcDateTime,
        });

        var flag = new ReviewFlag
        {
            Id = Guid.NewGuid(),
            ReviewId = reviewId,
            ReporterActorId = reporterId,
            Reason = body.Reason,
            Note = body.Note,
            IsQualified = qualified,
            QualifyingEvaluationJson = evaluationJson,
            CreatedAtUtc = nowUtc,
        };
        _db.Flags.Add(flag);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.Entry(flag).State = EntityState.Detached;
            return ReportReviewResult.Reject(409, ReviewReasonCode.ReportAlreadyReportedByActor,
                "You have already reported this review.");
        }

        // Threshold evaluation — only meaningful when the just-inserted flag is qualified
        // AND the review is in a state from which the system can transition to Flagged.
        var qualifiedCount = await CountQualifiedFlagsAsync(reviewId, nowUtc, policy.CommunityReportWindowDays, ct);
        if (qualified && review.State == ReviewState.Visible && qualifiedCount >= policy.CommunityReportThreshold)
        {
            // State-machine guard — system actor + community_report_threshold trigger.
            var allowed = ReviewStateMachine.TryTransition(
                ReviewState.Visible, ReviewState.Flagged,
                ReviewTriggerKind.CommunityReportThreshold, ReviewActorKind.System, out _);
            if (allowed)
            {
                review.State = ReviewState.Flagged;
                review.StateChangedAtUtc = nowUtc;
                review.StateChangedByActorId = Guid.Empty; // system actor
                review.TriggeredBy = ReviewTriggerKind.CommunityReportThreshold;

                _db.ModerationDecisions.Add(new ReviewModerationDecision
                {
                    Id = Guid.NewGuid(),
                    ReviewId = reviewId,
                    ActorId = Guid.Empty,
                    ActorRole = "system",
                    FromState = ReviewState.Visible,
                    ToState = ReviewState.Flagged,
                    TriggeredBy = ReviewTriggerKind.CommunityReportThreshold,
                    CreatedAtUtc = nowUtc,
                });

                await _db.SaveChangesAsync(ct);
                // Aggregate is unchanged because Visible and Flagged both count.
            }
        }

        return ReportReviewResult.Success(new ReportReviewResponse(
            FlagId: flag.Id,
            Qualified: qualified,
            ThresholdProgress: new ThresholdProgress(
                QualifiedCount: qualifiedCount,
                Threshold: policy.CommunityReportThreshold)));
    }

    private async Task<int> CountQualifiedFlagsAsync(
        Guid reviewId, DateTimeOffset nowUtc, int windowDays, CancellationToken ct)
    {
        var since = nowUtc - TimeSpan.FromDays(windowDays);
        return await _db.Flags
            .Where(f => f.ReviewId == reviewId
                     && f.IsQualified
                     && f.CreatedAtUtc >= since)
            .CountAsync(ct);
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

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
}

public sealed record ReportReviewResult(
    bool IsSuccess,
    int Status,
    string? ReasonCode,
    string? Detail,
    ReportReviewResponse? Response)
{
    public static ReportReviewResult Success(ReportReviewResponse r) => new(true, 201, null, null, r);
    public static ReportReviewResult Reject(int status, string code, string detail) => new(false, status, code, detail, null);
}
