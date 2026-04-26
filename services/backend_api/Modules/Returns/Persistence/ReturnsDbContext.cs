using BackendApi.Modules.Returns.Entities;
using Microsoft.EntityFrameworkCore;

namespace BackendApi.Modules.Returns.Persistence;

public sealed class ReturnsDbContext(DbContextOptions<ReturnsDbContext> options) : DbContext(options)
{
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<ReturnLine> ReturnLines => Set<ReturnLine>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<InspectionLine> InspectionLines => Set<InspectionLine>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<RefundLine> RefundLines => Set<RefundLine>();
    public DbSet<ReturnPhoto> ReturnPhotos => Set<ReturnPhoto>();
    public DbSet<ReturnPolicy> ReturnPolicies => Set<ReturnPolicy>();
    public DbSet<ReturnsOutboxEntry> Outbox => Set<ReturnsOutboxEntry>();
    public DbSet<ReturnStateTransition> StateTransitions => Set<ReturnStateTransition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("returns");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ReturnsDbContext).Assembly,
            type => type.Namespace?.StartsWith("BackendApi.Modules.Returns", StringComparison.Ordinal) == true);
    }
}
