using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace BackendApi.Modules.B2B.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations add</c> /
/// <c>dotnet ef database update</c> when the host's WebApplication isn't booted.
/// Reads the connection string from (in priority order):
/// <list type="number">
///   <item><c>B2B_CONNECTION_STRING</c> environment variable.</item>
///   <item><c>ConnectionStrings:DefaultConnection</c> from a layered configuration
///         chain (env vars + appsettings.json + user-secrets).</item>
/// </list>
/// Throws <see cref="InvalidOperationException"/> with remediation guidance when
/// neither source supplies a value. Production runtime resolves its connection string
/// from DI in <c>B2BModule.AddB2BModule</c>; this factory exists solely for tooling.
/// </summary>
public sealed class B2BDbContextDesignTimeFactory : IDesignTimeDbContextFactory<B2BDbContext>
{
    public B2BDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("B2B_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
                .AddUserSecrets<B2BDbContextDesignTimeFactory>(optional: true)
                .Build();

            connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "B2BDbContextDesignTimeFactory could not resolve a connection string. " +
                "Set B2B_CONNECTION_STRING in the environment or " +
                "ConnectionStrings:DefaultConnection in appsettings/user-secrets " +
                "before running `dotnet ef` against B2BDbContext.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<B2BDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new B2BDbContext(optionsBuilder.Options);
    }
}
