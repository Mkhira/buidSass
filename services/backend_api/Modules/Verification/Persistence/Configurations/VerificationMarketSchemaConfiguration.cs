using BackendApi.Modules.Verification.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Verification.Persistence.Configurations;

/// <summary>EF configuration for <c>verification_market_schemas</c> per spec 020 data-model §2.4.</summary>
public sealed class VerificationMarketSchemaConfiguration : IEntityTypeConfiguration<VerificationMarketSchema>
{
    public void Configure(EntityTypeBuilder<VerificationMarketSchema> builder)
    {
        builder.ToTable("verification_market_schemas", "verification", t =>
        {
            t.HasCheckConstraint(
                "CK_verification_market_schemas_market_code_enum",
                "\"MarketCode\" IN ('eg','ksa')");
            t.HasCheckConstraint(
                "CK_verification_market_schemas_retention_non_negative",
                "\"RetentionMonths\" >= 0");
            t.HasCheckConstraint(
                "CK_verification_market_schemas_cooldown_non_negative",
                "\"CooldownDays\" >= 0");
            t.HasCheckConstraint(
                "CK_verification_market_schemas_expiry_positive",
                "\"ExpiryDays\" > 0");
            t.HasCheckConstraint(
                "CK_verification_market_schemas_sla_warning_le_decision",
                "\"SlaWarningBusinessDays\" <= \"SlaDecisionBusinessDays\"");
        });

        builder.HasKey(x => new { x.MarketCode, x.Version });

        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.EffectiveFrom).IsRequired();
        builder.Property(x => x.RequiredFieldsJson)
            .HasColumnType("jsonb")
            .HasColumnName("RequiredFields")
            .IsRequired();
        builder.Property(x => x.AllowedDocumentTypesJson)
            .HasColumnType("jsonb")
            .HasColumnName("AllowedDocumentTypes")
            .IsRequired();
        builder.Property(x => x.RetentionMonths).IsRequired();
        builder.Property(x => x.CooldownDays).IsRequired();
        builder.Property(x => x.ExpiryDays).IsRequired();
        builder.Property(x => x.ReminderWindowsDaysJson)
            .HasColumnType("jsonb")
            .HasColumnName("ReminderWindowsDays")
            .IsRequired();
        builder.Property(x => x.SlaDecisionBusinessDays).HasDefaultValue(2).IsRequired();
        builder.Property(x => x.SlaWarningBusinessDays).HasDefaultValue(1).IsRequired();
        builder.Property(x => x.HolidaysListJson)
            .HasColumnType("jsonb")
            .HasColumnName("HolidaysList")
            .IsRequired();

        // §2.4 unique partial — ≤ 1 active row per market.
        builder.HasIndex(x => x.MarketCode)
            .IsUnique()
            .HasDatabaseName("UX_verification_market_schemas_active_per_market")
            .HasFilter("\"EffectiveTo\" IS NULL");
    }
}
