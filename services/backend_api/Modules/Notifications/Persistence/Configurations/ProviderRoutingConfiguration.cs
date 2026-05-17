using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class ProviderRoutingConfiguration : IEntityTypeConfiguration<ProviderRouting>
{
    public void Configure(EntityTypeBuilder<ProviderRouting> builder)
    {
        builder.ToTable("provider_routing", "notifications", t =>
        {
            t.HasCheckConstraint("CK_provider_routing_market_code",
                @"""MarketCode"" IN ('sa','eg')");
            t.HasCheckConstraint("CK_provider_routing_channel",
                @"""Channel"" IN ('sms','email','push')");
            t.HasCheckConstraint("CK_provider_routing_distinct_providers",
                @"""BackupProviderId"" IS NULL OR ""PrimaryProviderId"" <> ""BackupProviderId""");
            t.HasCheckConstraint("CK_provider_routing_threshold_range",
                @"""FailoverThresholdPct"" BETWEEN 10 AND 90");
            t.HasCheckConstraint("CK_provider_routing_window_positive",
                @"""FailoverWindowMinutes"" > 0");
        });

        builder.HasKey(x => new { x.MarketCode, x.Channel });
        builder.Property(x => x.PrimaryProviderId).HasColumnType("text").IsRequired();
        builder.Property(x => x.BackupProviderId).HasColumnType("text");
        builder.Property(x => x.AutoFailoverEnabled).IsRequired();
        builder.Property(x => x.FailoverThresholdPct).IsRequired();
        builder.Property(x => x.FailoverWindowMinutes).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
