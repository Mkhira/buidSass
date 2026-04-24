using BackendApi.Features.Seeding;
using BackendApi.Modules.Inventory.Entities;
using BackendApi.Modules.Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackendApi.Modules.Inventory.Seeding;

public sealed class InventoryBootstrapSeeder : ISeeder
{
    public string Name => "inventory.bootstrap";
    public int Version => 1;
    public IReadOnlyList<string> DependsOn => [];

    public async Task ApplyAsync(SeedContext ctx, CancellationToken ct)
    {
        var db = ctx.Services.GetRequiredService<InventoryDbContext>();

        await UpsertWarehouseAsync(db, "eg-main", "eg", "Egypt Main Warehouse", ct);
        await UpsertWarehouseAsync(db, "ksa-main", "ksa", "KSA Main Warehouse", ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertWarehouseAsync(
        InventoryDbContext db,
        string code,
        string marketCode,
        string displayName,
        CancellationToken ct)
    {
        var exists = await db.Warehouses.AnyAsync(w => w.Code == code, ct);
        if (exists)
        {
            return;
        }

        db.Warehouses.Add(new Warehouse
        {
            Id = Guid.NewGuid(),
            Code = code,
            MarketCode = marketCode,
            DisplayName = displayName,
            IsActive = true,
            OwnerId = "platform",
        });
    }
}
