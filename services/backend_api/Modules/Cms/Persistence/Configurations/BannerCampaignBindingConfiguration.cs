using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Cms.Persistence.Configurations;

public sealed class BannerCampaignBindingConfiguration : IEntityTypeConfiguration<BannerCampaignBinding>
{
    public void Configure(EntityTypeBuilder<BannerCampaignBinding> builder)
    {
        builder.ToTable("banner_campaign_bindings", "cms", t =>
        {
            t.HasCheckConstraint("CK_cms_banner_binding_state",
                "\"BindingState\" IN ('active','released_due_to_campaign_deactivation','released_by_editor')");
            t.HasCheckConstraint("CK_cms_banner_binding_release_consistency",
                "(\"BindingState\" = 'active' AND \"ReleasedAtUtc\" IS NULL) OR (\"BindingState\" <> 'active' AND \"ReleasedAtUtc\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.BannerId).IsRequired();
        builder.Property(x => x.VersionId).IsRequired();
        builder.Property(x => x.CampaignId).IsRequired();
        builder.Property(x => x.BoundAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.ReleasedAtUtc);
        builder.Property(x => x.BindingStateWire).HasColumnName("BindingState").HasColumnType("text").IsRequired().HasDefaultValue("active");
        builder.Property(x => x.ReleaseActorId);
        builder.Property(x => x.ReleaseReasonNote).HasColumnType("text");
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");

        builder.HasIndex(x => new { x.BannerId, x.BindingStateWire })
            .HasDatabaseName("IX_cms_banner_binding_banner");
        builder.HasIndex(x => new { x.CampaignId, x.BindingStateWire })
            .HasDatabaseName("IX_cms_banner_binding_campaign");
    }
}
