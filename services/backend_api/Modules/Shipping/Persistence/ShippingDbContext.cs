using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApi.Modules.Shipping.Persistence;

/// <summary>
/// EF Core DbContext for the <c>shipping</c> schema (11 tables per spec 026).
/// <c>ManyServiceProvidersCreatedWarning</c> is suppressed (project-memory
/// rule) so multi-WebApplicationFactory integration suites do not break the
/// Identity tests.
/// </summary>
public sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options)
    : DbContext(options)
{
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();
    public DbSet<ShippingMethodVersion> ShippingMethodVersions => Set<ShippingMethodVersion>();
    public DbSet<ShippingZone> ShippingZones => Set<ShippingZone>();
    public DbSet<FeeTable> FeeTables => Set<FeeTable>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();
    public DbSet<WebhookReceived> WebhooksReceived => Set<WebhookReceived>();
    public DbSet<ProviderRouting> ProviderRoutings => Set<ProviderRouting>();
    public DbSet<DeadLetterLabel> DeadLetterLabels => Set<DeadLetterLabel>();
    public DbSet<ShippingMarketSchema> MarketSchemas => Set<ShippingMarketSchema>();
    public DbSet<ShipmentDispute> ShipmentDisputes => Set<ShipmentDispute>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("shipping");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ShippingDbContext).Assembly,
            t => t.Namespace?.StartsWith(
                "BackendApi.Modules.Shipping.Persistence.Configurations",
                StringComparison.Ordinal) == true);
    }
}
