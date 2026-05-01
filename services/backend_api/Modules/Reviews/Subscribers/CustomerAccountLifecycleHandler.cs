using BackendApi.Modules.Reviews.Aggregate;
using BackendApi.Modules.Reviews.Entities;
using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Subscribers;

/// <summary>
/// Spec 022 US5 / FR-031 — when a customer account is locked or deleted, every
/// <c>visible</c> / <c>flagged</c> review the customer authored auto-transitions
/// to <c>hidden</c> with the appropriate <c>triggered_by</c> discriminator.
///
/// Implements <see cref="ICustomerAccountLifecycleSubscriber"/> from spec 020;
/// market-change is a no-op for reviews (they were authored under the prior
/// market and stay there per Principle 5).
/// </summary>
public sealed class CustomerAccountLifecycleHandler : ICustomerAccountLifecycleSubscriber
{
    private readonly ReviewsDbContext _db;
    private readonly RatingAggregateRecomputer _aggregate;
    private readonly TimeProvider _time;

    public CustomerAccountLifecycleHandler(
        ReviewsDbContext db,
        RatingAggregateRecomputer aggregate,
        TimeProvider time)
    {
        _db = db;
        _aggregate = aggregate;
        _time = time;
    }

    public Task OnAccountLockedAsync(CustomerAccountLocked evt, CancellationToken ct) =>
        AutoHideAsync(evt.CustomerId, ReviewTriggerKind.AccountLocked, "account_locked", ct);

    public Task OnAccountDeletedAsync(CustomerAccountDeleted evt, CancellationToken ct) =>
        AutoHideAsync(evt.CustomerId, ReviewTriggerKind.AccountDeleted, "account_deleted", ct);

    public Task OnMarketChangedAsync(CustomerMarketChanged evt, CancellationToken ct) =>
        Task.CompletedTask;

    private async Task AutoHideAsync(
        Guid customerId, string triggerKind, string reasonLabel, CancellationToken ct)
    {
        var affected = await _db.Reviews
            .Where(r => r.CustomerId == customerId
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
            review.StateChangedReasonNote = $"Auto-hidden — customer {reasonLabel}.";
            review.TriggeredBy = triggerKind;

            _db.ModerationDecisions.Add(new ReviewModerationDecision
            {
                Id = Guid.NewGuid(),
                ReviewId = review.Id,
                ActorId = Guid.Empty,
                ActorRole = "system",
                FromState = fromState,
                ToState = ReviewState.Hidden,
                TriggeredBy = triggerKind,
                ReasonNote = review.StateChangedReasonNote,
                CreatedAtUtc = nowUtc,
            });

            aggregateRefreshes.Add((review.ProductId, review.MarketCode));
        }

        // Hide + aggregate refresh must be atomic — otherwise an aggregate
        // recompute failure leaves the review hidden but the public rating
        // aggregate stale (CodeRabbit PR #48 round 2; mirrors the pattern in
        // RefundCompletedHandler).
        var supportsTransactions = _db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";
        var tx = supportsTransactions
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            await _db.SaveChangesAsync(ct);

            foreach (var (productId, marketCode) in aggregateRefreshes)
            {
                await _aggregate.RecomputeAsync(productId, marketCode, ct);
            }

            if (tx is not null) await tx.CommitAsync(ct);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }
}
