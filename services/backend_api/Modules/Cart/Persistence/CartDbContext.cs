using BackendApi.Modules.Cart.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Cart.Persistence;

public sealed class CartDbContext(DbContextOptions<CartDbContext> options) : DbContext(options)
{
    public DbSet<Cart.Entities.Cart> Carts => Set<Cart.Entities.Cart>();
    public DbSet<CartLine> CartLines => Set<CartLine>();
    public DbSet<CartSavedItem> CartSavedItems => Set<CartSavedItem>();
    public DbSet<CartB2BMetadata> CartB2BMetadata => Set<CartB2BMetadata>();
    public DbSet<CartAbandonedEmission> CartAbandonedEmissions => Set<CartAbandonedEmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("cart");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CartDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.Cart", StringComparison.Ordinal) == true);
    }
}
