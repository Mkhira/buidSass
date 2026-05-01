namespace BackendApi.Modules.Reviews.Aggregate.ReadProductRating;

/// <summary>
/// Single-product aggregate response per contract §5.1. <see cref="AvgRating"/>
/// is <see langword="null"/> when <see cref="ReviewCount"/> is zero (FR-028).
/// </summary>
public sealed record ReadProductRatingResponse(
    Guid ProductId,
    string MarketCode,
    decimal? AvgRating,
    int ReviewCount,
    RatingDistribution Distribution,
    DateTimeOffset LastUpdatedUtc);

/// <summary>5-bucket histogram keyed by integer rating 1..5.</summary>
public sealed record RatingDistribution(int Bucket1, int Bucket2, int Bucket3, int Bucket4, int Bucket5);

/// <summary>Batch response per contract §5.2 — one item per known aggregate row.</summary>
public sealed record ReadProductRatingsResponse(IReadOnlyList<ReadProductRatingResponse> Items);
