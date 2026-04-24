namespace BackendApi.Modules.Inventory.Entities;

public sealed class StockLevel
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public int OnHand { get; set; }
    public int Reserved { get; set; }
    public int SafetyStock { get; set; }
    public int ReorderThreshold { get; set; }
    public string BucketCache { get; set; } = "out_of_stock";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
