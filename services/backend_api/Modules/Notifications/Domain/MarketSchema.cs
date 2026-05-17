namespace BackendApi.Modules.Notifications.Domain;

/// <summary>
/// Per-market notification policy per
/// <c>data-model.md §notifications.market_schemas</c>. Holds quiet-hours
/// (marketing-only), unsubscribe footers (editorial AR + EN), and per-24h
/// rate-limit caps. Loaded once per market and cached by the scheduler.
/// </summary>
public sealed class MarketSchema
{
    public string MarketCode { get; set; } = string.Empty;

    public TimeOnly QuietHoursMarketingLocalStart { get; set; } = new(22, 0);
    public TimeOnly QuietHoursMarketingLocalEnd { get; set; } = new(8, 0);
    public string QuietHoursTimezone { get; set; } = string.Empty;

    public string UnsubscribeFooterAr { get; set; } = string.Empty;
    public string UnsubscribeFooterEn { get; set; } = string.Empty;

    public int RateLimitMarketingPer24h { get; set; } = 1;
    public int RateLimitTransactionalPer24h { get; set; } = 5;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
