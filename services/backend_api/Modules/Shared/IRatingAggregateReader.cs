namespace BackendApi.Modules.Shared;

/// <summary>
/// Read-only access to the per-(product, market) rating aggregate maintained by
/// spec 022. Spec 005 (product detail) and spec 006 (search-result decoration)
/// consume; spec 022 implements.
/// </summary>
public interface IRatingAggregateReader
{
    Task<RatingAggregate?> GetAsync(Guid productId, string marketCode, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, RatingAggregate>> GetManyAsync(
        IReadOnlyCollection<Guid> productIds,
        string marketCode,
        CancellationToken ct);
}

/// <summary>
/// Denormalized rating aggregate snapshot per data-model §2.5. <see cref="AvgRating"/>
/// is <see langword="null"/> when <see cref="ReviewCount"/> is zero (FR-028).
/// </summary>
public sealed record RatingAggregate(
    Guid ProductId,
    string MarketCode,
    decimal? AvgRating,
    int ReviewCount,
    int Dist1,
    int Dist2,
    int Dist3,
    int Dist4,
    int Dist5,
    DateTimeOffset LastUpdatedUtc);
