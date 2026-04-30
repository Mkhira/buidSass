using BackendApi.Modules.Reviews.Persistence;
using BackendApi.Modules.Shared;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Reviews.Aggregate;

/// <summary>
/// Implementation of <see cref="IRatingAggregateReader"/>. Single-row PK lookup
/// (and bulk PK lookup for the batch path) over the denormalized
/// <c>reviews.product_rating_aggregates</c> table per data-model §2.5 / §10.
/// Returns <see langword="null"/> when no aggregate row exists; callers render
/// review_count=0 in that case.
/// </summary>
public sealed class RatingAggregateReader : IRatingAggregateReader
{
    private readonly ReviewsDbContext _db;

    public RatingAggregateReader(ReviewsDbContext db) => _db = db;

    public async Task<RatingAggregate?> GetAsync(Guid productId, string marketCode, CancellationToken ct)
    {
        var row = await _db.RatingAggregates.AsNoTracking()
            .FirstOrDefaultAsync(a => a.ProductId == productId && a.MarketCode == marketCode, ct);
        return row is null ? null : Project(row);
    }

    public async Task<IReadOnlyDictionary<Guid, RatingAggregate>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        string marketCode,
        CancellationToken ct)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, RatingAggregate>();
        }

        var rows = await _db.RatingAggregates.AsNoTracking()
            .Where(a => a.MarketCode == marketCode && productIds.Contains(a.ProductId))
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.ProductId, Project);
    }

    private static RatingAggregate Project(Entities.ProductRatingAggregate row) =>
        new(
            ProductId: row.ProductId,
            MarketCode: row.MarketCode,
            AvgRating: row.AvgRating,
            ReviewCount: row.ReviewCount,
            Dist1: row.Distribution1,
            Dist2: row.Distribution2,
            Dist3: row.Distribution3,
            Dist4: row.Distribution4,
            Dist5: row.Distribution5,
            LastUpdatedUtc: row.LastUpdatedUtc);
}
