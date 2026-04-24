namespace BackendApi.Modules.Inventory.Entities;

public sealed class InventoryBatch
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public int QtyOnHand { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset ReceivedAt { get; set; }
    public Guid? ReceivedByAccountId { get; set; }
    public string? Notes { get; set; }
}
