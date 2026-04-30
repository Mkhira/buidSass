using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Admin.ListReviewsByCustomer;

/// <summary>
/// GET /api/admin/reviews/by-customer/{customerId} per contract §3.6 — used by
/// support agents during dispute investigation. Returns reviews regardless of
/// state (the support agent may need to see deleted rows for audit context).
/// </summary>
public sealed class ListReviewsByCustomerHandler
{
    private readonly ReviewsDbContext _db;

    public ListReviewsByCustomerHandler(ReviewsDbContext db) => _db = db;

    public async Task<ListReviewsByCustomerResponse> HandleAsync(
        Guid customerId,
        string? stateFilter,
        DateTimeOffset? cursorBefore,
        int limit,
        CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);

        var query = _db.Reviews.AsNoTracking().Where(r => r.CustomerId == customerId);
        var stateEnum = ParseState(stateFilter);
        if (stateEnum is { } s)
        {
            query = query.Where(r => r.State == s);
        }
        if (cursorBefore is { } cursor)
        {
            query = query.Where(r => r.CreatedAtUtc < cursor);
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit + 1)
            .Select(r => new CustomerReviewItem(
                r.Id, r.ProductId, r.MarketCode, ToWire(r.State),
                r.Rating, r.Headline, r.CreatedAtUtc, r.StateChangedAtUtc,
                r.StateChangedReasonNote))
            .ToListAsync(ct);

        DateTimeOffset? next = null;
        if (rows.Count > limit)
        {
            next = rows[limit - 1].CreatedAtUtc;
            rows = rows.Take(limit).ToList();
        }
        return new ListReviewsByCustomerResponse(rows, next);
    }

    private static ReviewState? ParseState(string? raw) => raw?.ToLowerInvariant() switch
    {
        "pending_moderation" => ReviewState.PendingModeration,
        "visible" => ReviewState.Visible,
        "flagged" => ReviewState.Flagged,
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

public sealed record CustomerReviewItem(
    Guid Id, Guid ProductId, string MarketCode, string State,
    int Rating, string Headline,
    DateTimeOffset CreatedAtUtc, DateTimeOffset StateChangedAtUtc,
    string? LastDecisionReason);

public sealed record ListReviewsByCustomerResponse(
    IReadOnlyList<CustomerReviewItem> Items,
    DateTimeOffset? NextCursor);
