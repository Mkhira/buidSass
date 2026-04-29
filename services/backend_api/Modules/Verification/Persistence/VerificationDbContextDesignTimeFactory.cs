using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Verification.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>. Reads <c>VERIFICATION_DB_CONNECTION</c>
/// or the shared <c>DEFAULT_DB_CONNECTION</c>; throws if neither is set.
/// </summary>
public sealed class VerificationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<VerificationDbContext>
{
    public VerificationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("VERIFICATION_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require VERIFICATION_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");

        var optionsBuilder = new DbContextOptionsBuilder<VerificationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new VerificationDbContext(optionsBuilder.Options);
    }
}
