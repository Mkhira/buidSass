using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BackendApi.Modules.Reviews.Subscribers;

/// <summary>
/// Spec 022 US5 / FR-032 — refund reversals are rare and often manual. Per the
/// Clarification Q5 of /speckit-specify, this handler does NOT auto-reinstate
/// previously-hidden reviews; it logs an operator advisory + appends an
/// admin-readable note so a moderator can decide whether to publicly reinstate.
///
/// Implementation: write an append-only <see cref="Entities.ReviewAdminNote"/>
/// row on every affected review carrying a structured "needs review" message
/// that the queue surfaces as a badge.
/// </summary>
public sealed class RefundReversedHandler : IRefundReversedSubscriber
{
    private readonly ReviewsDbContext _db;
    private readonly ILogger<RefundReversedHandler> _logger;
    private readonly TimeProvider _time;

    public RefundReversedHandler(
        ReviewsDbContext db,
        ILogger<RefundReversedHandler> logger,
        TimeProvider time)
    {
        _db = db;
        _logger = logger;
        _time = time;
    }

    public async Task OnRefundReversedAsync(RefundReversedEvent evt, CancellationToken ct)
    {
        // Affected = currently-hidden reviews tied to the order line. visible/flagged
        // wouldn't have been auto-hidden in the first place; pending_moderation is
        // never auto-affected by refund events.
        var affected = await _db.Reviews
            .Where(r => r.OrderLineId == evt.OrderLineId && r.State == ReviewState.Hidden)
            .ToListAsync(ct);
        if (affected.Count == 0)
        {
            _logger.LogInformation(
                "Refund reversed for order line {OrderLineId} but no hidden reviews are tied to it; no-op.",
                evt.OrderLineId);
            return;
        }

        var nowUtc = _time.GetUtcNow();
        foreach (var review in affected)
        {
            // Idempotency guard — same operator advisory is rewritten if event is
            // redelivered, which is harmless (append-only row with new id).
            _db.AdminNotes.Add(new Entities.ReviewAdminNote
            {
                Id = Guid.NewGuid(),
                ReviewId = review.Id,
                ActorId = Guid.Empty,
                Note = $"Operator advisory: order line {evt.OrderLineId} refund was reversed at {evt.ReversedAtUtc:o} by actor {evt.ActorId}. Reason: {evt.ReasonNote}. Review remains hidden — moderator may manually reinstate (FR-032).",
                CreatedAtUtc = nowUtc,
            });
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Refund-reversed advisory recorded on {Count} review(s) for order line {OrderLineId}.",
            affected.Count, evt.OrderLineId);
    }
}
