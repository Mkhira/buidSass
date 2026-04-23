using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Pricing.Persistence;

public sealed class PricingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PricingDbContext>
{
    public PricingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PricingDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=dental_commerce;Username=dental_api_app;Password=dental_api_app");
        return new PricingDbContext(optionsBuilder.Options);
    }
}
