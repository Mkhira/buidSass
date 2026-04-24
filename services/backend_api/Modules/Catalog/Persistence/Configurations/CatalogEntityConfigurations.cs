using BackendApi.Modules.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApi.Modules.Catalog.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Slug).HasColumnType("citext").IsRequired();
        builder.Property(x => x.NameAr).IsRequired();
        builder.Property(x => x.NameEn).IsRequired();
        builder.Property(x => x.OwnerId).HasColumnType("citext").HasDefaultValue("platform").IsRequired();
        builder.Property(x => x.DisplayOrder).HasDefaultValue(0);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.HasIndex(x => new { x.ParentId, x.Slug })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");
        builder.HasIndex(x => new { x.OwnerId, x.VendorId, x.IsActive });
        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public sealed class CategoryClosureConfiguration : IEntityTypeConfiguration<CategoryClosure>
{
    public void Configure(EntityTypeBuilder<CategoryClosure> builder)
    {
        builder.ToTable("category_closure", "catalog");
        builder.HasKey(x => new { x.AncestorId, x.DescendantId });
        builder.HasIndex(x => new { x.DescendantId, x.Depth });
    }
}

public sealed class CategoryAttributeSchemaConfiguration : IEntityTypeConfiguration<CategoryAttributeSchema>
{
    public void Configure(EntityTypeBuilder<CategoryAttributeSchema> builder)
    {
        builder.ToTable("category_attribute_schemas", "catalog");
        builder.HasKey(x => x.CategoryId);
        builder.Property(x => x.SchemaJson).HasColumnType("jsonb").IsRequired();
    }
}

public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Slug).HasColumnType("citext").IsRequired();
        builder.Property(x => x.NameAr).IsRequired();
        builder.Property(x => x.NameEn).IsRequired();
        builder.Property(x => x.OwnerId).HasColumnType("citext").HasDefaultValue("platform").IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public sealed class ManufacturerConfiguration : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> builder)
    {
        builder.ToTable("manufacturers", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Slug).HasColumnType("citext").IsRequired();
        builder.Property(x => x.NameAr).IsRequired();
        builder.Property(x => x.NameEn).IsRequired();
        builder.Property(x => x.OwnerId).HasColumnType("citext").HasDefaultValue("platform").IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Sku).HasColumnType("citext").IsRequired();
        builder.Property(x => x.SlugAr).HasColumnType("citext").IsRequired();
        builder.Property(x => x.SlugEn).HasColumnType("citext").IsRequired();
        builder.Property(x => x.NameAr).IsRequired();
        builder.Property(x => x.NameEn).IsRequired();
        builder.Property(x => x.AttributesJson).HasColumnName("attributes").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(x => x.MarketCodes).HasColumnType("citext[]").HasDefaultValueSql("'{}'::citext[]").IsRequired();
        builder.Property(x => x.Status).HasColumnType("citext").HasDefaultValue("draft").IsRequired();
        builder.Property(x => x.Restricted).HasDefaultValue(false);
        builder.Property(x => x.RestrictionReasonCode).HasColumnType("citext");
        builder.Property(x => x.RestrictionMarkets).HasColumnType("citext[]").HasDefaultValueSql("'{}'::citext[]").IsRequired();
        builder.Property(x => x.MinOrderQty).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MaxPerOrder).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.OwnerId).HasColumnType("citext").HasDefaultValue("platform").IsRequired();
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_products_min_order_qty_non_negative", "\"MinOrderQty\" >= 0");
            t.HasCheckConstraint("CK_products_max_per_order_non_negative", "\"MaxPerOrder\" >= 0");
        });
        builder.HasIndex(x => x.Sku).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
        builder.HasIndex(x => x.Barcode);
        builder.HasIndex(x => x.BrandId);
        builder.HasIndex(x => new { x.Status, x.MarketCodes });
        builder.HasIndex(x => new { x.Restricted, x.RestrictionMarkets });
        builder.HasIndex(x => x.AttributesJson).HasMethod("gin");
        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}

public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("product_categories", "catalog");
        builder.HasKey(x => new { x.ProductId, x.CategoryId });
        builder.HasIndex(x => new { x.ProductId, x.IsPrimary })
            .IsUnique()
            .HasFilter("\"IsPrimary\" = true");
    }
}

public sealed class ProductMediaConfiguration : IEntityTypeConfiguration<ProductMedia>
{
    public void Configure(EntityTypeBuilder<ProductMedia> builder)
    {
        builder.ToTable("product_media", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StorageKey).IsRequired();
        builder.Property(x => x.MimeType).IsRequired();
        builder.Property(x => x.VariantsJson).HasColumnName("variants").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        builder.Property(x => x.VariantStatus).HasColumnType("citext").HasDefaultValue("pending").IsRequired();
        builder.Property(x => x.OwnerId).HasColumnType("citext").HasDefaultValue("platform").IsRequired();
        builder.HasIndex(x => new { x.ProductId, x.DisplayOrder });
        builder.HasIndex(x => new { x.ProductId, x.IsPrimary })
            .IsUnique()
            .HasFilter("\"IsPrimary\" = true");
    }
}

public sealed class ProductDocumentConfiguration : IEntityTypeConfiguration<ProductDocument>
{
    public void Configure(EntityTypeBuilder<ProductDocument> builder)
    {
        builder.ToTable("product_documents", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DocType).HasColumnType("citext").IsRequired();
        builder.Property(x => x.Locale).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => new { x.ProductId, x.DocType, x.Locale }).IsUnique();
    }
}

public sealed class ProductStateTransitionConfiguration : IEntityTypeConfiguration<ProductStateTransition>
{
    public void Configure(EntityTypeBuilder<ProductStateTransition> builder)
    {
        builder.ToTable("product_state_transitions", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FromStatus).HasColumnType("citext").IsRequired();
        builder.Property(x => x.ToStatus).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => new { x.ProductId, x.OccurredAt });
    }
}

public sealed class ScheduledPublishConfiguration : IEntityTypeConfiguration<ScheduledPublish>
{
    public void Configure(EntityTypeBuilder<ScheduledPublish> builder)
    {
        builder.ToTable("scheduled_publishes", "catalog");
        builder.HasKey(x => x.ProductId);
        builder.HasIndex(x => new { x.PublishAt, x.WorkerClaimedAt });
    }
}

public sealed class CatalogOutboxEntryConfiguration : IEntityTypeConfiguration<CatalogOutboxEntry>
{
    public void Configure(EntityTypeBuilder<CatalogOutboxEntry> builder)
    {
        builder.ToTable("catalog_outbox", "catalog");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityByDefaultColumn();
        builder.Property(x => x.EventType).HasColumnType("citext").IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => x.CommittedAt);
        builder.HasIndex(x => x.DispatchedAt)
            .HasFilter("\"DispatchedAt\" IS NULL");
    }
}

public sealed class BulkImportIdempotencyRecordConfiguration : IEntityTypeConfiguration<BulkImportIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<BulkImportIdempotencyRecord> builder)
    {
        builder.ToTable("bulk_import_idempotency", "catalog");
        builder.HasKey(x => x.RowHash);
        builder.Property(x => x.Status).HasColumnType("citext").IsRequired();
    }
}
