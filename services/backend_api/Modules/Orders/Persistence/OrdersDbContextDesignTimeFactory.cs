using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Orders.Persistence;

/// <summary>
/// Design-time factory for `dotnet ef`. Reads <c>ORDERS_DB_CONNECTION</c> or the shared
/// <c>DEFAULT_DB_CONNECTION</c>; throws if neither is set.
/// </summary>
public sealed class OrdersDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ORDERS_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require ORDERS_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");

        var optionsBuilder = new DbContextOptionsBuilder<OrdersDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new OrdersDbContext(optionsBuilder.Options);
    }
}
