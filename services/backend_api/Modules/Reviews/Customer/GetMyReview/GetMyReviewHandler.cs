using BackendApi.Modules.Reviews.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Customer.GetMyReview;

public sealed class GetMyReviewHandler
{
    private readonly ReviewsDbContext _db;

    public GetMyReviewHandler(ReviewsDbContext db) => _db = db;

    public async Task<GetMyReviewResponse?> HandleAsync(Guid customerId, Guid reviewId, CancellationToken ct)
    {
        var row = await _db.Reviews.AsNoTracking()
            .Where(r => r.Id == reviewId && r.CustomerId == customerId)
            .Select(r => new GetMyReviewResponse(
                r.Id,
                r.ProductId,
                r.MarketCode,
                ToWire(r.State),
                r.Rating,
                r.Headline,
                r.Body,
                r.Locale,
                r.MediaUrlsJson,
                r.CreatedAtUtc,
                r.StateChangedAtUtc,
                r.EditCount,
                r.State == Primitives.ReviewState.PendingModeration,
                r.StateChangedReasonNote,
                r.Xmin))
            .FirstOrDefaultAsync(ct);
        return row;
    }

    private static string ToWire(Primitives.ReviewState s) => s switch
    {
        Primitives.ReviewState.PendingModeration => "pending_moderation",
        Primitives.ReviewState.Visible => "visible",
        Primitives.ReviewState.Flagged => "flagged",
        Primitives.ReviewState.Hidden => "hidden",
        Primitives.ReviewState.Deleted => "deleted",
        _ => "unknown",
    };
}

public sealed record GetMyReviewResponse(
    Guid Id,
    Guid ProductId,
    string MarketCode,
    string State,
    int Rating,
    string Headline,
    string Body,
    string Locale,
    string MediaUrlsJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset StateChangedAtUtc,
    int EditCount,
    bool PendingReview,
    string? LastDecisionReason,
    uint RowVersion);
