using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Reviews.Primitives;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Admin.ListModerationQueue;

/// <summary>
/// GET /api/admin/reviews/queue per contract §3.1. Returns reviews that need
/// moderator attention (pending_moderation OR flagged) ordered by
/// <c>pending_moderation_started_at</c> ASC (oldest first → SLA priority).
///
/// Filters honored: <c>state</c> (single state or all_attention), <c>market_code</c>,
/// <c>triggered_by</c>, <c>community_report_count_min</c>, <c>media_only</c>.
/// Cursor is the created-at + id of the last item to support stable pagination.
/// </summary>
public sealed class ListModerationQueueHandler
{
    private readonly ReviewsDbContext _db;

    public ListModerationQueueHandler(ReviewsDbContext db) => _db = db;

    public async Task<ListModerationQueueResponse> HandleAsync(
        ListModerationQueueQuery query,
        CancellationToken ct)
    {
        var limit = Math.Clamp(query.Limit ?? 50, 1, 200);
        var queryable = _db.Reviews.AsNoTracking();

        // State filter
        var stateFilter = query.State?.ToLowerInvariant();
        queryable = stateFilter switch
        {
            "pending_moderation" => queryable.Where(r => r.State == ReviewState.PendingModeration),
            "flagged" => queryable.Where(r => r.State == ReviewState.Flagged),
            null or "" or "all_attention" =>
                queryable.Where(r => r.State == ReviewState.PendingModeration || r.State == ReviewState.Flagged),
            _ => queryable.Where(r => false), // unknown → empty result
        };

        if (!string.IsNullOrWhiteSpace(query.MarketCode))
        {
            queryable = queryable.Where(r => r.MarketCode == query.MarketCode);
        }
        if (!string.IsNullOrWhiteSpace(query.TriggeredBy))
        {
            queryable = queryable.Where(r => r.TriggeredBy == query.TriggeredBy);
        }
        if (query.MediaOnly == true)
        {
            queryable = queryable.Where(r => r.MediaAttachmentReviewRequired);
        }
        if (query.CursorAfter is { } cursor)
        {
            queryable = queryable.Where(r => r.PendingModerationStartedAt > cursor);
        }

        // community_report_count_min must be applied at the SQL layer BEFORE
        // Take(limit + 1); otherwise a 50-row page can shrink to a few items
        // and pagination underfills. Apply on `queryable` (pre-Order) via a
        // correlated subquery against the flags table.
        if (query.CommunityReportCountMin is int minReports && minReports > 0)
        {
            queryable = queryable.Where(r =>
                _db.Flags.Count(f => f.ReviewId == r.Id) >= minReports);
        }

        // Order: oldest pending first; flagged-only rows fall through with their
        // pending_moderation_started_at == null (placed last).
        var ordered = queryable
            .OrderBy(r => r.PendingModerationStartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(r => r.CreatedAtUtc);

        var rows = await ordered
            .Take(limit + 1)
            .Select(r => new ModerationQueueItem(
                r.Id,
                r.CustomerId,
                r.ProductId,
                r.MarketCode,
                ToWire(r.State),
                r.Rating,
                r.Headline,
                Truncate(r.Body, 200),
                r.Locale,
                r.MediaUrlsJson,
                r.MediaAttachmentReviewRequired,
                r.FilterTripTerms,
                r.PendingModerationStartedAt,
                r.CreatedAtUtc,
                r.TriggeredBy,
                r.EditCount,
                r.Xmin))
            .ToListAsync(ct);

        DateTimeOffset? next = null;
        if (rows.Count > limit)
        {
            next = rows[limit - 1].PendingModerationStartedAt;
            rows = rows.Take(limit).ToList();
        }

        return new ListModerationQueueResponse(rows, next);
    }

    private static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= max ? s : s[..max] + "…";
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

public sealed record ListModerationQueueQuery(
    string? State,
    string? MarketCode,
    string? TriggeredBy,
    int? CommunityReportCountMin,
    bool? MediaOnly,
    DateTimeOffset? CursorAfter,
    int? Limit);

public sealed record ModerationQueueItem(
    Guid Id,
    Guid CustomerId,
    Guid ProductId,
    string MarketCode,
    string State,
    int Rating,
    string Headline,
    string? BodyExcerpt,
    string Locale,
    string MediaUrlsJson,
    bool MediaAttachmentReviewRequired,
    IReadOnlyList<string> FilterTripTerms,
    DateTimeOffset? PendingModerationStartedAt,
    DateTimeOffset CreatedAtUtc,
    string TriggeredBy,
    int EditCount,
    uint RowVersion);

public sealed record ListModerationQueueResponse(
    IReadOnlyList<ModerationQueueItem> Items,
    DateTimeOffset? NextCursor);
