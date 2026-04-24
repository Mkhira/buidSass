using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Cart.Persistence;

/// <summary>
/// Design-time factory invoked by `dotnet ef` when generating migrations. Reads the connection
/// string from `CART_DB_CONNECTION` (or the shared `DEFAULT_DB_CONNECTION`) and fails fast when
/// neither is set — no hardcoded fallback, no risk of committing or silently pointing at the
/// wrong database.
/// </summary>
public sealed class CartDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CartDbContext>
{
    public CartDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CART_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require CART_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");

        var optionsBuilder = new DbContextOptionsBuilder<CartDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new CartDbContext(optionsBuilder.Options);
    }
}
