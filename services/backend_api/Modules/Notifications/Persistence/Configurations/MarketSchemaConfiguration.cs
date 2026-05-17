using BackendApi.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Notifications.Persistence.Configurations;

public sealed class MarketSchemaConfiguration : IEntityTypeConfiguration<MarketSchema>
{
    public void Configure(EntityTypeBuilder<MarketSchema> builder)
    {
        builder.ToTable("market_schemas", "notifications", t =>
        {
            t.HasCheckConstraint("CK_market_schemas_market_code",
                @"""MarketCode"" IN ('sa','eg')");
            t.HasCheckConstraint("CK_market_schemas_rate_limits_positive",
                @"""RateLimitMarketingPer24h"" >= 0 AND ""RateLimitTransactionalPer24h"" >= 0");
        });

        builder.HasKey(x => x.MarketCode);
        builder.Property(x => x.MarketCode).HasColumnType("text");
        builder.Property(x => x.QuietHoursMarketingLocalStart).HasColumnType("time").IsRequired();
        builder.Property(x => x.QuietHoursMarketingLocalEnd).HasColumnType("time").IsRequired();
        builder.Property(x => x.QuietHoursTimezone).HasColumnType("text").IsRequired();
        builder.Property(x => x.UnsubscribeFooterAr).HasColumnType("text").IsRequired();
        builder.Property(x => x.UnsubscribeFooterEn).HasColumnType("text").IsRequired();
        builder.Property(x => x.RateLimitMarketingPer24h).IsRequired();
        builder.Property(x => x.RateLimitTransactionalPer24h).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
