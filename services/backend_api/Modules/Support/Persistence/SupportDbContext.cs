using BackendApi.Modules.Support.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApi.Modules.Support.Persistence;

/// <summary>
/// EF Core DbContext for the <c>support</c> schema (9 tables per spec 023).
/// <c>ManyServiceProvidersCreatedWarning</c> is suppressed (project-memory
/// rule) so multi-WebApplicationFactory integration suites do not break the
/// Identity tests.
/// </summary>
public sealed class SupportDbContext(DbContextOptions<SupportDbContext> options)
    : DbContext(options)
{
    public DbSet<SupportTicket> Tickets => Set<SupportTicket>();
    public DbSet<TicketMessage> Messages => Set<TicketMessage>();
    public DbSet<TicketAttachment> Attachments => Set<TicketAttachment>();
    public DbSet<TicketLink> Links => Set<TicketLink>();
    public DbSet<TicketAssignment> Assignments => Set<TicketAssignment>();
    public DbSet<TicketSlaBreachEvent> SlaBreachEvents => Set<TicketSlaBreachEvent>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<SupportMarketSchema> MarketSchemas => Set<SupportMarketSchema>();
    public DbSet<SupportAgentAvailability> AgentAvailability => Set<SupportAgentAvailability>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("support");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(SupportDbContext).Assembly,
            t => t.Namespace?.StartsWith(
                "BackendApi.Modules.Support.Persistence.Configurations",
                StringComparison.Ordinal) == true);
    }
}
