using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Cart.Persistence;

public sealed class CartDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CartDbContext>
{
    public CartDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CartDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=dental_commerce;Username=dental_api_app;Password=dental_api_app");
        return new CartDbContext(optionsBuilder.Options);
    }
}
