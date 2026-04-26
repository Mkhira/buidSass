using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Returns.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>. Reads <c>RETURNS_DB_CONNECTION</c> or the shared
/// <c>DEFAULT_DB_CONNECTION</c>; throws if neither is set.
/// </summary>
public sealed class ReturnsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ReturnsDbContext>
{
    public ReturnsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("RETURNS_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require RETURNS_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");

        var optionsBuilder = new DbContextOptionsBuilder<ReturnsDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new ReturnsDbContext(optionsBuilder.Options);
    }
}
