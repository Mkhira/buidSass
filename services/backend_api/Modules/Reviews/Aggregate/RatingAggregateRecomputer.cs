using BackendApi.Modules.Reviews.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Aggregate;

/// <summary>
/// Recomputes the <c>product_rating_aggregates</c> row for one
/// (product, market) tuple from scratch over the source-of-truth review rows
/// in <c>visible</c> + <c>flagged</c> states. Called inline by every
/// state-transitioning handler whose change crosses an aggregate-affecting
/// boundary (data-model §3 / FR-025–FR-029) and by the daily reconciliation
/// worker as a safety net.
/// </summary>
/// <remarks>
/// Implementation is a single <c>INSERT ... ON CONFLICT DO UPDATE</c> against
/// <c>reviews.product_rating_aggregates</c>. Driving the math in SQL keeps the
/// hot-path read-side at a single PK lookup (FR-029, p95 ≤ 50 ms) without
/// requiring an in-memory recount.
/// </remarks>
public sealed class RatingAggregateRecomputer
{
    private readonly ReviewsDbContext _db;
    private readonly TimeProvider _time;

    public RatingAggregateRecomputer(ReviewsDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task RecomputeAsync(Guid productId, string marketCode, CancellationToken ct)
    {
        // Materialize stats from the source-of-truth reviews. Driver supports
        // parameterized raw SQL via FormattableString; index
        // IX_reviews_product_market_state covers the WHERE clause.
        var nowUtc = _time.GetUtcNow();
        const string sql = @"
INSERT INTO reviews.product_rating_aggregates (
    ""ProductId"", ""MarketCode"",
    ""AvgRating"", ""ReviewCount"",
    ""Distribution1"", ""Distribution2"", ""Distribution3"", ""Distribution4"", ""Distribution5"",
    ""LastUpdatedUtc""
)
SELECT
    {0}::uuid,
    {1}::text,
    CASE WHEN COUNT(*) = 0 THEN NULL ELSE ROUND(AVG(""Rating""::numeric), 2) END,
    COUNT(*)::int,
    COUNT(*) FILTER (WHERE ""Rating"" = 1)::int,
    COUNT(*) FILTER (WHERE ""Rating"" = 2)::int,
    COUNT(*) FILTER (WHERE ""Rating"" = 3)::int,
    COUNT(*) FILTER (WHERE ""Rating"" = 4)::int,
    COUNT(*) FILTER (WHERE ""Rating"" = 5)::int,
    {2}::timestamptz
FROM reviews.reviews
WHERE ""ProductId"" = {0}::uuid
  AND ""MarketCode"" = {1}::text
  AND ""State"" IN ('visible','flagged')
ON CONFLICT (""ProductId"", ""MarketCode"") DO UPDATE
SET ""AvgRating""       = EXCLUDED.""AvgRating"",
    ""ReviewCount""     = EXCLUDED.""ReviewCount"",
    ""Distribution1""   = EXCLUDED.""Distribution1"",
    ""Distribution2""   = EXCLUDED.""Distribution2"",
    ""Distribution3""   = EXCLUDED.""Distribution3"",
    ""Distribution4""   = EXCLUDED.""Distribution4"",
    ""Distribution5""   = EXCLUDED.""Distribution5"",
    ""LastUpdatedUtc""  = EXCLUDED.""LastUpdatedUtc"";";

        await _db.Database.ExecuteSqlRawAsync(
            sql,
            new object[] { productId, marketCode, nowUtc.UtcDateTime },
            ct);
    }
}
