namespace BackendApi.Modules.Checkout.Entities;

public sealed class ShippingQuote
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string MethodCode { get; set; } = string.Empty;
    public int EtaMinDays { get; set; }
    public int EtaMaxDays { get; set; }
    public long FeeMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    /// <summary>Quote validity window — 10 min per spec R8; set at fetch time.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
