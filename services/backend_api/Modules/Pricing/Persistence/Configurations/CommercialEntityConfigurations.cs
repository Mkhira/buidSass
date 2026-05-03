using BackendApi.Modules.Pricing.Entities;
using BackendApi.Modules.Pricing.Primitives.Commercial;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Pricing.Persistence.Configurations;

public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("campaigns", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NameAr).HasColumnType("text").IsRequired().HasMaxLength(300);
        builder.Property(x => x.NameEn).HasColumnType("text").IsRequired().HasMaxLength(300);
        builder.Property(x => x.ValidFrom).IsRequired();
        builder.Property(x => x.ValidTo).IsRequired();
        builder.Property(x => x.Markets).HasColumnType("citext[]").IsRequired();
        builder.Property(x => x.LandingQuery).HasColumnType("text").HasMaxLength(1024);
        builder.Property(x => x.NotesInternal).HasColumnType("text").HasMaxLength(4000);
        builder.Property(x => x.State)
            .HasColumnName("State")
            .HasConversion<int>()
            .HasDefaultValue(LifecycleState.Draft)
            .IsRequired();
        builder.Property(x => x.StateChangedAtUtc).IsRequired();
        builder.Property(x => x.StateChangedByActorId).IsRequired();
        builder.Property(x => x.StateChangedReasonNote).HasColumnType("text");
        builder.Property(x => x.LinkBroken).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.VendorId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.XminRowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.HasIndex(x => x.State)
            .HasDatabaseName("IX_campaigns_state_active_or_scheduled")
            .HasFilter("\"State\" IN (1, 2)"); // Scheduled, Active
        builder.HasIndex(x => x.ValidFrom).HasDatabaseName("IX_campaigns_valid_from");
        builder.HasIndex(x => x.VendorId).HasDatabaseName("IX_campaigns_vendor_id");
        // GIN index on markets[] is created in raw SQL inside the migration.

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_campaign_valid_window", "\"ValidTo\" > \"ValidFrom\"");
            t.HasCheckConstraint("chk_campaign_markets_non_empty", "array_length(\"Markets\", 1) >= 1");
        });
    }
}

public sealed class CampaignLinkConfiguration : IEntityTypeConfiguration<CampaignLink>
{
    public void Configure(EntityTypeBuilder<CampaignLink> builder)
    {
        builder.ToTable("campaign_links", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CampaignId).IsRequired();
        builder.Property(x => x.Kind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.TargetId);
        builder.Property(x => x.LinkBrokenAtUtc);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne<Campaign>()
            .WithMany()
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.CampaignId)
            .IsUnique()
            .HasFilter("\"LinkBrokenAtUtc\" IS NULL")
            .HasDatabaseName("UX_campaign_links_active_per_campaign");
        builder.HasIndex(x => new { x.Kind, x.TargetId })
            .HasDatabaseName("IX_campaign_links_kind_target");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_campaign_link_kind",
                "\"Kind\"::text IN ('promotion','coupon','landing_only')");
            t.HasCheckConstraint("chk_campaign_link_target_required",
                "(\"Kind\"::text = 'landing_only' AND \"TargetId\" IS NULL) OR (\"Kind\"::text <> 'landing_only' AND \"TargetId\" IS NOT NULL)");
        });
    }
}

public sealed class PreviewProfileConfiguration : IEntityTypeConfiguration<PreviewProfile>
{
    public void Configure(EntityTypeBuilder<PreviewProfile> builder)
    {
        builder.ToTable("preview_profiles", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasColumnType("text").IsRequired().HasMaxLength(200);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Locale).HasColumnType("citext").IsRequired();
        builder.Property(x => x.AccountKind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.TierId);
        builder.Property(x => x.VerificationState).HasColumnType("citext").IsRequired();
        builder.Property(x => x.CartLinesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Visibility)
            .HasColumnType("citext")
            .HasDefaultValue("personal")
            .IsRequired();
        builder.Property(x => x.CreatedBy).IsRequired();
        builder.Property(x => x.VendorId);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.XminRowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.HasIndex(x => x.CreatedBy)
            .HasDatabaseName("IX_preview_profiles_personal_by_creator")
            .HasFilter("\"Visibility\"::text = 'personal'");
        builder.HasIndex(x => x.Visibility)
            .HasDatabaseName("IX_preview_profiles_shared")
            .HasFilter("\"Visibility\"::text = 'shared'");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_preview_profile_market", "\"MarketCode\"::text IN ('SA','EG')");
            t.HasCheckConstraint("chk_preview_profile_locale", "\"Locale\"::text IN ('ar','en')");
            t.HasCheckConstraint("chk_preview_profile_account_kind", "\"AccountKind\"::text IN ('consumer','b2b')");
            t.HasCheckConstraint("chk_preview_profile_visibility", "\"Visibility\"::text IN ('personal','shared')");
            t.HasCheckConstraint("chk_preview_profile_verification_state",
                "\"VerificationState\"::text IN ('none','submitted','approved','rejected','expired')");
        });
    }
}

