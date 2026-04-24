using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Checkout.Persistence;

/// <summary>
/// Design-time factory for `dotnet ef`. Reads `CHECKOUT_DB_CONNECTION` or the shared
/// `DEFAULT_DB_CONNECTION`; throws if neither is set so design-time operations never
/// silently target the wrong database.
/// </summary>
public sealed class CheckoutDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CheckoutDbContext>
{
    public CheckoutDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CHECKOUT_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require CHECKOUT_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");

        var optionsBuilder = new DbContextOptionsBuilder<CheckoutDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new CheckoutDbContext(optionsBuilder.Options);
    }
}
