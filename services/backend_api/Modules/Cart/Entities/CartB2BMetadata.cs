namespace BackendApi.Modules.Cart.Entities;

public sealed class CartB2BMetadata
{
    public Guid CartId { get; set; }
    /// <summary>Denormalised from the owning cart (ADR-010 partitioning).</summary>
    public string MarketCode { get; set; } = string.Empty;
    public string? PoNumber { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? RequestedDeliveryFrom { get; set; }
    public DateTimeOffset? RequestedDeliveryTo { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
