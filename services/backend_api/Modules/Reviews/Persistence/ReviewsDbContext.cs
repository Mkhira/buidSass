using BackendApi.Modules.Reviews.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApi.Modules.Reviews.Persistence;

/// <summary>
/// EF Core DbContext for the <c>reviews</c> schema. Seven tables per spec 022
/// data-model §2. <c>ManyServiceProvidersCreatedWarning</c> is suppressed
/// (project-memory rule) so integration suites that spin up multiple
/// WebApplicationFactories do not break Identity tests.
/// </summary>
public sealed class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options)
    : DbContext(options)
{
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewModerationDecision> ModerationDecisions => Set<ReviewModerationDecision>();
    public DbSet<ReviewAdminNote> AdminNotes => Set<ReviewAdminNote>();
    public DbSet<ReviewFlag> Flags => Set<ReviewFlag>();
    public DbSet<ProductRatingAggregate> RatingAggregates => Set<ProductRatingAggregate>();
    public DbSet<ReviewsFilterWordlist> Wordlists => Set<ReviewsFilterWordlist>();
    public DbSet<ReviewsMarketSchema> MarketSchemas => Set<ReviewsMarketSchema>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reviews");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ReviewsDbContext).Assembly,
            type => type.Namespace?.StartsWith(
                "BackendApi.Modules.Reviews.Persistence.Configurations",
                StringComparison.Ordinal) == true);
    }
}
