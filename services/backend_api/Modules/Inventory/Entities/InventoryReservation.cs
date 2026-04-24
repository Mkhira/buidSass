namespace BackendApi.Modules.Inventory.Entities;

public sealed class InventoryReservation
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public int Qty { get; set; }
    public Guid? CartId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? PickedBatchId { get; set; }
    public Guid? AccountId { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
}
