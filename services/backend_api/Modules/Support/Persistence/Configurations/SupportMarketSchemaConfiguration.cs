using BackendApi.Modules.Support.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Support.Persistence.Configurations;

public sealed class SupportMarketSchemaConfiguration : IEntityTypeConfiguration<SupportMarketSchema>
{
    public void Configure(EntityTypeBuilder<SupportMarketSchema> builder)
    {
        builder.ToTable("support_market_schemas", "support", t =>
        {
            t.HasCheckConstraint("CK_market_schemas_market_code", "\"MarketCode\" IN ('SA','EG')");
            t.HasCheckConstraint("CK_market_schemas_reopen_window",
                "\"ReopenWindowDays\" BETWEEN 0 AND 60");
            t.HasCheckConstraint("CK_market_schemas_max_reopen",
                "\"MaxReopenCount\" BETWEEN 1 AND 10");
            t.HasCheckConstraint("CK_market_schemas_auto_close",
                "\"AutoCloseAfterResolvedDays\" BETWEEN 0 AND 30");
            t.HasCheckConstraint("CK_market_schemas_attachment_caps",
                "\"AttachmentMaxPerTicket\" > 0 AND \"AttachmentMaxSizeMb\" > 0 AND \"AttachmentCumulativeMaxMb\" > 0");
        });

        builder.HasKey(x => x.MarketCode);
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.AutoAssignmentEnabled).IsRequired();
        builder.Property(x => x.ReopenWindowDays).IsRequired();
        builder.Property(x => x.MaxReopenCount).IsRequired();
        builder.Property(x => x.AutoCloseAfterResolvedDays).IsRequired();
        builder.Property(x => x.AttachmentMaxPerTicket).IsRequired();
        builder.Property(x => x.AttachmentMaxSizeMb).IsRequired();
        builder.Property(x => x.AttachmentCumulativeMaxMb).IsRequired();
        builder.Property(x => x.AllowedMimeTypes).HasColumnType("text[]").IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedByActorId);
    }
}
