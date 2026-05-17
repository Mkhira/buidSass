using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApi.Modules.Payments.Persistence;

/// <summary>
/// EF Core DbContext for the <c>payments</c> schema (12 tables per spec 027).
/// <c>ManyServiceProvidersCreatedWarning</c> is suppressed (project-memory
/// rule) so multi-WebApplicationFactory integration suites do not break the
/// Identity tests.
/// </summary>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
    : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<WebhookReceived> WebhooksReceived => Set<WebhookReceived>();
    public DbSet<ProviderRouting> ProviderRoutings => Set<ProviderRouting>();
    public DbSet<PaymentMethodMarketConfig> PaymentMethodsMarketConfigs => Set<PaymentMethodMarketConfig>();
    public DbSet<ReconciliationRun> ReconciliationRuns => Set<ReconciliationRun>();
    public DbSet<ReconciliationException> ReconciliationExceptions => Set<ReconciliationException>();
    public DbSet<Chargeback> Chargebacks => Set<Chargeback>();
    public DbSet<PciScopeEvent> PciScopeEvents => Set<PciScopeEvent>();
    public DbSet<BankTransferReference> BankTransferReferences => Set<BankTransferReference>();
    public DbSet<CodCollectionLog> CodCollectionLogs => Set<CodCollectionLog>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(PaymentsDbContext).Assembly,
            t => t.Namespace?.StartsWith(
                "BackendApi.Modules.Payments.Persistence.Configurations",
                StringComparison.Ordinal) == true);
    }
}
