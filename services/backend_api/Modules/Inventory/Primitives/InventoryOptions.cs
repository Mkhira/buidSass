namespace BackendApi.Modules.Inventory.Primitives;

public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    public int ReservationTtlMinutes { get; set; } = 15;
    public int ReservationReleaseWorkerIntervalSeconds { get; set; } = 30;
    public int ExpiryWriteoffWorkerIntervalSeconds { get; set; } = 300;
    public int ExpiryWriteoffHourUtc { get; set; } = 1;
}
