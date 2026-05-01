using BackendApi.Modules.Reviews.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Reviews.Persistence.Configurations;

public sealed class ReviewsFilterWordlistConfiguration : IEntityTypeConfiguration<ReviewsFilterWordlist>
{
    public void Configure(EntityTypeBuilder<ReviewsFilterWordlist> builder)
    {
        builder.ToTable("reviews_filter_wordlists", "reviews", t =>
        {
            t.HasCheckConstraint("CK_rfw_market_code", "\"MarketCode\" IN ('SA','EG')");
            t.HasCheckConstraint("CK_rfw_severity",
                "\"Severity\" IS NULL OR \"Severity\" IN ('block','warn')");
            t.HasCheckConstraint("CK_rfw_term_len", "char_length(\"Term\") BETWEEN 1 AND 200");
        });

        builder.HasKey(x => new { x.MarketCode, x.Term });

        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.Term).HasColumnType("text").IsRequired();
        builder.Property(x => x.Severity).HasColumnType("text");
        builder.Property(x => x.CreatedByActorId).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.MarketCode).HasDatabaseName("IX_rfw_market");
    }
}
