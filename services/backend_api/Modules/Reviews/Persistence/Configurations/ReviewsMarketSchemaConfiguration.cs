using BackendApi.Modules.Reviews.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Reviews.Persistence.Configurations;

public sealed class ReviewsMarketSchemaConfiguration : IEntityTypeConfiguration<ReviewsMarketSchema>
{
    public void Configure(EntityTypeBuilder<ReviewsMarketSchema> builder)
    {
        builder.ToTable("reviews_market_schemas", "reviews", t =>
        {
            t.HasCheckConstraint("CK_rms_market_code", "\"MarketCode\" IN ('SA','EG')");
            t.HasCheckConstraint("CK_rms_eligibility_window",
                "\"EligibilityWindowDays\" BETWEEN 30 AND 730");
            t.HasCheckConstraint("CK_rms_edit_window",
                "\"EditWindowDays\" BETWEEN 7 AND 90");
            t.HasCheckConstraint("CK_rms_community_threshold",
                "\"CommunityReportThreshold\" BETWEEN 1 AND 10");
            t.HasCheckConstraint("CK_rms_qualifying_age",
                "\"ReportQualifyingAccountAgeDays\" BETWEEN 0 AND 90");
        });

        builder.HasKey(x => x.MarketCode);

        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.EligibilityWindowDays).IsRequired();
        builder.Property(x => x.EditWindowDays).IsRequired();
        builder.Property(x => x.CommunityReportThreshold).IsRequired();
        builder.Property(x => x.CommunityReportWindowDays).IsRequired();
        builder.Property(x => x.ReportQualifyingAccountAgeDays).IsRequired();
        builder.Property(x => x.ReportQualifyingRequiresVerifiedBuyer).IsRequired();
        builder.Property(x => x.PendingModerationSlaHours).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedByActorId).IsRequired();
        builder.Property(x => x.Xmin).IsRowVersion().HasColumnName("xmin");
    }
}
