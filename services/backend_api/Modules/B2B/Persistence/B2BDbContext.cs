using BackendApi.Modules.B2B.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.B2B.Persistence;

/// <summary>
/// Spec 021 module DbContext. Schema: <c>b2b</c>. 10 tables (data-model §2):
/// companies + memberships + branches + invitations + quotes + quote_versions +
/// quote_version_documents + quote_state_transitions (append-only) +
/// quote_market_schemas + repeat_order_templates.
///
/// All <see cref="IEntityTypeConfiguration{TEntity}"/> classes under
/// <c>BackendApi.Modules.B2B.Persistence.Configurations</c> are auto-applied via
/// <see cref="OnModelCreating"/>.
///
/// EF Core warning suppression for <c>ManyServiceProvidersCreatedWarning</c> is wired
/// in <see cref="B2BModule"/> (project-memory rule R14, not this DbContext).
/// </summary>
public sealed class B2BDbContext(DbContextOptions<B2BDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();
    public DbSet<CompanyBranch> CompanyBranches => Set<CompanyBranch>();
    public DbSet<CompanyInvitation> CompanyInvitations => Set<CompanyInvitation>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteVersion> QuoteVersions => Set<QuoteVersion>();
    public DbSet<QuoteVersionDocument> QuoteVersionDocuments => Set<QuoteVersionDocument>();
    public DbSet<QuoteStateTransition> QuoteStateTransitions => Set<QuoteStateTransition>();
    public DbSet<QuoteMarketSchema> QuoteMarketSchemas => Set<QuoteMarketSchema>();
    public DbSet<RepeatOrderTemplate> RepeatOrderTemplates => Set<RepeatOrderTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("b2b");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(B2BDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.B2B.Persistence", StringComparison.Ordinal) == true);
    }
}
