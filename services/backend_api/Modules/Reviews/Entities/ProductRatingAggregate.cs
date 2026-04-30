namespace BackendApi.Modules.Reviews.Entities;

/// <summary>
/// Denormalized read-side per <c>(product_id, market_code)</c> per data-model §2.5.
/// Recomputed inline on every countable transition; reconciled by the daily
/// <c>RatingAggregateRebuildWorker</c> safety net.
/// </summary>
public sealed class ProductRatingAggregate
{
    public Guid ProductId { get; set; }
    public string MarketCode { get; set; } = string.Empty;

    /// <summary>NULL when <see cref="ReviewCount"/> is 0 (FR-028).</summary>
    public decimal? AvgRating { get; set; }

    public int ReviewCount { get; set; }
    public int Distribution1 { get; set; }
    public int Distribution2 { get; set; }
    public int Distribution3 { get; set; }
    public int Distribution4 { get; set; }
    public int Distribution5 { get; set; }

    public DateTimeOffset LastUpdatedUtc { get; set; }
    public Guid? VendorId { get; set; }
}
