namespace BackendApi.Modules.Cart.Entities;

public sealed class CartSavedItem
{
    public Guid CartId { get; set; }
    /// <summary>Denormalised from the owning cart (ADR-010 partitioning).</summary>
    public string MarketCode { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    /// <summary>Qty preserved from the source cart line; used when the item moves back to active.</summary>
    public int Qty { get; set; } = 1;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
}
