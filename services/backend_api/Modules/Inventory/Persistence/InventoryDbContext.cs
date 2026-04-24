using BackendApi.Modules.Inventory.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Inventory.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<InventoryBatch> InventoryBatches => Set<InventoryBatch>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<ReorderAlertDebounce> ReorderAlertDebounceEntries => Set<ReorderAlertDebounce>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(InventoryDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.Inventory", StringComparison.Ordinal) == true);
    }
}
