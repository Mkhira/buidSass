using BackendApi.Modules.Shipping.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Shipping.Persistence.Configurations;

public sealed class ShippingMarketSchemaConfiguration
    : IEntityTypeConfiguration<ShippingMarketSchema>
{
    public void Configure(EntityTypeBuilder<ShippingMarketSchema> builder)
    {
        builder.ToTable("market_schemas", "shipping", t =>
        {
            t.HasCheckConstraint("CK_market_schemas_market",
                "\"MarketCode\" IN ('SA','EG')");
            t.HasCheckConstraint("CK_market_schemas_currency",
                "\"DefaultCurrency\" IN ('SAR','EGP')");
            t.HasCheckConstraint("CK_market_schemas_eta_days",
                "\"DefaultEtaDaysMin\" > 0 AND \"DefaultEtaDaysMax\" >= \"DefaultEtaDaysMin\"");
            t.HasCheckConstraint("CK_market_schemas_sla_hours",
                "\"SlaBreachThresholdHours\" > 0");
        });
        builder.HasKey(x => x.MarketCode);
        builder.Property(x => x.MarketCode).HasColumnType("text").IsRequired();
        builder.Property(x => x.PostalCodeRegex).HasColumnType("text");
        builder.Property(x => x.DefaultCurrency).HasColumnType("text").IsRequired();
        builder.Property(x => x.DefaultEtaDaysMin).IsRequired();
        builder.Property(x => x.DefaultEtaDaysMax).IsRequired();
        builder.Property(x => x.SlaBreachThresholdHours).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
    }
}
