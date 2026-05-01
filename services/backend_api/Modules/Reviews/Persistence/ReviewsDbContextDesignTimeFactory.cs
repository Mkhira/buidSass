using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BackendApi.Modules.Reviews.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations add</c>. Reads
/// <c>REVIEWS_DB_CONNECTION</c> or the shared <c>DEFAULT_DB_CONNECTION</c>.
/// </summary>
public sealed class ReviewsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ReviewsDbContext>
{
    public ReviewsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("REVIEWS_DB_CONNECTION")
            ?? Environment.GetEnvironmentVariable("DEFAULT_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Design-time EF operations require REVIEWS_DB_CONNECTION or DEFAULT_DB_CONNECTION to be set.");

        var optionsBuilder = new DbContextOptionsBuilder<ReviewsDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new ReviewsDbContext(optionsBuilder.Options);
    }
}
