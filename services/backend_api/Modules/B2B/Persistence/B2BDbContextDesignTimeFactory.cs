using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.B2B.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations add</c> / <c>dotnet ef database update</c>
/// when the host's WebApplication isn't booted. The hard-coded local connection string
/// matches the project convention (mirrors every other module's DbContextDesignTimeFactory).
/// Production runtime uses the DI-resolved connection string from <c>B2BModule.AddB2BModule</c>.
/// </summary>
public sealed class B2BDbContextDesignTimeFactory : IDesignTimeDbContextFactory<B2BDbContext>
{
    public B2BDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<B2BDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=dental_commerce;Username=dental_api_app;Password=dental_api_app");
        return new B2BDbContext(optionsBuilder.Options);
    }
}
