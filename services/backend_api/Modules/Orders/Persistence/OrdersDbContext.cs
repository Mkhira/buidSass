using BackendApi.Modules.Orders.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Orders.Persistence;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentLine> ShipmentLines => Set<ShipmentLine>();
    public DbSet<OrderStateTransition> StateTransitions => Set<OrderStateTransition>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationLine> QuotationLines => Set<QuotationLine>();
    public DbSet<OrdersOutboxEntry> Outbox => Set<OrdersOutboxEntry>();
    public DbSet<CancellationPolicyRow> CancellationPolicies => Set<CancellationPolicyRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("orders");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(OrdersDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.Orders", StringComparison.Ordinal) == true);
    }
}
