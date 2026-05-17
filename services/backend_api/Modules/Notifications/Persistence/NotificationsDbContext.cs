using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApi.Modules.Notifications.Persistence;

/// <summary>
/// EF Core DbContext for the <c>notifications</c> schema (12 tables + 1
/// archive table per spec 025). <c>ManyServiceProvidersCreatedWarning</c> is
/// suppressed per the project-memory rule so multi-WebApplicationFactory
/// integration suites do not break the Identity tests.
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
    : DbContext(options)
{
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<WebhookReceived> WebhooksReceived => Set<WebhookReceived>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
    public DbSet<Preference> Preferences => Set<Preference>();
    public DbSet<UnsubscribeToken> UnsubscribeTokens => Set<UnsubscribeToken>();
    public DbSet<ProviderRouting> ProviderRoutings => Set<ProviderRouting>();
    public DbSet<DeadLetterEntry> DeadLetterEntries => Set<DeadLetterEntry>();
    public DbSet<DeadLetterArchive> DeadLetterArchive => Set<DeadLetterArchive>();
    public DbSet<MarketSchema> MarketSchemas => Set<MarketSchema>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(NotificationsDbContext).Assembly,
            t => t.Namespace?.StartsWith(
                "BackendApi.Modules.Notifications.Persistence.Configurations",
                StringComparison.Ordinal) == true);
    }
}
