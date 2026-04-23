using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Catalog.Persistence;

public sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=dental_commerce;Username=dental_api_app;Password=dental_api_app");
        return new CatalogDbContext(optionsBuilder.Options);
    }
}