public sealed class CommercialThresholdConfiguration : IEntityTypeConfiguration<CommercialThreshold>
{
    public void Configure(EntityTypeBuilder<CommercialThreshold> builder)
    {
        builder.ToTable("commercial_thresholds", "pricing");
        builder.HasKey(x => x.MarketCode);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.GateEnabled).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.ThresholdPercentOff).HasColumnType("numeric(5,2)");
        builder.Property(x => x.ThresholdAmountOffMinor);
        builder.Property(x => x.ThresholdDurationDays);
        builder.Property(x => x.CouponInFlightGraceSeconds).HasDefaultValue(1800).IsRequired();
        builder.Property(x => x.PromotionInFlightGraceSeconds).HasDefaultValue(1800).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedByActorId).IsRequired();
        builder.Property(x => x.XminRowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_thr_market", "\"MarketCode\"::text IN ('SA','EG')");
            t.HasCheckConstraint("chk_thr_pct_off",
                "\"ThresholdPercentOff\" IS NULL OR (\"ThresholdPercentOff\" >= 0 AND \"ThresholdPercentOff\" <= 100)");
            t.HasCheckConstraint("chk_thr_amount_off",
                "\"ThresholdAmountOffMinor\" IS NULL OR \"ThresholdAmountOffMinor\" >= 0");
            t.HasCheckConstraint("chk_thr_duration",
                "\"ThresholdDurationDays\" IS NULL OR \"ThresholdDurationDays\" >= 0");
            t.HasCheckConstraint("chk_thr_coupon_grace",
                "\"CouponInFlightGraceSeconds\" >= 300 AND \"CouponInFlightGraceSeconds\" <= 7200");
            t.HasCheckConstraint("chk_thr_promo_grace",
                "\"PromotionInFlightGraceSeconds\" >= 300 AND \"PromotionInFlightGraceSeconds\" <= 7200");
        });
    }
}

public sealed class CommercialApprovalConfiguration : IEntityTypeConfiguration<CommercialApproval>
{
    public void Configure(EntityTypeBuilder<CommercialApproval> builder)
    {
        builder.ToTable("commercial_approvals", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TargetEntityKind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.TargetEntityId).IsRequired();
        builder.Property(x => x.AuthorActorId).IsRequired();
        builder.Property(x => x.ApproverActorId).IsRequired();
        builder.Property(x => x.CosignNote).HasColumnType("text").IsRequired();
        builder.Property(x => x.ApprovedAtUtc).IsRequired();
        builder.Property(x => x.XminRowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.HasIndex(x => new { x.TargetEntityKind, x.TargetEntityId })
            .IsUnique()
            .HasDatabaseName("UX_commercial_approvals_target");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_approval_target_kind",
                "\"TargetEntityKind\"::text IN ('coupon','promotion')");
            t.HasCheckConstraint("chk_approval_separation_of_duties",
                "\"ApproverActorId\" <> \"AuthorActorId\"");
            t.HasCheckConstraint("chk_approval_note_min_length",
                "char_length(\"CosignNote\") >= 10");
        });
    }
}

public sealed class CommercialAuditEventConfiguration : IEntityTypeConfiguration<CommercialAuditEvent>
{
    public void Configure(EntityTypeBuilder<CommercialAuditEvent> builder)
    {
        builder.ToTable("commercial_audit_events", "pricing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TargetEntityKind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.TargetEntityId).IsRequired();
        builder.Property(x => x.Kind).HasColumnType("citext").IsRequired();
        builder.Property(x => x.ActorId).IsRequired();
        builder.Property(x => x.ActorRole).HasColumnType("citext").IsRequired();
        builder.Property(x => x.BeforeJson).HasColumnType("jsonb");
        builder.Property(x => x.AfterJson).HasColumnType("jsonb");
        builder.Property(x => x.DiffJson).HasColumnType("jsonb");
        builder.Property(x => x.ReasonNote).HasColumnType("text");
        builder.Property(x => x.RecordedAtUtc).IsRequired();
        builder.Property(x => x.CorrelationId);

        builder.HasIndex(x => new { x.TargetEntityKind, x.TargetEntityId, x.RecordedAtUtc })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_cae_target");
        builder.HasIndex(x => new { x.ActorId, x.RecordedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_cae_actor");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("chk_cae_target_kind",
                "\"TargetEntityKind\"::text IN ('coupon','promotion','campaign','business_pricing','preview_profile','commercial_threshold','commercial_approval')");
        });
        // Append-only trigger created in the migration via raw SQL.
    }
}
