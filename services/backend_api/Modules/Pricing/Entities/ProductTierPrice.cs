namespace BackendApi.Modules.Pricing.Entities;

public sealed class ProductTierPrice
{
    public Guid ProductId { get; set; }
    public Guid TierId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public long NetMinor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
