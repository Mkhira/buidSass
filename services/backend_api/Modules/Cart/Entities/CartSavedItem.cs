namespace BackendApi.Modules.Cart.Entities;

public sealed class CartSavedItem
{
    public Guid CartId { get; set; }
    public Guid ProductId { get; set; }
    /// <summary>Qty preserved from the source cart line; used when the item moves back to active.</summary>
    public int Qty { get; set; } = 1;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
}
