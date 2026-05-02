using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApi.Modules.Cms.Persistence;

/// <summary>
/// EF Core DbContext for the <c>cms</c> schema. 9 tables per spec 024
/// data-model §2. <c>ManyServiceProvidersCreatedWarning</c> is suppressed
/// (project-memory rule) so integration suites that spin up multiple
/// WebApplicationFactories do not break Identity tests.
/// </summary>
public sealed class CmsDbContext(DbContextOptions<CmsDbContext> options)
    : DbContext(options)
{
    public DbSet<BannerSlot> BannerSlots => Set<BannerSlot>();
    public DbSet<FeaturedSection> FeaturedSections => Set<FeaturedSection>();
    public DbSet<FaqEntry> FaqEntries => Set<FaqEntry>();
    public DbSet<BlogArticle> BlogArticles => Set<BlogArticle>();
    public DbSet<LegalPageVersion> LegalPageVersions => Set<LegalPageVersion>();
    public DbSet<CmsAsset> Assets => Set<CmsAsset>();
    public DbSet<CmsPreviewToken> PreviewTokens => Set<CmsPreviewToken>();
    public DbSet<BannerCampaignBinding> BannerCampaignBindings => Set<BannerCampaignBinding>();
    public DbSet<CmsMarketSchema> MarketSchemas => Set<CmsMarketSchema>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("cms");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CmsDbContext).Assembly,
            type => type.Namespace?.StartsWith(
                "BackendApi.Modules.Cms.Persistence.Configurations",
                StringComparison.Ordinal) == true);
    }
}
