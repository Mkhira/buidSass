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
            // SA → SAR, EG → EGP. Without this row-level pairing, a misconfig
            // could attach EGP to the SA market and quote in the wrong currency.
            t.HasCheckConstraint("CK_market_schemas_currency_matches_market",
                "(\"MarketCode\" = 'SA' AND \"DefaultCurrency\" = 'SAR') OR (\"MarketCode\" = 'EG' AND \"DefaultCurrency\" = 'EGP')");
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
