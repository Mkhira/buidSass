using BackendApi.Modules.Verification.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApi.Modules.Verification.Persistence;

/// <summary>
/// EF Core DbContext for the <c>verification</c> schema. Six tables per spec 020
/// data-model §2. All configurations are picked up from the
/// <c>Persistence/Configurations</c> folder via the <c>ApplyConfigurationsFromAssembly</c>
/// filter.
/// </summary>
public sealed class VerificationDbContext(DbContextOptions<VerificationDbContext> options)
    : DbContext(options)
{
    public DbSet<Verification.Entities.Verification> Verifications => Set<Verification.Entities.Verification>();
    public DbSet<VerificationDocument> Documents => Set<VerificationDocument>();
    public DbSet<VerificationStateTransition> StateTransitions => Set<VerificationStateTransition>();
    public DbSet<VerificationMarketSchema> MarketSchemas => Set<VerificationMarketSchema>();
    public DbSet<VerificationReminder> Reminders => Set<VerificationReminder>();
    public DbSet<VerificationEligibilityCache> EligibilityCache => Set<VerificationEligibilityCache>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // ManyServiceProvidersCreatedWarning is benign test-scaffold churn (project-memory
        // rule); suppress so integration suites that spin up many WebApplicationFactories
        // don't blow up. Mirrors Modules/Cart/CartModule.cs and the rest of the modules.
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("verification");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(VerificationDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.Verification.Persistence.Configurations", StringComparison.Ordinal) == true);
    }
}
