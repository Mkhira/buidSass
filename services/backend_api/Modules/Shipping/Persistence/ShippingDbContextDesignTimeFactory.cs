using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Shipping.Persistence;

public sealed class ShippingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ShippingDbContext>
{
    public ShippingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SHIPPING_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require SHIPPING_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");

        var optionsBuilder = new DbContextOptionsBuilder<ShippingDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new ShippingDbContext(optionsBuilder.Options);
    }
}
