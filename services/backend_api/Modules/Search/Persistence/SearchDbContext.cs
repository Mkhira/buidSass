using BackendApi.Modules.Search.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Search.Persistence;

public sealed class SearchDbContext(DbContextOptions<SearchDbContext> options) : DbContext(options)
{
    public DbSet<SearchIndexerCursor> SearchIndexerCursors => Set<SearchIndexerCursor>();
    public DbSet<ReindexJob> ReindexJobs => Set<ReindexJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("search");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(SearchDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.Search", StringComparison.Ordinal) == true);
    }
}
