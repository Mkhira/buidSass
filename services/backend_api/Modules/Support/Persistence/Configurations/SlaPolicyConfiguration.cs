using BackendApi.Modules.Support.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Support.Persistence.Configurations;

public sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.ToTable("sla_policies", "support", t =>
        {
            t.HasCheckConstraint("CK_sla_policies_market_code", "\"MarketCode\" IN ('SA','EG')");
            t.HasCheckConstraint("CK_sla_policies_priority",
                "\"Priority\" IN ('low','normal','high','urgent')");
            t.HasCheckConstraint("CK_sla_policies_targets_positive",
                "\"FirstResponseTargetMinutes\" > 0 AND \"ResolutionTargetMinutes\" > 0");
            t.HasCheckConstraint("CK_sla_policies_resolution_gt_first",
                "\"ResolutionTargetMinutes\" > \"FirstResponseTargetMinutes\"");
        });

        builder.HasKey(x => new { x.MarketCode, x.Priority });
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.Priority).HasColumnType("text").IsRequired();
        builder.Property(x => x.FirstResponseTargetMinutes).IsRequired();
        builder.Property(x => x.ResolutionTargetMinutes).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedByActorId);
    }
}
