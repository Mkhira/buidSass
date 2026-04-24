using BackendApi.Modules.Cart.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Cart.Persistence.Configurations;

public sealed class CartConfiguration : IEntityTypeConfiguration<Entities.Cart>
{
    public void Configure(EntityTypeBuilder<Entities.Cart> builder)
    {
        builder.ToTable("carts", "cart", t =>
        {
            t.HasCheckConstraint("CK_carts_status_enum", "\"Status\" IN ('active','archived','merged','purged')");
            t.HasCheckConstraint("CK_carts_identity_present", "\"AccountId\" IS NOT NULL OR \"CartTokenHash\" IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired();
        builder.Property(x => x.CouponCode).HasColumnType("citext");
        builder.Property(x => x.ArchivedReason).HasColumnType("citext");
        builder.Property(x => x.OwnerId).HasColumnType("citext").HasDefaultValue("platform").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        // One active cart per (account, market) — partial unique index
        builder.HasIndex(x => new { x.AccountId, x.MarketCode })
            .IsUnique()
            .HasFilter("\"Status\" = 'active' AND \"AccountId\" IS NOT NULL")
            .HasDatabaseName("IX_carts_account_market_active");
        // Anonymous cart lookup by token hash — unique: SHA-256 collision space makes duplicates
        // effectively impossible; enforcing uniqueness catches data-integrity bugs early.
        builder.HasIndex(x => x.CartTokenHash)
            .IsUnique()
            .HasFilter("\"Status\" = 'active' AND \"CartTokenHash\" IS NOT NULL")
            .HasDatabaseName("IX_carts_token_hash_active");
        builder.HasIndex(x => x.MarketCode);
        builder.HasIndex(x => x.LastTouchedAt);
        builder.HasIndex(x => x.Status);
    }
}

public sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        builder.ToTable("cart_lines", "cart", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_cart_lines_qty_positive", "\"Qty\" >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.RestrictionReasonCode).HasColumnType("citext");
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.CartId, x.ProductId }).IsUnique();
        builder.HasIndex(x => x.CartId);
        builder.HasIndex(x => x.MarketCode);
    }
}

public sealed class CartSavedItemConfiguration : IEntityTypeConfiguration<CartSavedItem>
{
    public void Configure(EntityTypeBuilder<CartSavedItem> builder)
    {
        builder.ToTable("cart_saved_items", "cart", t =>
        {
            t.HasCheckConstraint("CK_cart_saved_items_qty_positive", "\"Qty\" >= 1");
        });
        builder.HasKey(x => new { x.CartId, x.ProductId });
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Qty).HasDefaultValue(1).IsRequired();
        builder.HasIndex(x => x.MarketCode);
    }
}

public sealed class CartB2BMetadataConfiguration : IEntityTypeConfiguration<CartB2BMetadata>
{
    public void Configure(EntityTypeBuilder<CartB2BMetadata> builder)
    {
        builder.ToTable("cart_b2b_metadata", "cart");
        builder.HasKey(x => x.CartId);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.MarketCode);
    }
}

public sealed class CartAbandonedEmissionConfiguration : IEntityTypeConfiguration<CartAbandonedEmission>
{
    public void Configure(EntityTypeBuilder<CartAbandonedEmission> builder)
    {
        builder.ToTable("cart_abandoned_emissions", "cart");
        builder.HasKey(x => x.CartId);
        builder.Property(x => x.MarketCode).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.LastEmittedAt);
        builder.HasIndex(x => x.MarketCode);
    }
}
