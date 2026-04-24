using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Cart.Persistence;

/// <summary>
/// Design-time factory invoked by `dotnet ef` when generating migrations. Reads the connection
/// string from the `CART_DB_CONNECTION` environment variable so developer credentials are not
/// committed to source. Falls back to a well-known local-dev string only when the env var is
/// explicitly unset — CI and production images never hit the fallback because they inject the
/// env var at runtime.
/// </summary>
public sealed class CartDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CartDbContext>
{
    private const string LocalDevFallback =
        "Host=localhost;Port=5432;Database=dental_commerce;Username=dental_api_app;Password=dental_api_app";

    public CartDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CART_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? LocalDevFallback;

        var optionsBuilder = new DbContextOptionsBuilder<CartDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new CartDbContext(optionsBuilder.Options);
    }
}
