namespace BackendApi.Modules.Inventory.Entities;

public sealed class ReorderAlertDebounce
{
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public DateTimeOffset WindowStartHour { get; set; }
    public DateTimeOffset EmittedAt { get; set; }
}
