namespace BackendApi.Modules.Inventory.Entities;

public sealed class InventoryMovement
{
    public long Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public Guid? BatchId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int Delta { get; set; }
    public string? Reason { get; set; }
    public string? SourceKind { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? ActorAccountId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
