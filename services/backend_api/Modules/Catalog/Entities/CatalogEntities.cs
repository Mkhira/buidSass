namespace BackendApi.Modules.Catalog.Entities;

public sealed class Category
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string OwnerId { get; set; } = "platform";
    public Guid? VendorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class CategoryClosure
{
    public Guid AncestorId { get; set; }
    public Guid DescendantId { get; set; }
    public int Depth { get; set; }
}

public sealed class CategoryAttributeSchema
{
    public Guid CategoryId { get; set; }
    public string SchemaJson { get; set; } = "{}";
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Brand
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public Guid? LogoMediaId { get; set; }
    public string OwnerId { get; set; } = "platform";
    public Guid? VendorId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class Manufacturer
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public Guid? LogoMediaId { get; set; }
    public string OwnerId { get; set; } = "platform";
    public Guid? VendorId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class Product
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public Guid BrandId { get; set; }
    public Guid? ManufacturerId { get; set; }
    public string SlugAr { get; set; } = string.Empty;
    public string SlugEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? ShortDescriptionAr { get; set; }
    public string? ShortDescriptionEn { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string AttributesJson { get; set; } = "{}";
    public string[] MarketCodes { get; set; } = Array.Empty<string>();
    public string Status { get; set; } = "draft";
    public bool Restricted { get; set; }
    public string? RestrictionReasonCode { get; set; }
    public string[] RestrictionMarkets { get; set; } = Array.Empty<string>();
    public long? PriceHintMinorUnits { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string OwnerId { get; set; } = "platform";
    public Guid? VendorId { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class ProductCategory
{
    public Guid ProductId { get; set; }
    public Guid CategoryId { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class ProductMedia
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public byte[] ContentSha256 { get; set; } = Array.Empty<byte>();
    public string MimeType { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
    public string? AltAr { get; set; }
    public string? AltEn { get; set; }
    public string VariantsJson { get; set; } = "{}";
    public string VariantStatus { get; set; } = "pending";
    public DateTimeOffset? VariantClaimedAt { get; set; }
    public int VariantAttempts { get; set; }
    public string OwnerId { get; set; } = "platform";
    public Guid? VendorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProductDocument
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string DocType { get; set; } = string.Empty;
    public string Locale { get; set; } = "en";
    public string StorageKey { get; set; } = string.Empty;
    public byte[] ContentSha256 { get; set; } = Array.Empty<byte>();
    public string? TitleAr { get; set; }
    public string? TitleEn { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProductStateTransition
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public Guid ActorAccountId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ScheduledPublish
{
    public Guid ProductId { get; set; }
    public DateTimeOffset PublishAt { get; set; }
    public Guid ScheduledByAccountId { get; set; }
    public DateTimeOffset ScheduledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? WorkerClaimedAt { get; set; }
    public DateTimeOffset? WorkerCompletedAt { get; set; }
}

public sealed class CatalogOutboxEntry
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CommittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DispatchedAt { get; set; }
}

/// <summary>
/// Per-row idempotency ledger for <c>POST /v1/admin/catalog/products/bulk-import</c>. Keyed by a
/// SHA-256 hash of (<c>X-Idempotency-Key</c> + row-index) so a retried NDJSON body with the same
/// idempotency key is a no-op rather than creating duplicate products.
/// </summary>
public sealed class BulkImportIdempotencyRecord
{
    public byte[] RowHash { get; set; } = Array.Empty<byte>();
    public Guid? ProductId { get; set; }
    public string Status { get; set; } = "ok";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
