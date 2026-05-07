using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Admin.GetReviewDetail;

/// <summary>
/// GET /api/admin/reviews/{id} per contract §3.2 — full review body + audit
/// history + flags + admin notes + the customer's other reviews summary.
/// </summary>
public sealed class GetReviewDetailHandler
{
    private readonly ReviewsDbContext _db;

    public GetReviewDetailHandler(ReviewsDbContext db) => _db = db;

    public async Task<GetReviewDetailResponse?> HandleAsync(Guid reviewId, CancellationToken ct)
    {
        var review = await _db.Reviews.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null) return null;

        var auditHistory = await _db.ModerationDecisions.AsNoTracking()
            .Where(d => d.ReviewId == reviewId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Select(d => new AuditHistoryItem(
                d.Id, d.ActorId, d.ActorRole,
                d.FromState.HasValue ? ToWire(d.FromState.Value) : null,
                ToWire(d.ToState),
                d.TriggeredBy, d.ReasonNote, d.AdminNote, d.CreatedAtUtc))
            .ToListAsync(ct);

        var flags = await _db.Flags.AsNoTracking()
            .Where(f => f.ReviewId == reviewId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new ReviewFlagItem(
                f.Id, f.ReporterActorId, f.Reason, f.Note, f.IsQualified, f.CreatedAtUtc))
            .ToListAsync(ct);

        var adminNotes = await _db.AdminNotes.AsNoTracking()
            .Where(n => n.ReviewId == reviewId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new AdminNoteItem(n.Id, n.ActorId, n.Note, n.CreatedAtUtc))
            .ToListAsync(ct);

        var other = await _db.Reviews.AsNoTracking()
            .Where(r => r.CustomerId == review.CustomerId
                     && r.Id != review.Id
                     && (r.State == ReviewState.Visible || r.State == ReviewState.Flagged))
            .GroupBy(r => 1)
            .Select(g => new CustomerReviewsSummary(
                g.Count(),
                g.Select(r => (decimal?)r.Rating).Average()))
            .FirstOrDefaultAsync(ct)
            ?? new CustomerReviewsSummary(0, null);

        return new GetReviewDetailResponse(
            Id: review.Id,
            CustomerId: review.CustomerId,
            ProductId: review.ProductId,
            OrderLineId: review.OrderLineId,
            MarketCode: review.MarketCode,
            State: ToWire(review.State),
            Rating: review.Rating,
            Headline: review.Headline,
            Body: review.Body,
            Locale: review.Locale,
            MediaUrlsJson: review.MediaUrlsJson,
            MediaAttachmentReviewRequired: review.MediaAttachmentReviewRequired,
            FilterTripTerms: review.FilterTripTerms,
            PendingModerationStartedAt: review.PendingModerationStartedAt,
            CreatedAtUtc: review.CreatedAtUtc,
            DeliveredAtUtc: review.DeliveredAtUtc,
            EditCount: review.EditCount,
            TriggeredBy: review.TriggeredBy,
            StateChangedAtUtc: review.StateChangedAtUtc,
            StateChangedReasonNote: review.StateChangedReasonNote,
            StateChangedAdminNote: review.StateChangedAdminNote,
            RowVersion: review.Xmin,
            AuditHistory: auditHistory,
            Flags: flags,
            AdminNotes: adminNotes,
            CustomerOtherReviews: other);
    }

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

public sealed record GetReviewDetailResponse(
    Guid Id,
    Guid CustomerId,
    Guid ProductId,
    Guid OrderLineId,
    string MarketCode,
    string State,
    int Rating,
    string Headline,
    string Body,
    string Locale,
    string MediaUrlsJson,
    bool MediaAttachmentReviewRequired,
    IReadOnlyList<string> FilterTripTerms,
    DateTimeOffset? PendingModerationStartedAt,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset DeliveredAtUtc,
    int EditCount,
    string TriggeredBy,
    DateTimeOffset StateChangedAtUtc,
    string? StateChangedReasonNote,
    string? StateChangedAdminNote,
    uint RowVersion,
    IReadOnlyList<AuditHistoryItem> AuditHistory,
    IReadOnlyList<ReviewFlagItem> Flags,
    IReadOnlyList<AdminNoteItem> AdminNotes,
    CustomerReviewsSummary CustomerOtherReviews);

public sealed record AuditHistoryItem(
    Guid Id,
    Guid ActorId,
    string ActorRole,
    string? FromState,
    string ToState,
    string TriggeredBy,
    string? ReasonNote,
    string? AdminNote,
    DateTimeOffset CreatedAtUtc);

public sealed record ReviewFlagItem(
    Guid Id,
    Guid ReporterActorId,
    string Reason,
    string? Note,
    bool IsQualified,
    DateTimeOffset CreatedAtUtc);

public sealed record AdminNoteItem(
    Guid Id,
    Guid ActorId,
    string Note,
    DateTimeOffset CreatedAtUtc);

public sealed record CustomerReviewsSummary(int Count, decimal? AvgRating);
