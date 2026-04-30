using BackendApi.Modules.Shared;

namespace BackendApi.Modules.Reviews.Aggregate.ReadProductRating;

/// <summary>
/// Read handler for the public unauthenticated aggregate endpoints (contract §5).
/// Delegates to <see cref="IRatingAggregateReader"/> + projects to the wire shape.
/// When no row exists we synthesize a zero-count, null-avg response so the UI
/// can render "no reviews yet" without a 404 round-trip (FR-028).
/// </summary>
public sealed class ReadProductRatingHandler
{
    private readonly IRatingAggregateReader _reader;

    public ReadProductRatingHandler(IRatingAggregateReader reader) => _reader = reader;

    public async Task<ReadProductRatingResponse> GetAsync(
        Guid productId, string marketCode, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var aggregate = await _reader.GetAsync(productId, marketCode, ct);
        return aggregate is null
            ? Empty(productId, marketCode, nowUtc)
            : Project(aggregate);
    }

    public async Task<ReadProductRatingsResponse> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        string marketCode,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var rows = await _reader.GetManyAsync(productIds, marketCode, ct);
        var items = productIds
            .Select(id => rows.TryGetValue(id, out var found)
                ? Project(found)
                : Empty(id, marketCode, nowUtc))
            .ToList();
        return new ReadProductRatingsResponse(items);
    }

    private static ReadProductRatingResponse Project(RatingAggregate a) => new(
        ProductId: a.ProductId,
        MarketCode: a.MarketCode,
        AvgRating: a.AvgRating,
        ReviewCount: a.ReviewCount,
        Distribution: new RatingDistribution(a.Dist1, a.Dist2, a.Dist3, a.Dist4, a.Dist5),
        LastUpdatedUtc: a.LastUpdatedUtc);

    private static ReadProductRatingResponse Empty(Guid productId, string marketCode, DateTimeOffset nowUtc) => new(
        ProductId: productId,
        MarketCode: marketCode,
        AvgRating: null,
        ReviewCount: 0,
        Distribution: new RatingDistribution(0, 0, 0, 0, 0),
        LastUpdatedUtc: nowUtc);
}
