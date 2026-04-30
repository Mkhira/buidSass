using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Admin.DecideModeration;

/// <summary>
/// Handles POST /api/admin/reviews/{id}/decide per contract §3.3 + quickstart §7.
///
/// Pipeline:
/// <list type="number">
///   <item>Resolve target state from request; reject unknown values upstream via the validator.</item>
///   <item>Load the review; reject 404 if not found.</item>
///   <item>RBAC chord: caller MUST hold <c>reviews.moderator</c>; <c>deleted</c> requires <c>super_admin</c>.</item>
///   <item>Optimistic-concurrency check via <c>If-Match: row_version</c>; mismatch → 409.</item>
///   <item>Pure-function state-machine guard from <see cref="ReviewStateMachine"/>; bad transition → 400 / 400.delete_terminal.</item>
///   <item>Mutate the row, write append-only <see cref="ReviewModerationDecision"/> row, commit.</item>
///   <item>Refresh the rating aggregate inline when the transition crosses the counted-state boundary
///   (<see cref="ReviewStateMachine.TransitionAffectsAggregate"/>). visible↔flagged transitions
///   are no-ops for aggregate values.</item>
/// </list>
/// </summary>
public sealed class DecideModerationHandler
{
    private readonly ReviewsDbContext _db;
    private readonly RatingAggregateRecomputer _aggregate;
    private readonly TimeProvider _time;

    public DecideModerationHandler(
        ReviewsDbContext db,
        RatingAggregateRecomputer aggregate,
        TimeProvider time)
    {
        _db = db;
        _aggregate = aggregate;
        _time = time;
    }

    public async Task<DecideModerationResult> HandleAsync(
        Guid actorId,
        bool hasModerator,
        bool hasSuperAdmin,
        Guid reviewId,
        uint? ifMatchRowVersion,
        DecideModerationRequest body,
        CancellationToken ct)
    {
        if (!hasModerator)
        {
            return DecideModerationResult.Reject(403, ReviewReasonCode.ModerationForbidden,
                "reviews.moderator permission required.");
        }

        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            return DecideModerationResult.Reject(404, ReviewReasonCode.ModerationInvalidState, "Review not found.");
        }

        if (ifMatchRowVersion is not null && ifMatchRowVersion.Value != review.Xmin)
        {
            return DecideModerationResult.Reject(409, ReviewReasonCode.ModerationVersionConflict,
                "Row version conflict — review changed during decision.");
        }

        var toState = ParseTargetState(body.ToState);
        if (toState is null)
        {
            return DecideModerationResult.Reject(400, ReviewReasonCode.ModerationInvalidState,
                "Unknown target state.");
        }

        // delete chord — only super_admin may transition to Deleted.
        if (toState == ReviewState.Deleted && !hasSuperAdmin)
        {
            return DecideModerationResult.Reject(403, ReviewReasonCode.ModerationDeleteRequiresSuperAdmin,
                "Only super_admin may delete a review.");
        }

        // Idempotency: a no-op decision returns 200 without writing audit / aggregate.
        if (review.State == toState.Value)
        {
            return DecideModerationResult.Success(BuildResponse(review));
        }

        var trigger = toState == ReviewState.Deleted
            ? ReviewTriggerKind.ManualSuperAdmin
            : ReviewTriggerKind.ModeratorAction;
        var actorKind = toState == ReviewState.Deleted
            ? ReviewActorKind.SuperAdmin
            : ReviewActorKind.Moderator;

        if (!ReviewStateMachine.TryTransition(review.State, toState.Value, trigger, actorKind, out var failureReason))
        {
            // delete_terminal vs invalid_state distinguishes "from deleted" from "to unreachable".
            var status = failureReason == ReviewReasonCode.ModerationDeleteTerminal ? 400 : 400;
            return DecideModerationResult.Reject(status,
                failureReason ?? ReviewReasonCode.ModerationInvalidState,
                "Transition not allowed from current state.");
        }

        var nowUtc = _time.GetUtcNow();
        var fromState = review.State;
        review.State = toState.Value;
        review.StateChangedAtUtc = nowUtc;
        review.StateChangedByActorId = actorId;
        review.StateChangedReasonNote = body.ReasonNote;
        review.StateChangedAdminNote = body.AdminNote;
        review.TriggeredBy = trigger;
        if (toState == ReviewState.PendingModeration)
        {
            review.PendingModerationStartedAt = nowUtc;
        }

        _db.ModerationDecisions.Add(new ReviewModerationDecision
        {
            Id = Guid.NewGuid(),
            ReviewId = review.Id,
            ActorId = actorId,
            ActorRole = toState == ReviewState.Deleted ? "super_admin" : "reviews.moderator",
            FromState = fromState,
            ToState = toState.Value,
            TriggeredBy = trigger,
            ReasonNote = body.ReasonNote,
            AdminNote = body.AdminNote,
            CreatedAtUtc = nowUtc,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DecideModerationResult.Reject(409, ReviewReasonCode.ModerationVersionConflict,
                "Row version conflict — review changed during decision.");
        }

        if (ReviewStateMachine.TransitionAffectsAggregate(fromState, toState.Value))
        {
            await _aggregate.RecomputeAsync(review.ProductId, review.MarketCode, ct);
        }

        return DecideModerationResult.Success(BuildResponse(review));
    }

    private static DecideModerationResponse BuildResponse(Review r) => new(
        Id: r.Id,
        State: ToWire(r.State),
        RowVersion: r.Xmin,
        StateChangedAtUtc: r.StateChangedAtUtc,
        StateChangedByActorId: r.StateChangedByActorId);

    private static ReviewState? ParseTargetState(string raw) => raw switch
    {
        "visible" => ReviewState.Visible,
        "hidden" => ReviewState.Hidden,
        "deleted" => ReviewState.Deleted,
        _ => null,
    };

    private static string ToWire(ReviewState s) => s switch
    {
        ReviewState.PendingModeration => "pending_moderation",
        ReviewState.Visible => "visible",
        ReviewState.Flagged => "flagged",
        ReviewState.Hidden => "hidden",
        ReviewState.Deleted => "deleted",
        _ => "unknown",
    };
}

public sealed record DecideModerationResult(
    bool IsSuccess,
    int Status,
    string? ReasonCode,
    string? Detail,
    DecideModerationResponse? Response)
{
    public static DecideModerationResult Success(DecideModerationResponse r) => new(true, 200, null, null, r);
    public static DecideModerationResult Reject(int status, string code, string detail) => new(false, status, code, detail, null);
}
