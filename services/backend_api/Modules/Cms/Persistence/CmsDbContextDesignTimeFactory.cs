using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Cms.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations add</c>. Reads
/// <c>CMS_DB_CONNECTION</c> or the shared <c>DEFAULT_DB_CONNECTION</c>.
/// </summary>
public sealed class CmsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CmsDbContext>
{
    public CmsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CMS_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require CMS_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");

        var optionsBuilder = new DbContextOptionsBuilder<CmsDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new CmsDbContext(optionsBuilder.Options);
    }
}
