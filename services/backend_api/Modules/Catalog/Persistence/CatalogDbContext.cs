using BackendApi.Modules.Catalog.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Catalog.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryClosure> CategoryClosures => Set<CategoryClosure>();
    public DbSet<CategoryAttributeSchema> CategoryAttributeSchemas => Set<CategoryAttributeSchema>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Manufacturer> Manufacturers => Set<Manufacturer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();
    public DbSet<ProductDocument> ProductDocuments => Set<ProductDocument>();
    public DbSet<ProductStateTransition> ProductStateTransitions => Set<ProductStateTransition>();
    public DbSet<ScheduledPublish> ScheduledPublishes => Set<ScheduledPublish>();
    public DbSet<CatalogOutboxEntry> CatalogOutbox => Set<CatalogOutboxEntry>();
    public DbSet<BulkImportIdempotencyRecord> BulkImportIdempotency => Set<BulkImportIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");
        modelBuilder.HasPostgresExtension("citext");
        // Only apply configurations from the Catalog namespace so IdentityDbContext's shared
        // assembly types (and vice versa) don't bleed into this context's model.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CatalogDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.Catalog", StringComparison.Ordinal) == true);
    }
}
