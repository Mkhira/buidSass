namespace BackendApi.Modules.Cart.Entities;

public sealed class CartAbandonedEmission
{
    public Guid CartId { get; set; }
    /// <summary>Denormalised from the owning cart (ADR-010 partitioning).</summary>
    public string MarketCode { get; set; } = string.Empty;
    public DateTimeOffset LastEmittedAt { get; set; }
}
