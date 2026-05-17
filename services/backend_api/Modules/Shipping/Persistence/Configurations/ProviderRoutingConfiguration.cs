using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Shipping.Persistence.Configurations;

public sealed class ProviderRoutingConfiguration : IEntityTypeConfiguration<ProviderRouting>
{
    public void Configure(EntityTypeBuilder<ProviderRouting> builder)
    {
        builder.ToTable("provider_routing", "shipping", t =>
        {
            t.HasCheckConstraint("CK_provider_routing_market",
                "\"MarketCode\" IN ('SA','EG')");
            t.HasCheckConstraint("CK_provider_routing_primary_neq_backup",
                "\"BackupProviderId\" IS NULL OR \"BackupProviderId\" <> \"PrimaryProviderId\"");
            t.HasCheckConstraint("CK_provider_routing_threshold_pct",
                "\"FailoverThresholdPct\" BETWEEN 10 AND 90");
            t.HasCheckConstraint("CK_provider_routing_window_minutes",
                "\"FailoverWindowMinutes\" BETWEEN 1 AND 60");
            // Auto-failover requires a backup provider; without one, BR-11's
            // "no auto-cascade unless explicit" guarantee cannot hold.
            t.HasCheckConstraint("CK_provider_routing_auto_requires_backup",
                "NOT \"AutoFailoverEnabled\" OR \"BackupProviderId\" IS NOT NULL");
        });
        builder.HasKey(x => new { x.MarketCode, x.MethodId });
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.MethodId).IsRequired();
        builder.Property(x => x.PrimaryProviderId).HasColumnType("text").IsRequired();
        builder.Property(x => x.BackupProviderId).HasColumnType("text");
        builder.Property(x => x.AutoFailoverEnabled).IsRequired();
        builder.Property(x => x.FailoverThresholdPct).IsRequired();
        builder.Property(x => x.FailoverWindowMinutes).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
