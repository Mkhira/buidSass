using BackendApi.Modules.Verification.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Verification.Persistence.Configurations;

/// <summary>EF configuration for <c>verification_eligibility_cache</c> per spec 020 data-model §2.6.</summary>
public sealed class VerificationEligibilityCacheConfiguration : IEntityTypeConfiguration<VerificationEligibilityCache>
{
    public void Configure(EntityTypeBuilder<VerificationEligibilityCache> builder)
    {
        builder.ToTable("verification_eligibility_cache", "verification", t =>
        {
            t.HasCheckConstraint(
                "CK_verification_eligibility_cache_class_enum",
                "\"EligibilityClass\" IN ('eligible','ineligible','unrestricted_only')");
            t.HasCheckConstraint(
                "CK_verification_eligibility_cache_market_code_enum",
                "\"MarketCode\" IN ('eg','ksa')");
        });

        builder.HasKey(x => x.CustomerId);

        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.EligibilityClass).HasColumnType("text").IsRequired();
        builder.Property(x => x.ReasonCode).HasColumnType("text");
        builder.Property(x => x.ProfessionsJson)
            .HasColumnType("jsonb")
            .HasColumnName("Professions")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();
        builder.Property(x => x.ComputedAt).IsRequired();

        builder.HasIndex(x => new { x.MarketCode, x.EligibilityClass })
            .HasDatabaseName("IX_verification_eligibility_cache_market_class");
    }
}
