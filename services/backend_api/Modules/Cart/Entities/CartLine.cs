namespace BackendApi.Modules.Cart.Entities;

public sealed class CartLine
{
    public Guid Id { get; set; }
    public Guid CartId { get; set; }
    /// <summary>Denormalised from the owning cart for per-market partitioning (ADR-010). Populated from cart.market_code at insert time.</summary>
    public string MarketCode { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public int Qty { get; set; }
    public Guid? ReservationId { get; set; }
    public bool Unavailable { get; set; }
    public bool Restricted { get; set; }
    public string? RestrictionReasonCode { get; set; }
    public bool StockChanged { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public uint RowVersion { get; set; }
}
