using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Subscribers;

/// <summary>
/// Spec 022 US5 / FR-030 — when a return-request reaches the <c>completed</c>
/// terminal state and the payment refund settles, any <c>visible</c> /
/// <c>flagged</c> review tied to the refunded order line auto-transitions to
/// <c>hidden</c> with <c>triggered_by=refund_event</c> and the system actor.
///
/// Idempotent — replays of the same event find the review already in
/// <c>hidden</c> (or beyond) and short-circuit. Aggregate is recomputed once
/// per affected (product, market) pair.
/// </summary>
public sealed class RefundCompletedHandler : IRefundCompletedSubscriber
{
    private readonly ReviewsDbContext _db;
    private readonly RatingAggregateRecomputer _aggregate;
    private readonly TimeProvider _time;

    public RefundCompletedHandler(
        ReviewsDbContext db,
        RatingAggregateRecomputer aggregate,
        TimeProvider time)
    {
        _db = db;
        _aggregate = aggregate;
        _time = time;
    }

    public async Task OnRefundCompletedAsync(RefundCompletedEvent evt, CancellationToken ct)
    {
        var affected = await _db.Reviews
            .Where(r => r.OrderLineId == evt.OrderLineId
                     && (r.State == ReviewState.Visible || r.State == ReviewState.Flagged))
            .ToListAsync(ct);
        if (affected.Count == 0) return;

        var nowUtc = _time.GetUtcNow();
        var aggregateRefreshes = new HashSet<(Guid ProductId, string MarketCode)>();

        foreach (var review in affected)
        {
            var fromState = review.State;
            review.State = ReviewState.Hidden;
            review.StateChangedAtUtc = nowUtc;
            review.StateChangedByActorId = Guid.Empty;
            review.StateChangedReasonNote = $"Auto-hidden — order line {evt.OrderLineId} was refunded.";
            review.TriggeredBy = ReviewTriggerKind.RefundEvent;

            _db.ModerationDecisions.Add(new ReviewModerationDecision
            {
                Id = Guid.NewGuid(),
                ReviewId = review.Id,
                ActorId = Guid.Empty,
                ActorRole = "system",
                FromState = fromState,
                ToState = ReviewState.Hidden,
                TriggeredBy = ReviewTriggerKind.RefundEvent,
                ReasonNote = review.StateChangedReasonNote,
                CreatedAtUtc = nowUtc,
            });

            aggregateRefreshes.Add((review.ProductId, review.MarketCode));
        }

        // Hide + aggregate refresh must be atomic — otherwise an aggregate-refresh
        // failure leaves the review row hidden but the public rating aggregate
        // stale (read-side shows the refunded review as if it still counts).
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.SaveChangesAsync(ct);

        foreach (var (productId, marketCode) in aggregateRefreshes)
        {
            await _aggregate.RecomputeAsync(productId, marketCode, ct);
        }

        await tx.CommitAsync(ct);
    }
}
