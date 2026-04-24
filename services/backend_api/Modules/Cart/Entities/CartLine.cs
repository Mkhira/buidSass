namespace BackendApi.Modules.Cart.Entities;

public sealed class CartLine
{
    public Guid Id { get; set; }
    public Guid CartId { get; set; }
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
