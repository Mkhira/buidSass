using BackendApi.Modules.Cms.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Cms.Persistence.Configurations;

public sealed class CmsPreviewTokenConfiguration : IEntityTypeConfiguration<CmsPreviewToken>
{
    public void Configure(EntityTypeBuilder<CmsPreviewToken> builder)
    {
        builder.ToTable("preview_tokens", "cms", t =>
        {
            t.HasCheckConstraint("CK_cms_preview_entity_kind",
                "\"EntityKind\" IN ('banner_slot','featured_section','faq_entry','blog_article','legal_page_version')");
            t.HasCheckConstraint("CK_cms_preview_expires_after_mint",
                "\"ExpiresAtUtc\" > \"MintedAtUtc\"");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.TokenHash).HasColumnType("bytea").IsRequired();
        builder.Property(x => x.EntityKindWire).HasColumnName("EntityKind").HasColumnType("text").IsRequired();
        builder.Property(x => x.EntityId).IsRequired();
        builder.Property(x => x.VersionId).IsRequired();
        builder.Property(x => x.ActorRoleAtMint).HasColumnType("text").IsRequired();
        builder.Property(x => x.MintedByActorId).IsRequired();
        builder.Property(x => x.MintedAtUtc).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.ExpiresAtUtc).IsRequired();
        builder.Property(x => x.RevokedAtUtc);
        builder.Property(x => x.RevokedByActorId);

        builder.HasIndex(x => x.TokenHash)
            .HasDatabaseName("UX_cms_preview_token_hash")
            .IsUnique();
        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("IX_cms_preview_cleanup_scan");
    }
}
