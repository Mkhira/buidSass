namespace BackendApi.Modules.Inventory.Entities;

public sealed class Warehouse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string MarketCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string OwnerId { get; set; } = "platform";
    public Guid? VendorId { get; set; }
}
