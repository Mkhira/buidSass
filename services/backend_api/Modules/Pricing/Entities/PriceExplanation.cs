namespace BackendApi.Modules.Pricing.Entities;

public sealed class PriceExplanation
{
    public Guid Id { get; set; }
    public string OwnerKind { get; set; } = string.Empty; // quote | order | preview
    public Guid OwnerId { get; set; }
    public Guid? AccountId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string ExplanationJson { get; set; } = "{}";
    public byte[] ExplanationHash { get; set; } = Array.Empty<byte>();
    public long GrandTotalMinor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
