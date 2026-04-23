namespace BackendApi.Modules.Pricing.Entities;

public sealed class TaxRate
{
    public Guid Id { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string Kind { get; set; } = "vat";
    public int RateBps { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public Guid? CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
