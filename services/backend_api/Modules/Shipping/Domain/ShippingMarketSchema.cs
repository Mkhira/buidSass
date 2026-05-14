namespace BackendApi.Modules.Shipping.Domain;

/// <summary>
/// Per-market shipping policy per <c>data-model.md</c> §market_schemas.
/// Single row per market (PK = <c>market_code</c>). Holds market-specific
/// shipping defaults so business logic remains config-driven (Principle 5).
/// </summary>
public sealed class ShippingMarketSchema
{
    public string MarketCode { get; set; } = string.Empty;
    public string? PostalCodeRegex { get; set; }
    public string DefaultCurrency { get; set; } = string.Empty;
    public int DefaultEtaDaysMin { get; set; }
    public int DefaultEtaDaysMax { get; set; }
    public int SlaBreachThresholdHours { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
