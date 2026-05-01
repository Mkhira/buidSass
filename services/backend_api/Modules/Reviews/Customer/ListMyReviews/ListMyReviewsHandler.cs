using BackendApi.Modules.Reviews.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Customer.ListMyReviews;

/// <summary>
/// GET /api/customer/reviews/me — returns the caller's own reviews regardless
/// of state, paged by created-at cursor.
/// </summary>
public sealed class ListMyReviewsHandler
{
    private readonly ReviewsDbContext _db;

    public ListMyReviewsHandler(ReviewsDbContext db) => _db = db;

    public async Task<ListMyReviewsResponse> HandleAsync(
        Guid customerId,
        string? stateFilter,
        DateTimeOffset? cursorBefore,
        int limit,
        CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);

        var query = _db.Reviews.AsNoTracking()
            .Where(r => r.CustomerId == customerId);

        if (!string.IsNullOrWhiteSpace(stateFilter)
            && Enum.TryParse<Primitives.ReviewState>(MapWire(stateFilter), ignoreCase: true, out var parsed))
        {
            query = query.Where(r => r.State == parsed);
        }

        if (cursorBefore is not null)
        {
            query = query.Where(r => r.CreatedAtUtc < cursorBefore.Value);
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit + 1)
            .Select(r => new ListMyReviewsItem(
                r.Id,
                r.ProductId,
                r.MarketCode,
                ToWire(r.State),
                r.Rating,
                r.Headline,
                r.CreatedAtUtc,
                r.State == Primitives.ReviewState.PendingModeration,
                r.StateChangedReasonNote))
            .ToListAsync(ct);

        DateTimeOffset? nextCursor = null;
        if (rows.Count > limit)
        {
            nextCursor = rows[limit - 1].CreatedAtUtc;
            rows = rows.Take(limit).ToList();
        }

        return new ListMyReviewsResponse(rows, nextCursor);
    }

    private static string MapWire(string s) => s.ToLowerInvariant() switch
    {
        "pending_moderation" => "PendingModeration",
        "visible" => "Visible",
        "flagged" => "Flagged",
        "hidden" => "Hidden",
        "deleted" => "Deleted",
        _ => s,
    };

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

public sealed record ListMyReviewsItem(
    Guid Id,
    Guid ProductId,
    string MarketCode,
    string State,
    int Rating,
    string Headline,
    DateTimeOffset CreatedAtUtc,
    bool PendingReview,
    string? LastDecisionReason);

public sealed record ListMyReviewsResponse(
    IReadOnlyList<ListMyReviewsItem> Items,
    DateTimeOffset? NextCursor);
