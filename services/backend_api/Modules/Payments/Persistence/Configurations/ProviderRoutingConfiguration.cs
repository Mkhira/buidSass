using BackendApi.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Payments.Persistence.Configurations;

public sealed class ProviderRoutingConfiguration : IEntityTypeConfiguration<ProviderRouting>
{
    public void Configure(EntityTypeBuilder<ProviderRouting> builder)
    {
        builder.ToTable("provider_routing", "payments", t =>
        {
            t.HasCheckConstraint("CK_provider_routing_market",
                "\"MarketCode\" IN ('sa','eg')");
            t.HasCheckConstraint("CK_provider_routing_primary_not_backup",
                "\"BackupProviderId\" IS NULL OR \"PrimaryProviderId\" <> \"BackupProviderId\"");
            t.HasCheckConstraint("CK_provider_routing_threshold",
                "\"FailoverThresholdPct\" BETWEEN 1 AND 100");
            t.HasCheckConstraint("CK_provider_routing_window",
                "\"FailoverWindowMinutes\" > 0");
        });

        builder.HasKey(x => new { x.MarketCode, x.Method });
        builder.Property(x => x.MarketCode).HasColumnType("text");
        builder.Property(x => x.Method).HasColumnType("text");
        builder.Property(x => x.PrimaryProviderId).HasColumnType("text").IsRequired();
        builder.Property(x => x.BackupProviderId).HasColumnType("text");
        builder.Property(x => x.AutoFailoverEnabled).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.FailoverThresholdPct).IsRequired().HasDefaultValue(50);
        builder.Property(x => x.FailoverWindowMinutes).IsRequired().HasDefaultValue(5);
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
