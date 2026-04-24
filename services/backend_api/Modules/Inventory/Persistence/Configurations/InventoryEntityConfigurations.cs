using BackendApi.Modules.Inventory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Inventory.Persistence.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses", "inventory");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasColumnType("citext").IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.DisplayName).HasColumnType("text").IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.OwnerId).HasColumnType("citext").HasDefaultValue("platform").IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.MarketCode, x.IsActive });
    }
}

public sealed class StockLevelConfiguration : IEntityTypeConfiguration<StockLevel>
{
    public void Configure(EntityTypeBuilder<StockLevel> builder)
    {
        builder.ToTable("stock_levels", "inventory", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_stock_levels_on_hand_non_negative", "\"OnHand\" >= 0");
            tableBuilder.HasCheckConstraint("CK_stock_levels_reserved_non_negative", "\"Reserved\" >= 0");
            tableBuilder.HasCheckConstraint("CK_stock_levels_safety_stock_non_negative", "\"SafetyStock\" >= 0");
            tableBuilder.HasCheckConstraint("CK_stock_levels_reorder_threshold_non_negative", "\"ReorderThreshold\" >= 0");
        });
        builder.HasKey(x => new { x.ProductId, x.WarehouseId });
        builder.Property(x => x.BucketCache).HasColumnType("citext").HasDefaultValue("out_of_stock").IsRequired();
        builder.HasIndex(x => x.BucketCache);
    }
}

public sealed class InventoryBatchConfiguration : IEntityTypeConfiguration<InventoryBatch>
{
    public void Configure(EntityTypeBuilder<InventoryBatch> builder)
    {
        builder.ToTable("inventory_batches", "inventory", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_inventory_batches_qty_on_hand_non_negative", "\"QtyOnHand\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.LotNo).HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasColumnType("citext").HasDefaultValue("active").IsRequired();
        builder.Property(x => x.ExpiryDate).HasColumnType("date").IsRequired();
        builder.HasIndex(x => new { x.ProductId, x.WarehouseId, x.LotNo }).IsUnique();
        builder.HasIndex(x => new { x.ProductId, x.WarehouseId, x.ExpiryDate });
        builder.HasIndex(x => x.MarketCode);
    }
}

public sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("inventory_reservations", "inventory", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_inventory_reservations_qty_positive", "\"Qty\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Status).HasColumnType("citext").HasDefaultValue("active").IsRequired();
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });
        builder.HasIndex(x => x.MarketCode);
    }
}

public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("inventory_movements", "inventory");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Kind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.SourceKind).HasColumnType("citext");
        builder.HasIndex(x => new { x.ProductId, x.WarehouseId, x.OccurredAt });
        builder.HasIndex(x => x.MarketCode);
    }
}

public sealed class ReorderAlertDebounceConfiguration : IEntityTypeConfiguration<ReorderAlertDebounce>
{
    public void Configure(EntityTypeBuilder<ReorderAlertDebounce> builder)
    {
        builder.ToTable("reorder_alert_debounce", "inventory");
        builder.HasKey(x => new { x.WarehouseId, x.ProductId, x.WindowStartHour });
        builder.HasIndex(x => x.EmittedAt);
    }
}
