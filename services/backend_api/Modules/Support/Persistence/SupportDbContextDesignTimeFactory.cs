using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Support.Persistence;

public sealed class SupportDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SupportDbContext>
{
    public SupportDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPPORT_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require SUPPORT_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");

        var optionsBuilder = new DbContextOptionsBuilder<SupportDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new SupportDbContext(optionsBuilder.Options);
    }
}
